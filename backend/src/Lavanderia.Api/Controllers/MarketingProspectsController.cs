using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController, Authorize(Policy = "Marketing"), Route("api/marketing")]
public class MarketingProspectsController : ControllerBase
{
    private readonly ISqlConnectionFactory _db;
    public MarketingProspectsController(ISqlConnectionFactory db) => _db = db;
    private int UserId => int.Parse(User.FindFirst("marketingUserId")!.Value);

    [HttpGet("tags")]
    public async Task<ActionResult<List<MarketingTagDto>>> Tags(CancellationToken ct)
    {
        await using var conn = _db.Create(); await conn.OpenAsync(ct); await using var cmd = conn.CreateCommand(); cmd.CommandText="SELECT Id,Nombre,Color FROM marketing.Tag WHERE Activo=1 ORDER BY Nombre";
        return Ok(await cmd.ReadListAsync(r => new MarketingTagDto(r.GetInt32(0),r.GetString(1),r.GetString(2)),ct));
    }
    [HttpPost("tags")]
    public async Task<ActionResult<MarketingTagDto>> CrearTag(MarketingTagRequest r, CancellationToken ct)
    {
        await using var c=_db.Create(); await c.OpenAsync(ct); await using var q=c.CreateCommand(); q.CommandText="INSERT marketing.Tag(Nombre,Color) OUTPUT INSERTED.Id VALUES(@n,@c)";q.AddParam("@n",r.Nombre.Trim());q.AddParam("@c",r.Color.Trim()); var id=Convert.ToInt32(await q.ExecuteScalarAsync(ct));return Created($"/api/marketing/tags/{id}",new MarketingTagDto(id,r.Nombre.Trim(),r.Color.Trim()));
    }

