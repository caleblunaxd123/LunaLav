using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController,Authorize(Policy="Marketing"),Route("api/marketing/automation")]
public class MarketingAutomationController(ISqlConnectionFactory db) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<ActionResult<List<MarketingAutomationRuleDto>>> Rules(CancellationToken ct){await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT Id,Nombre,JobType,FrecuenciaMinutos,Activa,RequiereAprobacion,UltimaEjecucion FROM marketing.AutomationRule ORDER BY Id";return Ok(await q.ReadListAsync(r=>new MarketingAutomationRuleDto(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetBoolean(4),r.GetBoolean(5),r.GetNullableDateTime("UltimaEjecucion")),ct));}
    [HttpPatch("rules/{id:int}")]
    public async Task<IActionResult> Toggle(int id,[FromBody] bool activa,CancellationToken ct){await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE marketing.AutomationRule SET Activa=@a WHERE Id=@id";q.AddParam("@a",activa);q.AddParam("@id",id);return await q.ExecuteNonQueryAsync(ct)==0?NotFound():NoContent();}
    [HttpGet("approvals")]
    public async Task<ActionResult<List<MarketingApprovalDto>>> Approvals(CancellationToken ct){await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT Id,ActionType,Risk,Status,Reason,RequestedAt,ReviewedBy,ReviewedAt FROM agents.Approval ORDER BY CASE WHEN Status=N'PENDING' THEN 0 ELSE 1 END,RequestedAt DESC";return Ok(await q.ReadListAsync(r=>new MarketingApprovalDto(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetNullableString("Reason"),r.GetDateTime(5),r.GetNullableString("ReviewedBy"),r.GetNullableDateTime("ReviewedAt")),ct));}
    [HttpPatch("approvals/{id:long}")]
    public async Task<IActionResult> Decide(long id,[FromBody] MarketingStatusRequest r,CancellationToken ct){var s=r.Estado.Trim().ToUpperInvariant();if(s is not("APPROVED" or "REJECTED"))return BadRequest(new{mensaje="Decisión inválida."});await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE agents.Approval SET Status=@s,ReviewedBy=@u,ReviewedAt=SYSUTCDATETIME() WHERE Id=@id AND Status=N'PENDING'";q.AddParam("@s",s);q.AddParam("@u",User.Identity?.Name??"marketing");q.AddParam("@id",id);return await q.ExecuteNonQueryAsync(ct)==0?NotFound():NoContent();}
    [HttpPost("jobs/{type}")]
    public async Task<IActionResult> Run(string type,CancellationToken ct){var allowed=new[]{"MARKETING_SCORE_RECALCULATION","MARKETING_FOLLOWUP_REVIEW","MARKETING_PREPARE_OUTREACH"};if(!allowed.Contains(type,StringComparer.OrdinalIgnoreCase))return BadRequest(new{mensaje="Automatización no permitida."});await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="INSERT automation.Job(JobType,Payload,IdempotencyKey) VALUES(@t,N'{}',CONCAT(@t,N':manual:',NEWID()))";q.AddParam("@t",type.ToUpperInvariant());await q.ExecuteNonQueryAsync(ct);return Accepted();}
}
