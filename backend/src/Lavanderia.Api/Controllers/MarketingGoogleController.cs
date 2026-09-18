using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController,Authorize(Policy="Marketing"),Route("api/marketing/google")]
public class MarketingGoogleController(GooglePlacesService places, GmailOAuthService gmail) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)=>Ok(new{placesConfigured=places.Configured,gmail=await gmail.StatusAsync(ct),gmailMessage="Gmail usa consentimiento OAuth; LunaLav no recibe ni almacena tu contraseña."});
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
    [HttpGet("places/search")]
    public async Task<IActionResult> Search([FromQuery] string q,[FromQuery] int max=10,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(q))return BadRequest(new{mensaje="Indica una búsqueda."});
        try{return Ok(await places.SearchTextAsync(q.Trim(),max,ct));}
        catch(InvalidOperationException e){return StatusCode(503,new{mensaje=e.Message});}
    }
}

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
