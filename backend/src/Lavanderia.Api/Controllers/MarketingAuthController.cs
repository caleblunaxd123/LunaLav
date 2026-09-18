using Lavanderia.Api.Auth;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.ComponentModel.DataAnnotations;

namespace Lavanderia.Api.Controllers;

[ApiController]
[Route("api/marketing/auth")]
public class MarketingAuthController : ControllerBase
{
    private readonly ISqlConnectionFactory _db; private readonly IMarketingTokenService _tokens; private readonly IConfiguration _cfg;
    public MarketingAuthController(ISqlConnectionFactory db, IMarketingTokenService tokens, IConfiguration cfg) { _db = db; _tokens = tokens; _cfg = cfg; }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<ActionResult<MarketingLoginResponse>> Login(MarketingLoginRequest request, CancellationToken ct)
    {
        var user = await BuscarUsuarioAsync(request.Usuario.Trim(), ct);
        if (user is null || !user.Activo || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
        return Ok(await CrearSesionAsync(user, ct));
    }

    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<ActionResult<MarketingLoginResponse>> Refresh(MarketingRefreshRequest request, CancellationToken ct)
    {
        var hash = Hash(request.RefreshToken);
        await using var conn = _db.Create(); await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT u.Id,u.UsuarioLogin,u.NombreCompleto,u.Email,u.PasswordHash,u.RolCodigo,u.Activo
            FROM marketing.RefreshToken t JOIN marketing.[User] u ON u.Id=t.UserId
            WHERE t.TokenHash=@hash AND t.Revocado=0 AND t.FechaExpiracion>SYSUTCDATETIME()";
        cmd.AddParam("@hash", hash);
        var user = await cmd.ReadFirstOrDefaultAsync(MapUser, ct);
        if (user is null || !user.Activo) return Unauthorized(new { mensaje = "Sesión expirada. Inicia sesión de nuevo." });
        await using var revoke = conn.CreateCommand(); revoke.CommandText = "UPDATE marketing.RefreshToken SET Revocado=1 WHERE TokenHash=@hash AND Revocado=0"; revoke.AddParam("@hash", hash);
        if (await revoke.ExecuteNonQueryAsync(ct) != 1) return Unauthorized(new { mensaje = "Sesión expirada. Inicia sesión de nuevo." });
        return Ok(await CrearSesionAsync(user, ct));
    }

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout(MarketingRefreshRequest request, CancellationToken ct)
    {
        await using var conn = _db.Create(); await conn.OpenAsync(ct); await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE marketing.RefreshToken SET Revocado=1 WHERE TokenHash=@hash"; cmd.AddParam("@hash", Hash(request.RefreshToken)); await cmd.ExecuteNonQueryAsync(ct); return NoContent();
    }

    // Solo el dueño de LunaLav puede crear la primera cuenta comercial. Después el módulo de
    // equipo (Fase 1.1) delegará altas a ADMINISTRADOR de Marketing.
    [HttpPost("bootstrap"), Authorize(Roles = "PROPIETARIO")]
    public async Task<ActionResult<MarketingUserDto>> Bootstrap([FromBody] MarketingBootstrapRequest request, CancellationToken ct)
    {
        await using var conn = _db.Create(); await conn.OpenAsync(ct); await using var exists = conn.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM marketing.[User]";
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) > 0) return Conflict(new { mensaje = "Marketing ya tiene usuarios. Crea los siguientes desde administración." });
        await using var insert = conn.CreateCommand(); insert.CommandText = @"INSERT marketing.[User](UsuarioLogin,NombreCompleto,Email,PasswordHash,RolCodigo)
            OUTPUT INSERTED.Id VALUES(@login,@nombre,@email,@hash,N'ADMINISTRADOR')";
        insert.AddParam("@login", request.Usuario.Trim()); insert.AddParam("@nombre", request.Nombre.Trim()); insert.AddParam("@email", string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim()); insert.AddParam("@hash", BCrypt.Net.BCrypt.HashPassword(request.Password));
        var id = Convert.ToInt32(await insert.ExecuteScalarAsync(ct));
        return Created("/api/marketing/auth/me", new MarketingUserDto(id, request.Usuario.Trim(), request.Nombre.Trim(), "ADMINISTRADOR", request.Email?.Trim()));
    }

    [HttpGet("me"), Authorize(Policy = "Marketing")]
    public async Task<ActionResult<MarketingUserDto>> Me(CancellationToken ct)
    {
        var id = int.Parse(User.FindFirst("marketingUserId")!.Value); var user = await BuscarUsuarioAsync(id, ct);
        return user is null ? NotFound() : Ok(user.Dto);
    }

    private async Task<MarketingUser?> BuscarUsuarioAsync(string login, CancellationToken ct)
    {
        await using var conn = _db.Create(); await conn.OpenAsync(ct); await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id,UsuarioLogin,NombreCompleto,Email,PasswordHash,RolCodigo,Activo FROM marketing.[User] WHERE UsuarioLogin=@login"; cmd.AddParam("@login", login); return await cmd.ReadFirstOrDefaultAsync(MapUser, ct);
    }
    private async Task<MarketingUser?> BuscarUsuarioAsync(int id, CancellationToken ct)
    {
        await using var conn = _db.Create(); await conn.OpenAsync(ct); await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id,UsuarioLogin,NombreCompleto,Email,PasswordHash,RolCodigo,Activo FROM marketing.[User] WHERE Id=@id"; cmd.AddParam("@id", id); return await cmd.ReadFirstOrDefaultAsync(MapUser, ct);
    }
    private async Task<MarketingLoginResponse> CrearSesionAsync(MarketingUser user, CancellationToken ct)
    {
        var (token, expires) = _tokens.Generar(user.Id, user.Login, user.Nombre, user.Rol); var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        await using var conn = _db.Create(); await conn.OpenAsync(ct); await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT marketing.RefreshToken(UserId,TokenHash,FechaExpiracion) VALUES(@id,@hash,DATEADD(day,@days,SYSUTCDATETIME())); UPDATE marketing.[User] SET UltimoAcceso=SYSUTCDATETIME() WHERE Id=@id";
        cmd.AddParam("@id", user.Id); cmd.AddParam("@hash", Hash(raw)); cmd.AddParam("@days", Math.Clamp(_cfg.GetValue<int?>("Jwt:RefreshTokenDays") ?? 14, 1, 90)); await cmd.ExecuteNonQueryAsync(ct);
        return new(token, expires, raw, user.Dto);
    }
    private static MarketingUser MapUser(SqlDataReader r) => new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetNullableString("Email"), r.GetString(4), r.GetString(5), r.GetBoolean(6));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private sealed record MarketingUser(int Id, string Login, string Nombre, string? Email, string PasswordHash, string Rol, bool Activo) { public MarketingUserDto Dto => new(Id, Login, Nombre, Rol, Email); }
}

public class MarketingBootstrapRequest { [Required, StringLength(80)] public string Usuario { get; set; } = ""; [Required, StringLength(160)] public string Nombre { get; set; } = ""; [EmailAddress, StringLength(180)] public string? Email { get; set; } [Required, StringLength(200), MinLength(12)] public string Password { get; set; } = ""; }
