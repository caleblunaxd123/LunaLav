using System.Text.RegularExpressions;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Alta autónoma de una lavandería. Toda la estructura inicial se crea en una sola transacción:
/// si falla un paso no queda un tenant incompleto ni credenciales huérfanas.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/registro")]
public class RegistroPublicoController : ControllerBase
{
    private const int DiasPrueba = 14;
    private static readonly Regex SlugValido = new("^[a-z0-9][a-z0-9-]{1,49}$", RegexOptions.IgnoreCase);
    private static readonly Regex UsuarioValido = new("^[a-z0-9._-]{3,50}$", RegexOptions.IgnoreCase);
    // Incluye las rutas de primer nivel de la web (app.lunalav.pe/<ruta>): un tenant con ese
    // código chocaría con una página de LunaLav. Debe coincidir con RESERVED_SLUGS de la app móvil.
    private static readonly HashSet<string> SlugsReservados = new(StringComparer.OrdinalIgnoreCase)
    {
        "login", "demo", "api", "admin", "app", "marketing", "soporte", "inicio", "pedidos",
        "clientes", "caja", "inventario", "ajustes", "facturacion", "plataforma", "privacidad", "terminos",
        "ticket", "cuadre-caja", "seleccionar-sede", "registrar", "registro-antiguo", "promociones",
        "reportes", "assets", "seguimiento", "repartidor", "recibo-suscripcion", "nosotros"
    };
    private static readonly Dictionary<string, decimal> Precios = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BASICO"] = 20m,
        ["FACTURA"] = 50m,
        ["MULTISEDE"] = 80m
    };

    private readonly ISqlConnectionFactory _db;
    public RegistroPublicoController(ISqlConnectionFactory db) => _db = db;

    /// <summary>
    /// Consulta en vivo del código de empresa mientras se escribe (paso 1 del alta), para no
    /// enterarse recién al final de que está ocupado. Solo responde sí/no: no expone datos.
    /// </summary>
    [HttpGet("disponible")]
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> Disponible([FromQuery] string? slug, CancellationToken ct)
    {
        var s = (slug ?? "").Trim().ToLowerInvariant();
        if (!SlugValido.IsMatch(s))
            return Ok(new { disponible = false, mensaje = "Solo letras minúsculas, números y guiones; debe empezar con letra o número." });
        if (SlugsReservados.Contains(s))
            return Ok(new { disponible = false, mensaje = "Ese código está reservado por LunaLav. Prueba con otro." });

        await using var conn = _db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Negocio WHERE Slug = @Slug) THEN 1 ELSE 0 END";
        cmd.Parameters.AddWithValue("@Slug", s);
        var ocupado = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
        return Ok(new { disponible = !ocupado, mensaje = ocupado ? "Ese código de empresa ya está en uso. Prueba con otro." : null });
    }

    [HttpPost("prueba")]
    [EnableRateLimiting("signup")]
    public async Task<ActionResult<RegistrarPruebaResponse>> Registrar(
        [FromBody] RegistrarPruebaRequest req, CancellationToken ct)
    {
        if (!req.AceptaTerminos)
            return BadRequest(new { mensaje = "Debes aceptar los términos y la política de privacidad." });

        var slug = req.Slug.Trim().ToLowerInvariant();
        if (!SlugValido.IsMatch(slug) || SlugsReservados.Contains(slug))
            return BadRequest(new { mensaje = "El código de empresa solo puede contener letras, números y guiones, y no puede ser una palabra reservada." });

        var usuario = req.Usuario.Trim().ToLowerInvariant();
        if (!UsuarioValido.IsMatch(usuario))
            return BadRequest(new { mensaje = "El usuario solo puede contener letras, números, punto, guion y guion bajo." });

        var plan = req.Plan.Trim().ToUpperInvariant();
        if (!Precios.TryGetValue(plan, out var montoMensual))
            return BadRequest(new { mensaje = "El plan seleccionado no es válido." });

        var finPrueba = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(DiasPrueba));
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12);

        await using var conn = _db.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.Negocio WITH (UPDLOCK, HOLDLOCK) WHERE Slug = @Slug)
    THROW 51001, 'SLUG_DUPLICADO', 1;

DECLARE @AdminRolId INT = (SELECT TOP 1 Id FROM dbo.Rol WHERE Codigo = 'ADMIN' AND NegocioId IS NULL);
IF @AdminRolId IS NULL THROW 51002, 'ROL_ADMIN_NO_CONFIGURADO', 1;

INSERT INTO dbo.Negocio
    (Nombre, Slug, TitularNombre, TitularEmail, TitularCelular, Activo,
     PlanSuscripcion, EstadoSuscripcion, MontoMensual, ProximoPago, NotasInternas)
