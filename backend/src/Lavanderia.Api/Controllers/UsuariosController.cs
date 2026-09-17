using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Lavanderia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class UsuariosController : ControllerBase
{
    private static readonly Regex UsuarioValido = new("^[a-z0-9._-]{3,50}$", RegexOptions.IgnoreCase);
    private static readonly Regex PasswordSegura = new("^(?=.*[A-Za-z])(?=.*\\d).{8,}$", RegexOptions.Compiled);
    private static readonly Regex EmailBasicoValido = new("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.IgnoreCase);

    private readonly IUsuarioRepository _usuarios;
    private readonly IRolRepository _roles;
    private readonly ISedeRepository _sedes;

    public UsuariosController(IUsuarioRepository usuarios, IRolRepository roles, ISedeRepository sedes)
    {
        _usuarios = usuarios;
        _roles = roles;
        _sedes = sedes;
    }

    // El nuevo usuario siempre pertenece al mismo negocio que el admin que lo crea.
    private int NegocioIdActual => int.Parse(User.FindFirstValue("negocioId") ?? "0");
    private int UsuarioIdActual => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<UsuarioAdminDto>>> Listar(CancellationToken ct)
        => Ok((await _usuarios.ListarTodosAsync(NegocioIdActual, ct)).Select(Map).ToList());

    [HttpGet("roles")]
    public async Task<ActionResult<List<RolDto>>> Roles(CancellationToken ct)
        => Ok((await _roles.ListarPorNegocioAsync(NegocioIdActual, ct))
            .Select(r => new RolDto(r.Id, r.Codigo, r.Nombre)).ToList());

    [HttpPost]
    public async Task<ActionResult<UsuarioAdminDto>> Crear([FromBody] UsuarioAdminDto dto, CancellationToken ct)
    {
        if (!PasswordValida(dto.Password))
            return BadRequest(new { mensaje = "La contraseña debe tener al menos 8 caracteres e incluir letras y numeros." });

        var usuario = dto.Usuario?.Trim() ?? string.Empty;
        if (!UsuarioValido.IsMatch(usuario))
            return BadRequest(new { mensaje = "El usuario solo puede usar letras, numeros, punto, guion y guion bajo (3 a 50 caracteres)." });

        if (string.IsNullOrWhiteSpace(dto.NombreCompleto))
            return BadRequest(new { mensaje = "El nombre completo es obligatorio." });

        if (!EmailValido(dto.Email))
            return BadRequest(new { mensaje = "El email ingresado no tiene un formato valido." });

        if (!await RolAdministrableAsync(dto.RolId, ct))
            return BadRequest(new { mensaje = "El rol seleccionado no es válido para un usuario del negocio." });

        var existente = await _usuarios.BuscarPorUsuarioAsync(usuario, NegocioIdActual, ct);
        if (existente is not null)
            return Conflict(new { mensaje = "Ya existe un usuario con ese nombre de acceso." });

        if (!await RolSedeValidaAsync(dto.RolId, dto.SedeId, ct))
            return BadRequest(new { mensaje = "Los usuarios no administradores deben estar asignados a una sede." });

        if (!await SedeValidaAsync(dto.SedeId, ct))
            return BadRequest(new { mensaje = "La sede seleccionada no existe o no esta activa." });

        var id = await _usuarios.CrearAsync(new Usuario
        {
            UsuarioLogin = usuario,
            NombreCompleto = dto.NombreCompleto?.Trim() ?? string.Empty,
            Email = NormalizarOpcional(dto.Email),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            RolId = dto.RolId,
            Activo = dto.Activo,
            NegocioId = NegocioIdActual,
            SedeId = dto.SedeId
        }, ct);

        var creado = await _usuarios.ObtenerPorIdAsync(id, NegocioIdActual, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] UsuarioAdminDto dto, CancellationToken ct)
    {
        var existente = await _usuarios.ObtenerPorIdAsync(id, NegocioIdActual, ct);
        if (existente is null) return NotFound();

        var usuario = dto.Usuario?.Trim() ?? string.Empty;
        if (!UsuarioValido.IsMatch(usuario))
            return BadRequest(new { mensaje = "El usuario solo puede usar letras, numeros, punto, guion y guion bajo (3 a 50 caracteres)." });

        if (string.IsNullOrWhiteSpace(dto.NombreCompleto))
            return BadRequest(new { mensaje = "El nombre completo es obligatorio." });

        if (!EmailValido(dto.Email))
            return BadRequest(new { mensaje = "El email ingresado no tiene un formato valido." });

        if (!string.IsNullOrWhiteSpace(dto.Password) && !PasswordValida(dto.Password))
            return BadRequest(new { mensaje = "La contraseña debe tener al menos 8 caracteres e incluir letras y numeros." });

        if (!await RolAdministrableAsync(dto.RolId, ct))
            return BadRequest(new { mensaje = "El rol seleccionado no es válido para un usuario del negocio." });

        if (id == UsuarioIdActual && (dto.RolId != existente.RolId || !dto.Activo))
            return BadRequest(new { mensaje = "No puedes cambiar tu propio rol ni desactivar tu usuario." });

        if (!string.Equals(existente.UsuarioLogin, usuario, StringComparison.OrdinalIgnoreCase))
        {
            var tomado = await _usuarios.BuscarPorUsuarioAsync(usuario, NegocioIdActual, ct);
            if (tomado is not null && tomado.Id != id)
                return Conflict(new { mensaje = "Ya existe un usuario con ese nombre de acceso." });
        }

        if (!await RolSedeValidaAsync(dto.RolId, dto.SedeId, ct))
            return BadRequest(new { mensaje = "Los usuarios no administradores deben estar asignados a una sede." });

        if (!await SedeValidaAsync(dto.SedeId, ct))
            return BadRequest(new { mensaje = "La sede seleccionada no existe o no esta activa." });

        existente.UsuarioLogin = usuario;
        existente.NombreCompleto = dto.NombreCompleto?.Trim() ?? string.Empty;
        existente.Email = NormalizarOpcional(dto.Email);
        existente.RolId = dto.RolId;
        existente.Activo = dto.Activo;
        existente.SedeId = dto.SedeId;
        await _usuarios.ActualizarAsync(existente, NegocioIdActual, ct);

        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            await _usuarios.ActualizarPasswordAsync(id, BCrypt.Net.BCrypt.HashPassword(dto.Password), NegocioIdActual, ct);
        }

        return NoContent();
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        if (!req.Activo && id == UsuarioIdActual)
            return BadRequest(new { mensaje = "No puedes desactivar tu propio usuario." });

        var existente = await _usuarios.ObtenerPorIdAsync(id, NegocioIdActual, ct);
        if (existente is null) return NotFound();
        await _usuarios.CambiarEstadoAsync(id, req.Activo, NegocioIdActual, ct);
        return NoContent();
    }

    private static UsuarioAdminDto Map(Usuario u) => new()
    {
        Id = u.Id,
        Usuario = u.UsuarioLogin,
        NombreCompleto = u.NombreCompleto,
        Email = u.Email,
        RolId = u.RolId,
        SedeId = u.SedeId,
        SedeNombre = u.SedeNombre,
        RolCodigo = u.RolCodigo,
        Activo = u.Activo
    };

    private async Task<bool> SedeValidaAsync(int? sedeId, CancellationToken ct)
    {
        if (sedeId is null) return true;
        var sede = await _sedes.ObtenerPorIdAsync(sedeId.Value, ct);
        return sede is not null && sede.NegocioId == NegocioIdActual && sede.Activo;
    }

    private async Task<bool> RolSedeValidaAsync(int rolId, int? sedeId, CancellationToken ct)
    {
        if (sedeId is not null) return true;
        var admin = await _roles.BuscarPorCodigoAsync("ADMIN", ct);
        return admin is not null && admin.Id == rolId;
    }

    private async Task<bool> RolAdministrableAsync(int rolId, CancellationToken ct)
    {
        // Asignable = ADMIN (sistema) o un rol propio de este negocio.
        var roles = await _roles.ListarPorNegocioAsync(NegocioIdActual, ct);
        return roles.Any(r => r.Id == rolId);
    }

    private static bool PasswordValida(string? password)
        => !string.IsNullOrWhiteSpace(password) && PasswordSegura.IsMatch(password);

    private static string? NormalizarOpcional(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrWhiteSpace(limpio) ? null : limpio;
    }

    private static bool EmailValido(string? valor)
        => string.IsNullOrWhiteSpace(valor) || EmailBasicoValido.IsMatch(valor.Trim());
}
