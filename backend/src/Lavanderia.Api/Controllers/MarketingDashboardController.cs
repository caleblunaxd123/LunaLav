using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController, Authorize(Policy = "Marketing"), Route("api/marketing/dashboard")]
public class MarketingDashboardController : ControllerBase
{
    private readonly ISqlConnectionFactory _db;
    public MarketingDashboardController(ISqlConnectionFactory db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<MarketingDashboardDto>> Obtener(CancellationToken ct)
    {
        await using var conn = _db.Create(); await conn.OpenAsync(ct); await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT
          COUNT(*) Total, SUM(CASE WHEN Estado=N'NUEVO' THEN 1 ELSE 0 END) Nuevos,
          SUM(CASE WHEN FechaUltimoContacto IS NOT NULL THEN 1 ELSE 0 END) Contactados,
          SUM(CASE WHEN Estado=N'INTERESADO' THEN 1 ELSE 0 END) Interesados,
          SUM(CASE WHEN Estado IN(N'DEMO_PROGRAMADA',N'DEMO_REALIZADA') THEN 1 ELSE 0 END) Demos,
          SUM(CASE WHEN Estado=N'GANADO' THEN 1 ELSE 0 END) Ganados
          FROM marketing.Prospect WHERE Activo=1;
          SELECT COUNT(*) Pendientes, SUM(CASE WHEN FechaProgramada<SYSUTCDATETIME() THEN 1 ELSE 0 END) Vencidos FROM marketing.FollowUp WHERE Completado=0;
          SELECT COUNT(*) FROM marketing.Task WHERE Estado=N'PENDIENTE';
          WITH dias AS (SELECT CAST(DATEADD(DAY,-n,CAST(SYSUTCDATETIME() AS date)) AS date) d FROM (VALUES(6),(5),(4),(3),(2),(1),(0)) v(n))
          SELECT d.d Dia, COUNT(p.Id) Total FROM dias d
          LEFT JOIN marketing.Prospect p ON CAST(p.FechaCreacion AS date)=d.d AND p.Activo=1
          GROUP BY d.d ORDER BY d.d;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var total = 0; var nuevos = 0; var contactados = 0; var interesados = 0; var demos = 0; var ganados = 0; var pendientes = 0; var vencidos = 0; var tareas = 0;
        if (await reader.ReadAsync(ct)) { total = reader.GetInt32(0); nuevos = reader.IsDBNull(1) ? 0 : reader.GetInt32(1); contactados = reader.IsDBNull(2) ? 0 : reader.GetInt32(2); interesados = reader.IsDBNull(3) ? 0 : reader.GetInt32(3); demos = reader.IsDBNull(4) ? 0 : reader.GetInt32(4); ganados = reader.IsDBNull(5) ? 0 : reader.GetInt32(5); }
        if (await reader.NextResultAsync(ct) && await reader.ReadAsync(ct)) { pendientes = reader.GetInt32(0); vencidos = reader.IsDBNull(1) ? 0 : reader.GetInt32(1); }
        if (await reader.NextResultAsync(ct) && await reader.ReadAsync(ct)) tareas = reader.GetInt32(0);
        var serie = new List<MarketingSeriePuntoDto>();
        if (await reader.NextResultAsync(ct)) while (await reader.ReadAsync(ct)) serie.Add(new MarketingSeriePuntoDto(reader.GetDateTime(0), reader.GetInt32(1)));
        var next = await PrioridadesAsync(ct);
        return Ok(new MarketingDashboardDto(total,nuevos,contactados,interesados,demos,ganados,pendientes,vencidos,tareas,next,serie));
    }

    private async Task<IReadOnlyList<MarketingNextActionDto>> PrioridadesAsync(CancellationToken ct)
    {
        await using var conn = _db.Create(); await conn.OpenAsync(ct); await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT TOP 6 p.Id,p.NombreComercial,p.Estado,p.Score,p.Distrito,f.FechaProgramada,
          CASE WHEN f.Id IS NOT NULL AND f.FechaProgramada<SYSUTCDATETIME() THEN N'Seguimiento vencido'
               WHEN f.Id IS NOT NULL THEN N'Seguimiento programado'
               WHEN p.FechaUltimoContacto IS NULL THEN N'Aún no fue contactado'
               ELSE N'Revisar oportunidad' END
          FROM marketing.Prospect p OUTER APPLY(SELECT TOP 1 Id,FechaProgramada FROM marketing.FollowUp WHERE ProspectId=p.Id AND Completado=0 ORDER BY FechaProgramada) f
          WHERE p.Activo=1 ORDER BY CASE WHEN f.Id IS NOT NULL AND f.FechaProgramada<SYSUTCDATETIME() THEN 0 WHEN p.FechaUltimoContacto IS NULL THEN 1 ELSE 2 END,p.Score DESC,p.FechaCreacion DESC";
        return await cmd.ReadListAsync(r => new MarketingNextActionDto(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetNullableString("Distrito"),r.GetNullableDateTime("FechaProgramada"),r.GetString(6)), ct);
    }
}