    [HttpGet("prospects")]
    public async Task<ActionResult<object>> Listar([FromQuery] string? q, [FromQuery] string? estado, [FromQuery] string? distrito, [FromQuery] string? prioridad, [FromQuery] string? fuente, [FromQuery] int? minScore, [FromQuery] int page=1, [FromQuery] int pageSize=25, CancellationToken ct=default)
    {
        page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,100); await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText=@";WITH P AS(SELECT p.*, ROW_NUMBER() OVER(ORDER BY p.Score DESC,p.FechaCreacion DESC) rn,COUNT(*) OVER() Total FROM marketing.Prospect p WHERE p.Activo=1 AND (@q IS NULL OR p.NombreComercial LIKE N'%'+@q+N'%' OR p.Telefono LIKE N'%'+@q+N'%' OR p.Whatsapp LIKE N'%'+@q+N'%' OR p.Email LIKE N'%'+@q+N'%' OR p.Distrito LIKE N'%'+@q+N'%') AND (@estado IS NULL OR p.Estado=@estado) AND (@distrito IS NULL OR p.Distrito=@distrito) AND (@prioridad IS NULL OR p.Prioridad=@prioridad) AND (@fuente IS NULL OR p.Fuente=@fuente) AND (@minScore IS NULL OR p.Score>=@minScore)) SELECT p.Id,p.NombreComercial,p.Distrito,p.Telefono,p.Whatsapp,p.Email,p.Estado,p.Prioridad,p.Score,p.Fuente,p.FechaUltimoContacto,(SELECT MIN(f.FechaProgramada) FROM marketing.FollowUp f WHERE f.ProspectId=p.Id AND f.Completado=0) AS ProximoSeguimiento,p.Latitud,p.Longitud,p.Direccion,p.NumeroSedesEstimado,p.TieneDelivery,p.Rating,p.Total FROM P p WHERE p.rn BETWEEN @from AND @to";
        cmd.AddParam("@q",string.IsNullOrWhiteSpace(q)?null:q.Trim());cmd.AddParam("@estado",string.IsNullOrWhiteSpace(estado)?null:estado.Trim().ToUpperInvariant());cmd.AddParam("@distrito",string.IsNullOrWhiteSpace(distrito)?null:distrito.Trim());cmd.AddParam("@prioridad",string.IsNullOrWhiteSpace(prioridad)?null:prioridad.Trim().ToUpperInvariant());cmd.AddParam("@fuente",string.IsNullOrWhiteSpace(fuente)?null:fuente.Trim().ToUpperInvariant());cmd.AddParam("@minScore",minScore);cmd.AddParam("@from",(page-1)*pageSize+1);cmd.AddParam("@to",page*pageSize);
        var items=new List<MarketingProspectListItemDto>();var total=0;await using var rd=await cmd.ExecuteReaderAsync(ct);while(await rd.ReadAsync(ct)){total=rd.GetInt32(18);items.Add(new(rd.GetInt64(0),rd.GetString(1),rd.GetNullableString("Distrito"),rd.GetNullableString("Telefono"),rd.GetNullableString("Whatsapp"),rd.GetNullableString("Email"),rd.GetString(6),rd.GetString(7),rd.GetInt32(8),rd.GetString(9),rd.GetNullableDateTime("FechaUltimoContacto"),rd.GetNullableDateTime("ProximoSeguimiento"),[],rd.GetNullableDecimal("Latitud"),rd.GetNullableDecimal("Longitud"),rd.GetNullableString("Direccion"),rd.GetNullableInt("NumeroSedesEstimado"),rd.GetBoolean(16),rd.GetNullableDecimal("Rating")));}
        if(items.Count>0){var tags=await TagsPorProspectoAsync(items.Select(x=>x.Id),ct);items=items.Select(x=>x with{Tags=tags.GetValueOrDefault(x.Id,[])}).ToList();}return Ok(new{items,total,page,pageSize});
    }

    [HttpPost("prospects")]
    public async Task<ActionResult<MarketingProspectListItemDto>> Crear(MarketingProspectRequest r,CancellationToken ct)
    {
        var estado=NormalizarEstado(r.Estado);var score=CalcularScore(r);await using var c=_db.Create();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);try{await using var cmd=c.CreateCommand();cmd.Transaction=(Microsoft.Data.SqlClient.SqlTransaction)tx;cmd.CommandText=@"INSERT marketing.Prospect(NombreComercial,RazonSocial,Ruc,Telefono,Whatsapp,Email,SitioWeb,Instagram,Direccion,Distrito,TipoNegocio,NumeroSedesEstimado,TieneDelivery,Rating,NumeroResenas,Latitud,Longitud,Estado,Prioridad,Score,Fuente,FormaTrabajoActual,SoftwareActual,Observaciones,ResponsableId) OUTPUT INSERTED.Id VALUES(@n,@rs,@ruc,@tel,@wa,@email,@web,@ig,@dir,@dis,@tipo,@sedes,@delivery,@rating,@resenas,@lat,@lng,@estado,@prio,@score,@fuente,@forma,@software,@obs,@usuario)";
            cmd.AddParam("@n",r.NombreComercial.Trim());cmd.AddParam("@rs",r.RazonSocial);cmd.AddParam("@ruc",r.Ruc);cmd.AddParam("@tel",r.Telefono);cmd.AddParam("@wa",r.Whatsapp);cmd.AddParam("@email",r.Email);cmd.AddParam("@web",r.SitioWeb);cmd.AddParam("@ig",r.Instagram);cmd.AddParam("@dir",r.Direccion);cmd.AddParam("@dis",r.Distrito);cmd.AddParam("@tipo",r.TipoNegocio);cmd.AddParam("@sedes",r.NumeroSedesEstimado);cmd.AddParam("@delivery",r.TieneDelivery);cmd.AddParam("@rating",r.Rating);cmd.AddParam("@resenas",r.NumeroResenas);cmd.AddParam("@lat",r.Latitud);cmd.AddParam("@lng",r.Longitud);cmd.AddParam("@estado",estado);cmd.AddParam("@prio",r.Prioridad.Trim().ToUpperInvariant());cmd.AddParam("@score",score);cmd.AddParam("@fuente",r.Fuente.Trim().ToUpperInvariant());cmd.AddParam("@forma",r.FormaTrabajoActual.Trim().ToUpperInvariant());cmd.AddParam("@software",r.SoftwareActual);cmd.AddParam("@obs",r.Observaciones);cmd.AddParam("@usuario",UserId);var id=Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));await GuardarTagsAsync(c,(Microsoft.Data.SqlClient.SqlTransaction)tx,id,r.TagIds,ct);await AuditarAsync(c,(Microsoft.Data.SqlClient.SqlTransaction)tx,"CREAR","PROSPECT",id.ToString(),r.NombreComercial,ct);await tx.CommitAsync(ct);return Created($"/api/marketing/prospects/{id}",new MarketingProspectListItemDto(id,r.NombreComercial.Trim(),r.Distrito,r.Telefono,r.Whatsapp,r.Email,estado,r.Prioridad.Trim().ToUpperInvariant(),score,r.Fuente.Trim().ToUpperInvariant(),null,null,await ObtenerTagsAsync(id,ct)));}catch{await tx.RollbackAsync(ct);throw;}
    }

    [HttpGet("prospects/{id:long}")]
    public async Task<ActionResult<MarketingProspectDetailDto>> Obtener(long id, CancellationToken ct)
    {
        await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText=@"SELECT Id,NombreComercial,Distrito,Telefono,Whatsapp,Email,Estado,Prioridad,Score,Fuente,FechaUltimoContacto,
          (SELECT MIN(FechaProgramada) FROM marketing.FollowUp WHERE ProspectId=p.Id AND Completado=0) Proximo,
          RazonSocial,Ruc,SitioWeb,Instagram,Facebook,TikTok,Direccion,Pais,NumeroSedesEstimado,TieneDelivery,Rating,NumeroResenas,TipoNegocio,FormaTrabajoActual,SoftwareActual,Observaciones
          FROM marketing.Prospect p WHERE Id=@id AND Activo=1";cmd.AddParam("@id",id);
        await using var rd=await cmd.ExecuteReaderAsync(ct);if(!await rd.ReadAsync(ct))return NotFound();
        var summary=new MarketingProspectListItemDto(rd.GetInt64(0),rd.GetString(1),rd.GetNullableString("Distrito"),rd.GetNullableString("Telefono"),rd.GetNullableString("Whatsapp"),rd.GetNullableString("Email"),rd.GetString(6),rd.GetString(7),rd.GetInt32(8),rd.GetString(9),rd.GetNullableDateTime("FechaUltimoContacto"),rd.GetNullableDateTime("Proximo"),await ObtenerTagsAsync(id,ct));
        return Ok(new MarketingProspectDetailDto(summary,rd.GetNullableString("RazonSocial"),rd.GetNullableString("Ruc"),rd.GetNullableString("SitioWeb"),rd.GetNullableString("Instagram"),rd.GetNullableString("Facebook"),rd.GetNullableString("TikTok"),rd.GetNullableString("Direccion"),rd.GetString(19),rd.GetNullableInt("NumeroSedesEstimado"),rd.GetBoolean(21),rd.GetNullableDecimal("Rating"),rd.GetNullableInt("NumeroResenas"),rd.GetNullableString("TipoNegocio"),rd.GetString(25),rd.GetNullableString("SoftwareActual"),rd.GetNullableString("Observaciones"),await ActividadesAsync(id,ct),await SeguimientosAsync(id,ct)));
    }

    [HttpPatch("prospects/{id:long}/estado")]
    public async Task<IActionResult> CambiarEstado(long id,[FromBody] MarketingStatusRequest r,CancellationToken ct)
    {
        var estado=NormalizarEstado(r.Estado);await using var c=_db.Create();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await using var cmd=c.CreateCommand();cmd.Transaction=(Microsoft.Data.SqlClient.SqlTransaction)tx;cmd.CommandText="UPDATE marketing.Prospect SET Estado=@e,FechaActualizacion=SYSUTCDATETIME() WHERE Id=@id AND Activo=1";cmd.AddParam("@e",estado);cmd.AddParam("@id",id);if(await cmd.ExecuteNonQueryAsync(ct)==0)return NotFound();await using var a=c.CreateCommand();a.Transaction=(Microsoft.Data.SqlClient.SqlTransaction)tx;a.CommandText="INSERT marketing.Activity(ProspectId,UserId,Tipo,Titulo,Detalle) VALUES(@p,@u,N'CAMBIO_ESTADO',N'Estado actualizado',@d)";a.AddParam("@p",id);a.AddParam("@u",UserId);a.AddParam("@d",estado);await a.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);return NoContent();
    }

    [HttpPost("prospects/{id:long}/activities")]
    public async Task<IActionResult> Actividad(long id,MarketingActivityRequest r,CancellationToken ct){await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText=@"INSERT marketing.Activity(ProspectId,UserId,Tipo,Canal,Titulo,Detalle) VALUES(@p,@u,@t,@c,@ti,@d);UPDATE marketing.Prospect SET FechaUltimoContacto=SYSUTCDATETIME(),FechaActualizacion=SYSUTCDATETIME() WHERE Id=@p AND Activo=1";cmd.AddParam("@p",id);cmd.AddParam("@u",UserId);cmd.AddParam("@t",r.Tipo.Trim().ToUpperInvariant());cmd.AddParam("@c",r.Canal);cmd.AddParam("@ti",r.Titulo.Trim());cmd.AddParam("@d",r.Detalle);await cmd.ExecuteNonQueryAsync(ct);return NoContent();}

    [HttpGet("followups")]
    public Task<ActionResult<List<MarketingFollowUpDto>>> Followups(CancellationToken ct)=>ListarSeguimientos(ct);
    [HttpPost("followups")]
    public async Task<ActionResult> CrearFollowup(MarketingFollowUpRequest r,CancellationToken ct){await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="INSERT marketing.FollowUp(ProspectId,ResponsableId,Tipo,Prioridad,Descripcion,FechaProgramada) VALUES(@p,@u,@t,@prio,@d,@f)";cmd.AddParam("@p",r.ProspectId);cmd.AddParam("@u",UserId);cmd.AddParam("@t",r.Tipo.Trim().ToUpperInvariant());cmd.AddParam("@prio",r.Prioridad.Trim().ToUpperInvariant());cmd.AddParam("@d",r.Descripcion.Trim());cmd.AddParam("@f",r.FechaProgramada);await cmd.ExecuteNonQueryAsync(ct);return NoContent();}
    [HttpPatch("followups/{id:long}/complete")]
    public async Task<IActionResult> CompletarFollowup(long id,CancellationToken ct){await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="UPDATE marketing.FollowUp SET Completado=1,FechaCompletado=SYSUTCDATETIME() WHERE Id=@id";cmd.AddParam("@id",id);return await cmd.ExecuteNonQueryAsync(ct)==0?NotFound():NoContent();}

    [HttpGet("tasks")]
    public async Task<ActionResult<List<MarketingTaskDto>>> Tasks(CancellationToken ct)
    {
        await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT t.Id,t.ProspectId,p.NombreComercial,t.Titulo,t.Prioridad,t.FechaVencimiento,t.Estado,t.Notas FROM marketing.Task t LEFT JOIN marketing.Prospect p ON p.Id=t.ProspectId ORDER BY CASE WHEN t.Estado=N'PENDIENTE' THEN 0 ELSE 1 END,t.FechaVencimiento";
        return Ok(await cmd.ReadListAsync(r=>new MarketingTaskDto(r.GetInt64(0),r.IsDBNull(1)?null:r.GetInt64(1),r.GetNullableString("NombreComercial"),r.GetString(3),r.GetString(4),r.GetNullableDateTime("FechaVencimiento"),r.GetString(6),r.GetNullableString("Notas")),ct));
    }
    [HttpPost("tasks")]
    public async Task<ActionResult> CrearTask(MarketingTaskRequest r,CancellationToken ct)
    {
        await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText="INSERT marketing.Task(ProspectId,ResponsableId,Titulo,Prioridad,FechaVencimiento,Notas) VALUES(@p,@u,@t,@prio,@f,@n)";cmd.AddParam("@p",r.ProspectId);cmd.AddParam("@u",UserId);cmd.AddParam("@t",r.Titulo.Trim());cmd.AddParam("@prio",r.Prioridad.Trim().ToUpperInvariant());cmd.AddParam("@f",r.FechaVencimiento);cmd.AddParam("@n",r.Notas);await cmd.ExecuteNonQueryAsync(ct);return NoContent();
    }
    [HttpPatch("tasks/{id:long}/complete")]
    public async Task<IActionResult> CompletarTask(long id,CancellationToken ct){await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="UPDATE marketing.Task SET Estado=N'COMPLETADA',FechaCompletado=SYSUTCDATETIME() WHERE Id=@id AND Estado=N'PENDIENTE'";cmd.AddParam("@id",id);return await cmd.ExecuteNonQueryAsync(ct)==0?NotFound():NoContent();}

    private async Task<ActionResult<List<MarketingFollowUpDto>>> ListarSeguimientos(CancellationToken ct){await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT f.Id,f.ProspectId,p.NombreComercial,f.Tipo,f.Prioridad,f.Descripcion,f.FechaProgramada,f.Completado FROM marketing.FollowUp f JOIN marketing.Prospect p ON p.Id=f.ProspectId ORDER BY f.Completado,f.FechaProgramada";return Ok(await cmd.ReadListAsync(r=>new MarketingFollowUpDto(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetDateTime(6),r.GetBoolean(7)),ct));}
    private async Task<IReadOnlyList<MarketingActivityDto>> ActividadesAsync(long id,CancellationToken ct){await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT a.Id,a.Tipo,a.Canal,a.Titulo,a.Detalle,a.Fecha,u.NombreCompleto FROM marketing.Activity a LEFT JOIN marketing.[User] u ON u.Id=a.UserId WHERE a.ProspectId=@id ORDER BY a.Fecha DESC";cmd.AddParam("@id",id);return await cmd.ReadListAsync(r=>new MarketingActivityDto(r.GetInt64(0),r.GetString(1),r.GetNullableString("Canal"),r.GetString(3),r.GetNullableString("Detalle"),r.GetDateTime(5),r.GetNullableString("NombreCompleto")),ct);}
    private async Task<IReadOnlyList<MarketingFollowUpDto>> SeguimientosAsync(long id,CancellationToken ct){await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT f.Id,f.ProspectId,p.NombreComercial,f.Tipo,f.Prioridad,f.Descripcion,f.FechaProgramada,f.Completado FROM marketing.FollowUp f JOIN marketing.Prospect p ON p.Id=f.ProspectId WHERE f.ProspectId=@id ORDER BY f.FechaProgramada DESC";cmd.AddParam("@id",id);return await cmd.ReadListAsync(r=>new MarketingFollowUpDto(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetDateTime(6),r.GetBoolean(7)),ct);}
    private static string NormalizarEstado(string estado){var e=estado.Trim().ToUpperInvariant();var validos=new[]{"NUEVO","INVESTIGAR","LISTO_CONTACTAR","CONTACTADO","RESPONDIO","INTERESADO","DEMO_PROGRAMADA","DEMO_REALIZADA","NEGOCIACION","GANADO","PERDIDO","NO_INTERESADO","NO_RESPONDE","RECONTACTAR","DUPLICADO"};if(!validos.Contains(e))throw new ArgumentException("Estado comercial inválido.");return e;}
    private static int CalcularScore(MarketingProspectRequest r){var n=0;if(!string.IsNullOrWhiteSpace(r.Whatsapp))n+=10;if(!string.IsNullOrWhiteSpace(r.Instagram))n+=8;if(!string.IsNullOrWhiteSpace(r.SitioWeb))n+=5;if(r.NumeroSedesEstimado>1)n+=20;if(r.TieneDelivery)n+=10;if(r.NumeroResenas>=100)n+=10;if(r.Rating>4)n+=5;if(r.Estado is "RESPONDIO" or "INTERESADO")n+=10;if(r.Estado.StartsWith("DEMO",StringComparison.OrdinalIgnoreCase))n+=20;return Math.Min(100,n);}
    private async Task<Dictionary<long,IReadOnlyList<MarketingTagDto>>> TagsPorProspectoAsync(IEnumerable<long> ids,CancellationToken ct){var list=ids.Distinct().ToList();if(list.Count==0)return[];await using var c=_db.Create();await c.OpenAsync(ct);await using var cmd=c.CreateCommand();var ns=list.Select((_,i)=>"@i"+i).ToArray();cmd.CommandText=$"SELECT pt.ProspectId,t.Id,t.Nombre,t.Color FROM marketing.ProspectTag pt JOIN marketing.Tag t ON t.Id=pt.TagId WHERE pt.ProspectId IN ({string.Join(',',ns)})";for(var i=0;i<list.Count;i++)cmd.AddParam(ns[i],list[i]);var result=new Dictionary<long,IReadOnlyList<MarketingTagDto>>();await using var rd=await cmd.ExecuteReaderAsync(ct);while(await rd.ReadAsync(ct)){var id=rd.GetInt64(0);var current=result.TryGetValue(id,out var x)?x.ToList():[];current.Add(new(rd.GetInt32(1),rd.GetString(2),rd.GetString(3)));result[id]=current;}return result;}
    private async Task<IReadOnlyList<MarketingTagDto>> ObtenerTagsAsync(long id,CancellationToken ct)=>(await TagsPorProspectoAsync([id],ct)).GetValueOrDefault(id,[]);
    private static async Task GuardarTagsAsync(Microsoft.Data.SqlClient.SqlConnection c,Microsoft.Data.SqlClient.SqlTransaction tx,long id,IEnumerable<int> tags,CancellationToken ct){foreach(var tag in tags.Distinct()){await using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT marketing.ProspectTag(ProspectId,TagId) SELECT @p,Id FROM marketing.Tag WHERE Id=@t AND Activo=1";q.AddParam("@p",id);q.AddParam("@t",tag);await q.ExecuteNonQueryAsync(ct);}}
    private async Task AuditarAsync(Microsoft.Data.SqlClient.SqlConnection c,Microsoft.Data.SqlClient.SqlTransaction tx,string accion,string entidad,string id,string detalle,CancellationToken ct){await using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT marketing.Audit(UserId,Accion,Entidad,EntidadId,Detalle,Ip) VALUES(@u,@a,@e,@id,@d,@ip)";q.AddParam("@u",UserId);q.AddParam("@a",accion);q.AddParam("@e",entidad);q.AddParam("@id",id);q.AddParam("@d",detalle);q.AddParam("@ip",HttpContext.Connection.RemoteIpAddress?.ToString());await q.ExecuteNonQueryAsync(ct);}
}