VALUES
    (@NombreNegocio, @Slug, @Responsable, @Email, @Celular, 1,
     @Plan, 'PRUEBA', @MontoMensual, @FinPrueba, N'Alta autónoma desde LunaLav Mobile');
DECLARE @NegocioId INT = CONVERT(INT, SCOPE_IDENTITY());

INSERT INTO dbo.Sede (NegocioId, Nombre, Telefono, Activo)
VALUES (@NegocioId, @SedeNombre, @Celular, 1);
DECLARE @SedeId INT = CONVERT(INT, SCOPE_IDENTITY());

INSERT INTO dbo.AreaLavado (SedeId, Nombre, Orden, TiempoEstMinutos, Activa) VALUES
    (@SedeId, N'Recepción', 1, 15, 1), (@SedeId, N'Lavado', 2, 60, 1),
    (@SedeId, N'Secado', 3, 45, 1), (@SedeId, N'Doblado', 4, 20, 1),
    (@SedeId, N'Control de calidad', 5, 10, 1), (@SedeId, N'Empacado', 6, 5, 1);

INSERT INTO dbo.Usuario
    (Usuario, NombreCompleto, Email, PasswordHash, RolId, Activo, NegocioId, SedeId)
VALUES (@Usuario, @Responsable, @Email, @PasswordHash, @AdminRolId, 1, @NegocioId, @SedeId);

INSERT INTO dbo.Rol (Codigo, Nombre, NegocioId, EsSistema)
VALUES ('COORDINADOR', N'Coordinador', @NegocioId, 0);
DECLARE @CoordinadorId INT = CONVERT(INT, SCOPE_IDENTITY());
INSERT INTO dbo.Rol (Codigo, Nombre, NegocioId, EsSistema)
VALUES ('TRABAJADOR', N'Trabajador', @NegocioId, 0);
DECLARE @TrabajadorId INT = CONVERT(INT, SCOPE_IDENTITY());

INSERT INTO dbo.RolPermiso (NegocioId, RolId, Modulo, PuedeAcceder)
SELECT @NegocioId, @CoordinadorId, v.Modulo, v.Puede
FROM (VALUES
    ('INICIO',1),('PEDIDOS',1),('REGISTRAR',1),('CAJA',1),('CLIENTES',1),
    ('PROMOCIONES',0),('REPORTES',1),('INVENTARIO',1),('AJUSTES',0)
) v(Modulo, Puede);

INSERT INTO dbo.RolPermiso (NegocioId, RolId, Modulo, PuedeAcceder)
SELECT @NegocioId, @TrabajadorId, v.Modulo, v.Puede
FROM (VALUES
    ('INICIO',1),('PEDIDOS',1),('REGISTRAR',1),('CAJA',0),('CLIENTES',0),
    ('PROMOCIONES',0),('REPORTES',0),('INVENTARIO',0),('AJUSTES',0)
) v(Modulo, Puede);

INSERT INTO dbo.ConfiguracionNegocio
    (NegocioId, NombreNegocio, ColorPrimario, ColorSecundario, ColorAcento,
     Telefono, Igv, MetaMensual, SolesPorPunto, MaxDescuentoPct)
VALUES
    (@NegocioId, @NombreNegocio, '#0b57d0', '#29b6f6', '#f5a623',
     @Celular, 18, 0, 1, 30);

INSERT INTO dbo.Servicio (NegocioId, Nombre, Precio, Costo, Unidad, Activo, EsCargoDelivery)
VALUES (@NegocioId, N'Servicio a Domicilio', 0, 0, N'Unidad', 1, 1);

SELECT @NegocioId;";
            cmd.Parameters.AddWithValue("@NombreNegocio", req.NombreNegocio.Trim());
            cmd.Parameters.AddWithValue("@Slug", slug);
            cmd.Parameters.AddWithValue("@Responsable", req.NombreResponsable.Trim());
            cmd.Parameters.AddWithValue("@Email", req.Email.Trim().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@Celular", req.Celular.Trim());
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
            cmd.Parameters.AddWithValue("@Plan", plan);
            cmd.Parameters.AddWithValue("@MontoMensual", montoMensual);
            cmd.Parameters.AddWithValue("@FinPrueba", finPrueba.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@SedeNombre", string.IsNullOrWhiteSpace(req.SedeNombre) ? "Principal" : req.SedeNombre.Trim());

            var negocioId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            await tx.CommitAsync(ct);
            return Created($"/api/negocios/{negocioId}", new RegistrarPruebaResponse(negocioId, slug, finPrueba, DiasPrueba));
        }
        catch (SqlException ex) when (ex.Number is 51001 or 2601 or 2627)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { mensaje = "Ese código de empresa ya está en uso. Prueba con otro." });
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
