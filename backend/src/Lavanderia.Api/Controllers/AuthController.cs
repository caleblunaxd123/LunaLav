using Lavanderia.Api.Auth;
using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Lavanderia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IRolPermisoRepository _permisos;
    private readonly ISedeRepository _sedes;
    private readonly INegocioRepository _negocios;
    private readonly ITokenService _tokens;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly int _refreshTokenDias;
    private readonly string _celularPlataforma;

    public AuthController(
        IUsuarioRepository usuarios, IRolPermisoRepository permisos, ISedeRepository sedes,
        INegocioRepository negocios, ITokenService tokens, IRefreshTokenRepository refreshTokens,
        Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOpts, IConfiguration config)
    {
        _usuarios = usuarios;
        _permisos = permisos;
        _sedes = sedes;
        _negocios = negocios;
        _tokens = tokens;
        _refreshTokens = refreshTokens;
        _refreshTokenDias = jwtOpts.Value.RefreshTokenDays;
        _celularPlataforma = config["Plataforma:CelularContacto"] ?? "";
    }

    private async Task<string> EmitirRefreshTokenAsync(int usuarioId, int? sedeId, CancellationToken ct)
    {
        var token = RefreshTokenGenerator.GenerarToken();
        var hash = RefreshTokenGenerator.Hash(token);
        var expira = DateTime.UtcNow.AddDays(_refreshTokenDias);
        await _refreshTokens.CrearAsync(usuarioId, sedeId, hash, expira, ct);
        return token;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        Negocio? negocioDeLaUrl = null;
        Usuario? usuario;

        if (!string.IsNullOrWhiteSpace(req.EmpresaSlug))
        {
            negocioDeLaUrl = await _negocios.ObtenerPorSlugIncluyendoInactivoAsync(req.EmpresaSlug.Trim(), ct);
            usuario = negocioDeLaUrl is null
                ? null
                : await _usuarios.BuscarPorUsuarioAsync(req.Usuario.Trim(), negocioDeLaUrl.Id, ct);
        }
        else
        {
            // Conserva el acceso historico por /login cuando el usuario es unico.
            // Si el nombre existe en varios tenants, la ruta con slug evita ambiguedades.
            usuario = await _usuarios.BuscarPorUsuarioAsync(req.Usuario.Trim(), ct);
        }
        if (usuario is null || !usuario.Activo)
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });

        if (!BCrypt.Net.BCrypt.Verify(req.Password, usuario.PasswordHash))
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });

        var negocioUsuario = await _negocios.ObtenerPorIdAsync(usuario.NegocioId, ct);
        if (usuario.RolCodigo != "PROPIETARIO" && !NegocioAccessRules.PuedeOperar(negocioUsuario))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = NegocioAccessRules.MensajeBloqueo(negocioUsuario, _celularPlataforma)
            });
        }

        // Si la URL trae el slug de una empresa, el usuario autenticado debe pertenecer a ella.
        // Sin esto, cualquier usuario global podria "entrar" visualmente por la URL de otra empresa.
        if (!string.IsNullOrWhiteSpace(req.EmpresaSlug))
        {
            if (negocioDeLaUrl is null || negocioDeLaUrl.Id != usuario.NegocioId)
                return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
        }

        var modulos = await ObtenerModulosAsync(usuario, ct);
        var (token, expira) = _tokens.GenerarAccessToken(usuario, modulos);
        var refreshToken = await EmitirRefreshTokenAsync(usuario.Id, usuario.SedeId, ct);
        // Marca de actividad para el panel del propietario (saber qué empresas usan el sistema).
        await _usuarios.RegistrarUltimoAccesoAsync(usuario.Id, ct);

        return Ok(new LoginResponse(
            token,
            expira,
            refreshToken,
            new UsuarioDto(usuario.Id, usuario.UsuarioLogin, usuario.NombreCompleto, usuario.RolCodigo, modulos,
                usuario.NegocioId, usuario.SedeId, usuario.SedeNombre)
        ));
    }

    /// <summary>
    /// Renueva el access token (de corta duracion) usando el refresh token de larga duracion.
    /// Rota el refresh token en cada uso: el anterior queda revocado y se emite uno nuevo, asi
    /// que si alguien roba un refresh token ya usado, el siguiente intento con el original falla.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")] // anonimo y consulta BD: mismo freno anti-fuerza-bruta que el login
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshTokenRequest req, CancellationToken ct)
    {
        var hash = RefreshTokenGenerator.Hash(req.RefreshToken);
        var guardado = await _refreshTokens.ObtenerPorHashAsync(hash, ct);
        if (guardado is null || guardado.Revocado || guardado.FechaExpiracion <= DateTime.UtcNow)
            return Unauthorized(new { mensaje = "Sesión expirada. Inicia sesión de nuevo." });

        var usuario = await _usuarios.ObtenerPorIdAsync(guardado.UsuarioId, ct);
        if (usuario is null || !usuario.Activo)
            return Unauthorized(new { mensaje = "Sesión expirada. Inicia sesión de nuevo." });

        var negocio = await _negocios.ObtenerPorIdAsync(usuario.NegocioId, ct);
        if (usuario.RolCodigo != "PROPIETARIO" && !NegocioAccessRules.PuedeOperar(negocio))
        {
            await _refreshTokens.RevocarAsync(hash, ct);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = NegocioAccessRules.MensajeBloqueo(negocio, _celularPlataforma)
            });
        }

        var sedeSesionId = guardado.SedeId ?? usuario.SedeId;
        if (sedeSesionId is int sedeId)
        {
            var sede = await _sedes.ObtenerPorIdAsync(sedeId, ct);
            if (sede is null || !sede.Activo || sede.NegocioId != usuario.NegocioId)
            {
                await _refreshTokens.RevocarAsync(hash, ct);
                return Unauthorized(new { mensaje = "La sede asignada ya no está disponible. Contacta al administrador." });
            }
        }

        // Solo una solicitud puede consumir el token. Dos pestañas pueden intentar renovar al
        // mismo tiempo; la actualización condicional del repositorio decide atómicamente cuál
        // gana y la otra no debe recibir una segunda sesión válida.
        if (!await _refreshTokens.RevocarAsync(hash, ct))
            return Unauthorized(new { mensaje = "Sesión expirada. Inicia sesión de nuevo." });

        usuario.SedeId = sedeSesionId;
        if (sedeSesionId is int sedeActivaId)
            usuario.SedeNombre = (await _sedes.ObtenerPorIdAsync(sedeActivaId, ct))?.Nombre;

        var modulos = await ObtenerModulosAsync(usuario, ct);
        var (token, expira) = _tokens.GenerarAccessToken(usuario, modulos);
        var nuevoRefreshToken = await EmitirRefreshTokenAsync(usuario.Id, sedeSesionId, ct);

        return Ok(new LoginResponse(
            token,
            expira,
            nuevoRefreshToken,
            new UsuarioDto(usuario.Id, usuario.UsuarioLogin, usuario.NombreCompleto, usuario.RolCodigo, modulos,
                usuario.NegocioId, usuario.SedeId, usuario.SedeNombre)
        ));
    }

    /// <summary>Revoca el refresh token de esta sesion (logout real: server-side, no solo borrar
    /// el token del navegador). El access token ya emitido sigue siendo valido hasta su
    /// expiracion natural (corta, ver Jwt:AccessTokenMinutes), pero no podra renovarse mas.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest req, CancellationToken ct)
    {
        await _refreshTokens.RevocarAsync(RefreshTokenGenerator.Hash(req.RefreshToken), ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UsuarioDto>> Me(CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var u = await _usuarios.ObtenerPorIdAsync(id, ct);
        if (u is null) return NotFound();
        var modulos = await ObtenerModulosAsync(u, ct);
        return Ok(new UsuarioDto(u.Id, u.UsuarioLogin, u.NombreCompleto, u.RolCodigo, modulos,
            u.NegocioId, u.SedeId, u.SedeNombre));
    }

    /// <summary>
    /// Cambia la sede activa de la sesion actual (no persiste el cambio en la BD, solo
    /// reemite el JWT con el SedeId elegido). Pensado para ADMIN con acceso a varias sedes.
    /// </summary>
    [HttpPost("seleccionar-sede")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> SeleccionarSede([FromBody] SeleccionarSedeRequest req, CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var usuario = await _usuarios.ObtenerPorIdAsync(id, ct);
        if (usuario is null) return NotFound();

        var sede = await _sedes.ObtenerPorIdAsync(req.SedeId, ct);
        if (sede is null || sede.NegocioId != usuario.NegocioId)
            return BadRequest(new { mensaje = "La sede no pertenece a tu negocio." });
        if (!sede.Activo)
            return BadRequest(new { mensaje = "La sede seleccionada está inactiva." });

        if (usuario.SedeId is int sedeAsignada && sedeAsignada != req.SedeId)
            return BadRequest(new { mensaje = "Tu usuario esta asignado a otra sede." });

        usuario.SedeId = sede.Id;
        usuario.SedeNombre = sede.Nombre;
        var modulos = await ObtenerModulosAsync(usuario, ct);
        var (token, expira) = _tokens.GenerarAccessToken(usuario, modulos);
        if (!string.IsNullOrWhiteSpace(req.RefreshToken) &&
            !await _refreshTokens.RevocarAsync(RefreshTokenGenerator.Hash(req.RefreshToken), ct))
            return Unauthorized(new { mensaje = "La sesión ya fue renovada. Vuelve a iniciar sesión." });

        var refreshToken = await EmitirRefreshTokenAsync(usuario.Id, sede.Id, ct);

        return Ok(new LoginResponse(
            token,
            expira,
            refreshToken,
            new UsuarioDto(usuario.Id, usuario.UsuarioLogin, usuario.NombreCompleto, usuario.RolCodigo, modulos,
                usuario.NegocioId, usuario.SedeId, usuario.SedeNombre)
        ));
    }

    private async Task<List<string>> ObtenerModulosAsync(Domain.Usuario usuario, CancellationToken ct)
    {
        if (usuario.RolCodigo == "ADMIN") return Modulos.Todos.ToList();
        return await _permisos.ObtenerModulosPermitidosPorRolAsync(usuario.RolId, usuario.NegocioId, ct);
    }
}
