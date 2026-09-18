using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController, Authorize(Policy = "Marketing"), Route("api/marketing")]
public class MarketingOperationsController(ISqlConnectionFactory db) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirst("marketingUserId")!.Value);

    [HttpGet("campaigns")]
    public async Task<ActionResult<List<MarketingCampaignDto>>> Campaigns(CancellationToken ct)
    {
        await using var c=db.Create(); await c.OpenAsync(ct); await using var q=c.CreateCommand();
        q.CommandText="SELECT Id,Nombre,Audiencia,Canal,Asunto,Mensaje,Estado,FechaProgramada,FechaCreacion FROM marketing.Campaign ORDER BY FechaCreacion DESC";
        return Ok(await q.ReadListAsync(r=>new MarketingCampaignDto(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetNullableString("Asunto"),r.GetNullableString("Mensaje"),r.GetString(6),r.GetNullableDateTime("FechaProgramada"),r.GetDateTime(8)),ct));
    }

    [HttpPost("campaigns")]
    public async Task<ActionResult> CreateCampaign(MarketingCampaignRequest r,CancellationToken ct)
    {
        await using var c=db.Create(); await c.OpenAsync(ct); await using var q=c.CreateCommand();
        q.CommandText="INSERT marketing.Campaign(Nombre,Audiencia,Canal,Asunto,Mensaje,FechaProgramada,CreadoPorId) OUTPUT INSERTED.Id VALUES(@n,@a,@c,@s,@m,@f,@u)";
        q.AddParam("@n",r.Nombre.Trim());q.AddParam("@a",r.Audiencia.Trim());q.AddParam("@c",r.Canal.Trim().ToUpperInvariant());q.AddParam("@s",r.Asunto);q.AddParam("@m",r.Mensaje);q.AddParam("@f",r.FechaProgramada);q.AddParam("@u",UserId);
        var id=Convert.ToInt64(await q.ExecuteScalarAsync(ct)); return Created($"/api/marketing/campaigns/{id}",new{id});
    }

    [HttpPatch("campaigns/{id:long}/status")]
    public async Task<IActionResult> CampaignStatus(long id,[FromBody] MarketingStatusRequest r,CancellationToken ct)
    {
        var state=r.Estado.Trim().ToUpperInvariant(); if(state is not ("BORRADOR" or "PENDIENTE_APROBACION" or "APROBADA" or "PAUSADA" or "FINALIZADA")) return BadRequest(new{mensaje="Estado de campaña inválido."});
        await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE marketing.Campaign SET Estado=@e,FechaActualizacion=SYSUTCDATETIME() WHERE Id=@id";q.AddParam("@e",state);q.AddParam("@id",id);return await q.ExecuteNonQueryAsync(ct)==0?NotFound():NoContent();
    }

    [HttpGet("drafts")]
    public async Task<ActionResult<List<MarketingDraftDto>>> Drafts(CancellationToken ct)
    {
        await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT d.Id,d.ProspectId,p.NombreComercial,d.Canal,d.Destinatario,d.Asunto,d.Cuerpo,d.Estado,d.FechaCreacion FROM marketing.MessageDraft d LEFT JOIN marketing.Prospect p ON p.Id=d.ProspectId ORDER BY d.FechaCreacion DESC";
        return Ok(await q.ReadListAsync(r=>new MarketingDraftDto(r.GetInt64(0),r.IsDBNull(1)?null:r.GetInt64(1),r.GetNullableString("NombreComercial"),r.GetString(3),r.GetString(4),r.GetNullableString("Asunto"),r.GetString(6),r.GetString(7),r.GetDateTime(8)),ct));
    }

    [HttpPost("drafts")]
    public async Task<ActionResult> CreateDraft(MarketingDraftRequest r,CancellationToken ct)
    {
        await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="INSERT marketing.MessageDraft(ProspectId,Canal,Destinatario,Asunto,Cuerpo,CreadoPorId) OUTPUT INSERTED.Id VALUES(@p,@c,@d,@a,@b,@u)";q.AddParam("@p",r.ProspectId);q.AddParam("@c",r.Canal.Trim().ToUpperInvariant());q.AddParam("@d",r.Destinatario.Trim());q.AddParam("@a",r.Asunto);q.AddParam("@b",r.Cuerpo.Trim());q.AddParam("@u",UserId);var id=Convert.ToInt64(await q.ExecuteScalarAsync(ct));return Created($"/api/marketing/drafts/{id}",new{id});
    }

    [HttpPatch("drafts/{id:long}/status")]
    public async Task<IActionResult> DraftStatus(long id,[FromBody] MarketingStatusRequest r,CancellationToken ct)
    {
        var state=r.Estado.Trim().ToUpperInvariant();if(state is not ("BORRADOR" or "PENDIENTE_APROBACION" or "APROBADO" or "ARCHIVADO"))return BadRequest(new{mensaje="Estado de borrador inválido."});await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE marketing.MessageDraft SET Estado=@e,FechaActualizacion=SYSUTCDATETIME() WHERE Id=@id";q.AddParam("@e",state);q.AddParam("@id",id);return await q.ExecuteNonQueryAsync(ct)==0?NotFound():NoContent();
    }

    [HttpGet("agent-settings")]
    public async Task<ActionResult<MarketingAgentSettingDto>> AgentSettings(CancellationToken ct)
    {
        await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT Nombre,AnalizarProspectos,GenerarBorradores,CrearSeguimientos,PrimerContactoAutomatico,SeguimientosAutomaticos,LimiteDiario FROM marketing.AgentSetting WHERE Id=1";await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return NotFound();return Ok(new MarketingAgentSettingDto(r.GetString(0),r.GetBoolean(1),r.GetBoolean(2),r.GetBoolean(3),r.GetBoolean(4),r.GetBoolean(5),r.GetInt32(6)));
    }

    [HttpPut("agent-settings")]
    public async Task<IActionResult> SaveAgentSettings(MarketingAgentSettingDto r,CancellationToken ct)
    {
        await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE marketing.AgentSetting SET Nombre=@n,AnalizarProspectos=@a,GenerarBorradores=@b,CrearSeguimientos=@c,PrimerContactoAutomatico=@p,SeguimientosAutomaticos=@s,LimiteDiario=@l,FechaActualizacion=SYSUTCDATETIME() WHERE Id=1";q.AddParam("@n",r.Nombre.Trim());q.AddParam("@a",r.AnalizarProspectos);q.AddParam("@b",r.GenerarBorradores);q.AddParam("@c",r.CrearSeguimientos);q.AddParam("@p",r.PrimerContactoAutomatico);q.AddParam("@s",r.SeguimientosAutomaticos);q.AddParam("@l",Math.Clamp(r.LimiteDiario,1,100));await q.ExecuteNonQueryAsync(ct);return NoContent();
    }
}
