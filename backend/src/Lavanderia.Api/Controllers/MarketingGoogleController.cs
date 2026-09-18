using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController,Authorize(Policy="Marketing"),Route("api/marketing/google")]
public class MarketingGoogleController(GooglePlacesService places, GmailOAuthService gmail, OpenStreetMapPlacesService osm) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)=>Ok(new{placesConfigured=places.Configured,descubrimientoGratis=true,proveedorDescubrimiento=places.Configured?"Google Places":"OpenStreetMap",gmail=await gmail.StatusAsync(ct),gmailMessage="Gmail usa consentimiento OAuth; LunaLav no recibe ni almacena tu contraseña."});
    [HttpPost("gmail/connect")]
    public async Task<IActionResult> ConnectGmail(CancellationToken ct)
    {
        try { return Ok(new { authorizationUrl = await gmail.StartAsync(User.Identity?.Name ?? "marketing", ct) }); }
        catch(InvalidOperationException e) { return StatusCode(503,new { mensaje=e.Message }); }
    }
    [HttpPost("gmail/sync")]
    public async Task<IActionResult> Sync(CancellationToken ct){try{return Ok(new{synced=await gmail.SyncInboxAsync(ct)});}catch(InvalidOperationException e){return StatusCode(503,new{mensaje=e.Message});}}
    [HttpGet("gmail/inbox")]
    public async Task<IActionResult> Inbox(CancellationToken ct)=>Ok(await gmail.InboxAsync(ct));
    [HttpGet("gmail/thread")]
    public async Task<IActionResult> Thread([FromQuery] string threadId,CancellationToken ct)
    { if(string.IsNullOrWhiteSpace(threadId))return BadRequest(new{mensaje="Falta el hilo."}); try{return Ok(await gmail.ThreadMessagesAsync(threadId.Trim(),ct));}catch(InvalidOperationException e){return StatusCode(503,new{mensaje=e.Message});} }
    [HttpPost("gmail/send")]
    public async Task<IActionResult> Send([FromBody] GmailSendRequest r,CancellationToken ct)
    { try{ var (id,threadId)=await gmail.SendAsync(r.To,r.Subject,r.Body,null,null,null,r.Attachments,ct); return Ok(new{id,threadId,enviado=true}); }catch(InvalidOperationException e){return StatusCode(503,new{mensaje=e.Message});} }
    [HttpPost("gmail/reply")]
    public async Task<IActionResult> Reply([FromBody] GmailReplyRequest r,CancellationToken ct)
    { if(string.IsNullOrWhiteSpace(r.ThreadId))return BadRequest(new{mensaje="Falta el hilo."}); try{ var (id,threadId)=await gmail.ReplyAsync(r.ThreadId.Trim(),r.Body,ct); return Ok(new{id,threadId,enviado=true}); }catch(InvalidOperationException e){return StatusCode(503,new{mensaje=e.Message});} }
    [HttpGet("places/search")]
    public async Task<IActionResult> Search([FromQuery] string? q,[FromQuery] double? lat,[FromQuery] double? lng,[FromQuery] int max=10,CancellationToken ct=default)
    {
        try{
            if(lat.HasValue && lng.HasValue)
                return Ok(await osm.SearchAroundAsync((decimal)lat.Value,(decimal)lng.Value,max,ct));
            if(string.IsNullOrWhiteSpace(q))return BadRequest(new{mensaje="Indica una búsqueda o comparte tu ubicación."});
            return Ok(places.Configured ? await places.SearchTextAsync(q.Trim(),max,ct) : await osm.SearchLaundriesAsync(q.Trim(),max,ct));
        }
        catch(InvalidOperationException e){return StatusCode(503,new{mensaje=e.Message});}
    }
}

public record GmailSendRequest(string To, string? Subject, string Body, List<GmailAttachment>? Attachments = null);
public record GmailReplyRequest(string ThreadId, string Body);

[ApiController, Route("api/marketing/google/gmail")]
public class MarketingGmailCallbackController(GmailOAuthService gmail) : ControllerBase
{
    [HttpGet("callback"), AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        const string target = "https://marketing.lunalav.pe/marketing/configuracion";
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return Redirect(target + "?gmail=cancelled");
        try { await gmail.CompleteAsync(code, state, ct); return Redirect(target + "?gmail=connected"); }
        catch { return Redirect(target + "?gmail=error"); }
    }
}
