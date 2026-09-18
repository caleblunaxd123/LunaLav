using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Services;

/// <summary>Procesa la cola de Marketing mientras la API está encendida. Solo ejecuta tareas internas seguras.</summary>
public sealed class MarketingAutomationWorker(ISqlConnectionFactory db, ILogger<MarketingAutomationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProgramarReglasAsync(stoppingToken); await ProcesarUnoAsync(stoppingToken); }
            catch (Exception ex) { log.LogError(ex,"Error en worker de Marketing"); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ProgramarReglasAsync(CancellationToken ct)
    {
        await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();
        q.CommandText=@"DECLARE @now datetime2=SYSUTCDATETIME();
        INSERT automation.Job(JobType,Payload,IdempotencyKey,AvailableAt)
        SELECT r.JobType,N'{}',CONCAT(r.JobType,N':',CONVERT(char(16),@now,120)),@now
        FROM marketing.AutomationRule r
        WHERE r.Activa=1 AND (r.UltimaEjecucion IS NULL OR DATEADD(MINUTE,r.FrecuenciaMinutos,r.UltimaEjecucion)<=@now);
        UPDATE r SET UltimaEjecucion=@now FROM marketing.AutomationRule r WHERE r.Activa=1 AND (r.UltimaEjecucion IS NULL OR DATEADD(MINUTE,r.FrecuenciaMinutos,r.UltimaEjecucion)<=@now);";
        try { await q.ExecuteNonQueryAsync(ct); } catch (SqlException e) when (e.Number is 2627 or 2601) { /* misma ventana ya programada */ }
    }

    private async Task ProcesarUnoAsync(CancellationToken ct)
    {
        await using var c=db.Create();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        long id=0; string type=string.Empty; bool found;
        await using (var claim=c.CreateCommand())
        {
            claim.Transaction=(SqlTransaction)tx;
            claim.CommandText=@";WITH nextJob AS(SELECT TOP(1)* FROM automation.Job WITH(UPDLOCK,READPAST,ROWLOCK) WHERE Status=N'PENDING' AND AvailableAt<=SYSUTCDATETIME() ORDER BY CreatedAt)
            UPDATE nextJob SET Status=N'RUNNING',Attempts=Attempts+1,LockedUntil=DATEADD(MINUTE,5,SYSUTCDATETIME()) OUTPUT inserted.Id,inserted.JobType;";
            await using var r=await claim.ExecuteReaderAsync(ct);
            found=await r.ReadAsync(ct);
            if(found){id=r.GetInt64(0);type=r.GetString(1);}
        }
        // El reader debe estar cerrado antes de confirmar la transacción, incluso cuando
        // no había trabajo. SQL Server rechaza Commit si el DataReader sigue abierto.
        if(!found){await tx.CommitAsync(ct);return;}
        try
        {
            await EjecutarSeguroAsync(c,(SqlTransaction)tx,type,ct);
            await using var done=c.CreateCommand();done.Transaction=(SqlTransaction)tx;done.CommandText="UPDATE automation.Job SET Status=N'COMPLETED',CompletedAt=SYSUTCDATETIME(),LockedUntil=NULL WHERE Id=@id";done.AddParam("@id",id);await done.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
        }
        catch(Exception ex)
        {
            await tx.RollbackAsync(ct);await using var fail=c.CreateCommand();fail.CommandText="UPDATE automation.Job SET Status=CASE WHEN Attempts>=MaxAttempts THEN N'FAILED' ELSE N'PENDING' END,LastError=@e,LockedUntil=NULL,AvailableAt=DATEADD(MINUTE,5,SYSUTCDATETIME()) WHERE Id=@id";fail.AddParam("@e",ex.Message[..Math.Min(ex.Message.Length,1900)]);fail.AddParam("@id",id);await fail.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task EjecutarSeguroAsync(SqlConnection c,SqlTransaction tx,string type,CancellationToken ct)
    {
        await using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText=type switch
        {
            "MARKETING_SCORE_RECALCULATION" => @"UPDATE marketing.Prospect SET Score=CASE WHEN (CASE WHEN NULLIF(Whatsapp,N'') IS NOT NULL THEN 10 ELSE 0 END+CASE WHEN NULLIF(Instagram,N'') IS NOT NULL THEN 8 ELSE 0 END+CASE WHEN NULLIF(SitioWeb,N'') IS NOT NULL THEN 5 ELSE 0 END+CASE WHEN ISNULL(NumeroSedesEstimado,0)>1 THEN 20 ELSE 0 END+CASE WHEN TieneDelivery=1 THEN 10 ELSE 0 END+CASE WHEN ISNULL(NumeroResenas,0)>=100 THEN 10 ELSE 0 END+CASE WHEN ISNULL(Rating,0)>4 THEN 5 ELSE 0 END)>100 THEN 100 ELSE (CASE WHEN NULLIF(Whatsapp,N'') IS NOT NULL THEN 10 ELSE 0 END+CASE WHEN NULLIF(Instagram,N'') IS NOT NULL THEN 8 ELSE 0 END+CASE WHEN NULLIF(SitioWeb,N'') IS NOT NULL THEN 5 ELSE 0 END+CASE WHEN ISNULL(NumeroSedesEstimado,0)>1 THEN 20 ELSE 0 END+CASE WHEN TieneDelivery=1 THEN 10 ELSE 0 END+CASE WHEN ISNULL(NumeroResenas,0)>=100 THEN 10 ELSE 0 END+CASE WHEN ISNULL(Rating,0)>4 THEN 5 ELSE 0 END) END,FechaActualizacion=SYSUTCDATETIME() WHERE Activo=1;",
            "MARKETING_FOLLOWUP_REVIEW" => "UPDATE marketing.FollowUp SET Prioridad=N'ALTA' WHERE Completado=0 AND FechaProgramada<SYSUTCDATETIME() AND Prioridad<>N'ALTA';",
            "MARKETING_PREPARE_OUTREACH" => "INSERT agents.Approval(AgentId,ActionType,Payload,Risk,Reason) SELECT TOP(1) a.Id,N'PREPARAR_CONTACTOS',N'{}',N'MEDIUM',N'Requiere aprobación humana antes de generar o enviar contactos.' FROM agents.AgentDefinition a WHERE a.Nombre=N'Alex' AND NOT EXISTS(SELECT 1 FROM agents.Approval WHERE ActionType=N'PREPARAR_CONTACTOS' AND Status=N'PENDING');",
            _ => throw new InvalidOperationException("Tipo de automatización no permitido.")
        };
        await q.ExecuteNonQueryAsync(ct);
    }
}
