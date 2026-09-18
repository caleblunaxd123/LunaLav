using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

public record MarketingDiscoverRequest(string? Zona, int? Max);

[ApiController,Authorize(Policy="Marketing"),Route("api/marketing/automation")]
public class MarketingAutomationController(ISqlConnectionFactory db, OpenStreetMapPlacesService osm, OllamaService ollama) : ControllerBase
{
    [HttpPost("discover")]
    public async Task<IActionResult> Discover([FromBody] MarketingDiscoverRequest r, CancellationToken ct)
    {
        var zona = string.IsNullOrWhiteSpace(r.Zona) ? "Lima" : r.Zona.Trim();
        var max = Math.Clamp(r.Max ?? 15, 1, 25);
        IReadOnlyList<GooglePlaceResult> found;
        try { found = await osm.SearchLaundriesAsync($"lavanderías {zona}", max, ct); }
        catch (InvalidOperationException e) { return StatusCode(503, new { mensaje = e.Message }); }
        int? uid = int.TryParse(User.FindFirst("marketingUserId")?.Value, out var u) ? u : null;
        var agregados = 0; var omitidos = 0; var nombres = new List<string>();
        await using var c = db.Create(); await c.OpenAsync(ct);
        foreach (var p in found)
        {
            if (string.IsNullOrWhiteSpace(p.Nombre)) continue;
            await using (var dup = c.CreateCommand())
            {
                dup.CommandText = "SELECT COUNT(*) FROM marketing.Prospect WHERE NombreComercial=@n AND ISNULL(Distrito,N'')=@d AND Activo=1";
                dup.AddParam("@n", p.Nombre); dup.AddParam("@d", zona);
                if (Convert.ToInt32(await dup.ExecuteScalarAsync(ct)) > 0) { omitidos++; continue; }
            }
            await using var ins = c.CreateCommand();
            ins.CommandText = @"INSERT marketing.Prospect(NombreComercial,Direccion,Distrito,Telefono,SitioWeb,Latitud,Longitud,Rating,NumeroResenas,Estado,Prioridad,Score,Fuente,FormaTrabajoActual,ResponsableId)
                VALUES(@n,@dir,@d,@tel,@web,@lat,@lng,@rating,@res,N'NUEVO',N'MEDIA',0,N'OPENSTREETMAP',N'DESCONOCIDO',@uid)";
            ins.AddParam("@n", p.Nombre); ins.AddParam("@dir", p.Direccion); ins.AddParam("@d", zona); ins.AddParam("@tel", p.Telefono);
            ins.AddParam("@web", p.SitioWeb); ins.AddParam("@lat", p.Latitud); ins.AddParam("@lng", p.Longitud);
            ins.AddParam("@rating", p.Rating); ins.AddParam("@res", p.Resenas); ins.AddParam("@uid", uid);
            await ins.ExecuteNonQueryAsync(ct);
            agregados++; if (nombres.Count < 8) nombres.Add(p.Nombre);
        }
        string? guion = null;
        if (agregados > 0)
        {
            try { guion = await ollama.GenerarLibreAsync($"En máximo 2 frases y en español, redacta un primer mensaje breve y cordial para contactar por WhatsApp a una lavandería de {zona} y ofrecerle LunaLav, un sistema de gestión (software) para lavanderías. No inventes datos. Devuelve solo el mensaje, sin comillas.", "llama3.2:latest", ct); }
            catch { /* la IA es opcional aquí */ }
        }
        return Ok(new { agregados, omitidos, encontrados = found.Count, zona, nombres, guion });
    }

