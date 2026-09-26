

using System.Text.Json;
using System.Text.RegularExpressions;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services.Pagos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

public sealed record Parametros3DSRequest(string Eci, string Xid, string Cavv, string ProtocolVersion, string DirectoryServerTransactionId);

public sealed record ActivarPagoRequest(
    string TokenId, string Nombre, string Apellido, string Email, string Telefono, string Direccion, string Ciudad,
    bool AceptaCobroRecurrente, Parametros3DSRequest? Parametros3DS);

/// <summary>
/// Pago de la mensualidad con tarjeta (Culqi) desde LunaLav web. Solo el administrador de la
/// empresa, y siempre sobre su propio negocio (NegocioId del JWT). Estas rutas quedan fuera del
/// bloqueo por suscripción vencida (Program.cs) para que una empresa vencida pueda pagar.
/// </summary>
[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/suscripcion/pago")]
public class SuscripcionPagoController(
    SuscripcionCulqiService pagos, INegocioRepository negocios, IPagoSuscripcionRepository historial) : TenantAwareControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Estado(CancellationToken ct)
    {
        var n = await negocios.ObtenerPorIdAsync(NegocioId, ct);
        if (n is null) return NotFound(new { mensaje = "Empresa no encontrada." });
        var auto = await pagos.ObtenerAsync(NegocioId, ct);
        var pagosRecientes = (await historial.ListarPorNegocioAsync(NegocioId, ct)).Take(12)
            .Select(p => new { p.Id, p.Fecha, p.Monto, p.Metodo, p.PeriodoDesde, p.PeriodoHasta });
        return Ok(new
        {
            configurado = pagos.Configurado,
            publicKey = pagos.Configurado ? pagos.PublicKey : null,
            modo = pagos.Modo,
            empresa = n.Nombre,
            plan = n.PlanSuscripcion,
            montoMensual = n.MontoMensual,
            estadoSuscripcion = n.EstadoSuscripcion,
            proximoPago = n.ProximoPago,
            pagoAutomatico = new
            {
                estado = auto.Estado,
                tarjeta = auto.TarjetaUltimos4 is null ? null : $"{auto.TarjetaMarca ?? "Tarjeta"} •••• {auto.TarjetaUltimos4}",
                ultimoError = auto.UltimoError
            },
            titular = new { nombre = n.TitularNombre, email = n.TitularEmail, celular = n.TitularCelular },
            pagos = pagosRecientes
        });
    }

    [HttpPost("activar")]
    public async Task<IActionResult> Activar([FromBody] ActivarPagoRequest req, CancellationToken ct)
    {
        if (!req.AceptaCobroRecurrente)
            return BadRequest(new { mensaje = "Debes aceptar el cobro mensual automático a tu tarjeta." });
        if (string.IsNullOrWhiteSpace(req.TokenId) || !req.TokenId.StartsWith("tkn_", StringComparison.Ordinal))
            return BadRequest(new { mensaje = "No recibimos los datos de la tarjeta. Vuelve a ingresarla." });
        var faltante = Validar(req);
        if (faltante is not null) return BadRequest(new { mensaje = faltante });

        var tds = req.Parametros3DS is { } p
            ? new Parametros3DS(p.Eci, p.Xid, p.Cavv, p.ProtocolVersion, p.DirectoryServerTransactionId) : null;
        try
        {
            var r = await pagos.ActivarAsync(NegocioId, req.TokenId.Trim(),
                new DatosTitular(req.Nombre.Trim(), req.Apellido.Trim(), req.Email.Trim().ToLowerInvariant(),
                    Regex.Replace(req.Telefono, @"[^\d+]", ""), req.Direccion.Trim(), req.Ciudad.Trim()), tds, ct);
            return Ok(new { activado = r.Activado, requiere3DS = r.Requiere3DS, pagosRegistrados = r.PagosRegistrados });
        }
        catch (CulqiException e) { return BadRequest(new { mensaje = e.Message }); }
    }

    [HttpPost("cancelar")]
    public async Task<IActionResult> Cancelar(CancellationToken ct)
    {
        try { await pagos.CancelarAsync(NegocioId, ct); return Ok(new { cancelado = true }); }
        catch (CulqiException e) { return BadRequest(new { mensaje = e.Message }); }
    }

    /// <summary>Consulta a Culqi y registra cobros pendientes (respaldo si el webhook se demoró).</summary>
    [HttpPost("sincronizar")]
    public async Task<IActionResult> Sincronizar(CancellationToken ct)
    {
        var auto = await pagos.ObtenerAsync(NegocioId, ct);
        if (auto.SubscriptionId is null) return Ok(new { pagosRegistrados = 0 });
        try { return Ok(new { pagosRegistrados = await pagos.ConciliarAsync(NegocioId, auto.SubscriptionId, ct) }); }
        catch (CulqiException e) { return BadRequest(new { mensaje = e.Message }); }
    }

    private static string? Validar(ActivarPagoRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length < 2) return "Escribe el nombre del titular de la tarjeta.";
        if (string.IsNullOrWhiteSpace(r.Apellido) || r.Apellido.Trim().Length < 2) return "Escribe el apellido del titular de la tarjeta.";
        if (string.IsNullOrWhiteSpace(r.Email) || !Regex.IsMatch(r.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return "Escribe un correo válido.";
        if (Regex.Replace(r.Telefono ?? "", @"[^\d]", "").Length is < 6 or > 15) return "Escribe un celular válido.";
        if (string.IsNullOrWhiteSpace(r.Direccion) || r.Direccion.Trim().Length < 5) return "Escribe la dirección de facturación (mínimo 5 caracteres).";
        if (string.IsNullOrWhiteSpace(r.Ciudad) || r.Ciudad.Trim().Length < 2) return "Escribe la ciudad.";
        return null;
    }
}

/// <summary>
/// Webhook de Culqi (URL a registrar en CulqiPanel: https://app.lunalav.pe/api/webhooks/culqi).
/// Culqi no firma los avisos, así que no se confía en su contenido: el evento se vuelve a consultar
/// a Culqi con la llave secreta (uno inventado no existe y se descarta) y la suscripción se concilia
/// desde Culqi, de forma idempotente (un aviso repetido no duplica pagos).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/webhooks/culqi")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("public-read")]
public class CulqiWebhookController(SuscripcionCulqiService pagos, CulqiClient culqi,
    ILogger<CulqiWebhookController> log) : ControllerBase
{
    private static readonly Regex IdCulqiRegex = new(@"(?:sxn|crd|cus)_(?:test|live)_[A-Za-z0-9]+", RegexOptions.Compiled);

    [HttpPost]
    public async Task<IActionResult> Recibir([FromBody] JsonElement body, CancellationToken ct)
    {
        if (!culqi.Configurado) return Ok(); // aún sin llaves: se acepta para que Culqi no reintente en bucle

        var eventId = body.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var tipo = body.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;
        // Suscripciones (creación, actualización, cancelación) y cargos: el panel de Culqi no ofrece un
        // evento de "cobro de suscripción", los cobros mensuales llegan como charge.creation.*.
        if (string.IsNullOrWhiteSpace(eventId) || tipo is null
            || !(tipo.StartsWith("subscription.", StringComparison.Ordinal) || tipo.StartsWith("charge.", StringComparison.Ordinal)))
            return Ok();

        try
        {
            // Verificación: el evento debe existir en Culqi con esta llave secreta.
            var evento = await culqi.ObtenerEventoAsync(eventId, ct);
            var ids = IdCulqiRegex.Matches(evento.ToJsonString()).Select(x => x.Value).Distinct().ToList();

            // La empresa se identifica por la suscripción o, en un cargo, por su tarjeta o cliente.
            var ref_ = await pagos.BuscarPorIdsCulqiAsync(ids, ct);
            if (ref_ is null) { log.LogInformation("Evento Culqi {Evento} ({Tipo}) sin empresa de LunaLav asociada.", eventId, tipo); return Ok(); }
            var (negocioId, subscriptionId) = ref_.Value;

            var nuevos = await pagos.ConciliarAsync(negocioId, subscriptionId, ct);
            log.LogInformation("Webhook Culqi {Tipo} ({Evento}): {Nuevos} pago(s) registrado(s) para el negocio {Negocio}.",
                tipo, eventId, nuevos, negocioId);
            return Ok();
        }
        catch (CulqiException e)
        {
            // Evento inexistente o Culqi caído: 400 para que Culqi reintente más tarde.
            log.LogWarning("No se pudo verificar el webhook Culqi {Evento}: {Mensaje}", eventId, e.Message);
            return BadRequest();
        }
    }
}
