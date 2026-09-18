using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController,Authorize(Policy="Marketing"),Route("api/marketing/google")]
public class MarketingGoogleController(GooglePlacesService places) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status()=>Ok(new{placesConfigured=places.Configured,gmailConfigured=false,gmailMessage="Gmail requiere cliente OAuth y consentimiento del propietario de la cuenta."});
    [HttpGet("places/search")]
    public async Task<IActionResult> Search([FromQuery] string q,[FromQuery] int max=10,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(q))return BadRequest(new{mensaje="Indica una búsqueda."});
        try{return Ok(await places.SearchTextAsync(q.Trim(),max,ct));}
        catch(InvalidOperationException e){return StatusCode(503,new{mensaje=e.Message});}
    }
}