    [HttpGet("rules")]
    public async Task<ActionResult<List<MarketingAutomationRuleDto>>> Rules(CancellationToken ct){await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT Id,Nombre,JobType,FrecuenciaMinutos,Activa,RequiereAprobacion,UltimaEjecucion FROM marketing.AutomationRule ORDER BY Id";return Ok(await q.ReadListAsync(r=>new MarketingAutomationRuleDto(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetBoolean(4),r.GetBoolean(5),r.GetNullableDateTime("UltimaEjecucion")),ct));}
    [HttpPatch("rules/{id:int}")]
    public async Task<IActionResult> Toggle(int id,[FromBody] bool activa,CancellationToken ct){await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE marketing.AutomationRule SET Activa=@a WHERE Id=@id";q.AddParam("@a",activa);q.AddParam("@id",id);return await q.ExecuteNonQueryAsync(ct)==0?NotFound():NoContent();}
    [HttpGet("approvals")]
    public async Task<ActionResult<List<MarketingApprovalDto>>> Approvals(CancellationToken ct){await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT Id,ActionType,Risk,Status,Reason,RequestedAt,ReviewedBy,ReviewedAt FROM agents.Approval ORDER BY CASE WHEN Status=N'PENDING' THEN 0 ELSE 1 END,RequestedAt DESC";return Ok(await q.ReadListAsync(r=>new MarketingApprovalDto(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetNullableString("Reason"),r.GetDateTime(5),r.GetNullableString("ReviewedBy"),r.GetNullableDateTime("ReviewedAt")),ct));}
    [HttpPatch("approvals/{id:long}")]
    public async Task<IActionResult> Decide(long id,[FromBody] MarketingStatusRequest r,CancellationToken ct){var s=r.Estado.Trim().ToUpperInvariant();if(s is not("APPROVED" or "REJECTED"))return BadRequest(new{mensaje="Decisión inválida."});await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE agents.Approval SET Status=@s,ReviewedBy=@u,ReviewedAt=SYSUTCDATETIME() WHERE Id=@id AND Status=N'PENDING'";q.AddParam("@s",s);q.AddParam("@u",User.Identity?.Name??"marketing");q.AddParam("@id",id);return await q.ExecuteNonQueryAsync(ct)==0?NotFound():NoContent();}
    [HttpGet("status")]
    public async Task<ActionResult<MarketingAgentStatusDto>> Status(CancellationToken ct)
    {
        await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();
        q.CommandText=@"SELECT SUM(CASE WHEN Status=N'PENDING' THEN 1 ELSE 0 END),SUM(CASE WHEN Status=N'RUNNING' THEN 1 ELSE 0 END),SUM(CASE WHEN Status=N'COMPLETED' AND CompletedAt>=DATEADD(day,-1,SYSUTCDATETIME()) THEN 1 ELSE 0 END),SUM(CASE WHEN Status=N'FAILED' THEN 1 ELSE 0 END) FROM automation.Job;
        SELECT TOP 8 Id,JobType,Status,CreatedAt,CompletedAt,LastError FROM automation.Job ORDER BY CreatedAt DESC;";
        await using var rd=await q.ExecuteReaderAsync(ct);
        int pend=0,run=0,comp=0,fail=0;
        if(await rd.ReadAsync(ct)){pend=rd.IsDBNull(0)?0:rd.GetInt32(0);run=rd.IsDBNull(1)?0:rd.GetInt32(1);comp=rd.IsDBNull(2)?0:rd.GetInt32(2);fail=rd.IsDBNull(3)?0:rd.GetInt32(3);}
        var recientes=new List<MarketingAgentJobDto>();
        if(await rd.NextResultAsync(ct))while(await rd.ReadAsync(ct))recientes.Add(new MarketingAgentJobDto(rd.GetInt64(0),rd.GetString(1),rd.GetString(2),rd.GetDateTime(3),rd.IsDBNull(4)?null:rd.GetDateTime(4),rd.IsDBNull(5)?null:rd.GetString(5)));
        return Ok(new MarketingAgentStatusDto(pend,run,comp,fail,recientes));
    }

    [HttpPost("jobs/{type}")]
    public async Task<IActionResult> Run(string type,CancellationToken ct){var allowed=new[]{"MARKETING_SCORE_RECALCULATION","MARKETING_FOLLOWUP_REVIEW","MARKETING_PREPARE_OUTREACH"};if(!allowed.Contains(type,StringComparer.OrdinalIgnoreCase))return BadRequest(new{mensaje="Automatización no permitida."});await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="INSERT automation.Job(JobType,Payload,IdempotencyKey) VALUES(@t,N'{}',CONCAT(@t,N':manual:',NEWID()))";q.AddParam("@t",type.ToUpperInvariant());await q.ExecuteNonQueryAsync(ct);return Accepted();}
}
