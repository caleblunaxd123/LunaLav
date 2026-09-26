using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace Lavanderia.Api.Services.Facturacion;

/// <summary>
/// Resultado de validar un RUC. <see cref="Verificado"/> indica si se pudo consultar el padrón de
/// SUNAT; si ambos proveedores fallan solo queda la validación del dígito verificador y el RUC
/// no se rechaza por eso (no se bloquea la operación por una caída de un servicio externo).
/// </summary>
public sealed record RucConsulta(
    string Ruc, bool FormatoValido, bool Verificado, bool Existe,
    string? RazonSocial, string? Estado, string? Condicion, string? Direccion)
{
    /// <summary>SUNAT solo acepta facturas a contribuyentes ACTIVO y HABIDO.</summary>
    public bool ActivoHabido => string.Equals(Estado, "ACTIVO", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(Condicion, "HABIDO", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mensaje para el usuario cuando el RUC no sirve; null si está todo bien.</summary>
    public string? Problema => !FormatoValido
        ? "El RUC no es válido: revisa los 11 dígitos."
        : Verificado && !Existe
            ? "Ese RUC no existe en el padrón de SUNAT."
            : null;

    /// <summary>Advertencia no bloqueante (el RUC existe pero no está ACTIVO y HABIDO).</summary>
    public string? Advertencia => Verificado && Existe && !ActivoHabido
        ? $"SUNAT lo reporta como {Estado ?? "?"} / {Condicion ?? "?"}: no podrás emitirle facturas."
        : null;
}

/// <summary>Nombre de una persona por DNI (padrón de RENIEC vía apis.net.pe). Nunca bloquea: solo autocompleta.</summary>
public sealed record DniConsulta(string Dni, bool FormatoValido, bool Verificado, bool Existe,
    string? NombreCompleto, string? Nombres, string? ApellidoPaterno, string? ApellidoMaterno);

/// <summary>
/// Consulta de RUC contra el padrón de SUNAT mediante APIs públicas gratuitas (sin token):
/// OpenRUC (principal) y apis.net.pe v1 (respaldo). Resultados en caché 24 h.
/// </summary>
public sealed class RucConsultaService(HttpClient http, IMemoryCache cache, ILogger<RucConsultaService> log)
{
    private static readonly TimeSpan Duracion = TimeSpan.FromHours(24);

    public async Task<RucConsulta> ConsultarAsync(string? rucEntrada, CancellationToken ct)
    {
        var ruc = new string((rucEntrada ?? "").Where(char.IsDigit).ToArray());
        if (!DocumentoFiscalValidator.EsRucValido(ruc))
            return new RucConsulta(ruc, false, false, false, null, null, null, null);

        var clave = $"ruc:{ruc}";
        if (cache.TryGetValue(clave, out RucConsulta? guardado) && guardado is not null) return guardado;

        var resultado = await ConsultarOpenRucAsync(ruc, ct) ?? await ConsultarApisNetPeAsync(ruc, ct);
        if (resultado is null)
            return new RucConsulta(ruc, true, false, false, null, null, null, null); // sin verificar: no se cachea

        cache.Set(clave, resultado, Duracion);
        return resultado;
    }

    public async Task<DniConsulta> ConsultarDniAsync(string? dniEntrada, CancellationToken ct)
    {
        var dni = new string((dniEntrada ?? "").Where(char.IsDigit).ToArray());
        if (dni.Length != 8) return new DniConsulta(dni, false, false, false, null, null, null, null);

        var clave = $"dni:{dni}";
        if (cache.TryGetValue(clave, out DniConsulta? guardado) && guardado is not null) return guardado;
        try
        {
            using var response = await http.GetAsync($"https://api.apis.net.pe/v1/dni?numero={dni}", ct);
            DniConsulta? r = null;
            if (response.StatusCode == HttpStatusCode.NotFound)
                r = new DniConsulta(dni, true, true, false, null, null, null, null);
            else if (response.IsSuccessStatusCode)
            {
                var j = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;
                var nombres = Texto(j, "nombres");
                var paterno = Texto(j, "apellidoPaterno");
                var materno = Texto(j, "apellidoMaterno");
                // "Nombres Apellidos" se lee mejor en el mostrador que el formato de RENIEC (apellidos primero).
                var completo = nombres is null ? Texto(j, "nombre") : string.Join(" ", new[] { nombres, paterno, materno }.Where(x => x is not null));
                if (completo is not null) r = new DniConsulta(dni, true, true, true, completo, nombres, paterno, materno);
            }
            if (r is null) return new DniConsulta(dni, true, false, false, null, null, null, null); // 429/caída: sin cachear
            cache.Set(clave, r, Duracion);
            return r;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            log.LogInformation("Consulta de DNI no disponible: {Error}", e.Message);
            return new DniConsulta(dni, true, false, false, null, null, null, null);
        }
    }

    private async Task<RucConsulta?> ConsultarOpenRucAsync(string ruc, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"https://openruc.com/api/ruc/{ruc}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new RucConsulta(ruc, true, true, false, null, null, null, null);
            if (!response.IsSuccessStatusCode) return null;
            var j = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;
            var razon = Texto(j, "razon_social");
            if (razon is null) return null;
            return new RucConsulta(ruc, true, true, true, razon, Texto(j, "estado"), Texto(j, "condicion"), Texto(j, "direccion"));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            log.LogInformation("OpenRUC no respondió para {Ruc}: {Error}", ruc, e.Message);
            return null;
        }
    }

    private async Task<RucConsulta?> ConsultarApisNetPeAsync(string ruc, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"https://api.apis.net.pe/v1/ruc?numero={ruc}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new RucConsulta(ruc, true, true, false, null, null, null, null);
            if (!response.IsSuccessStatusCode) return null;
            var j = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;
            var razon = Texto(j, "nombre");
            if (razon is null) return null;
            return new RucConsulta(ruc, true, true, true, razon, Texto(j, "estado"), Texto(j, "condicion"), Texto(j, "direccion"));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            log.LogInformation("apis.net.pe no respondió para {Ruc}: {Error}", ruc, e.Message);
            return null;
        }
    }

    private static string? Texto(JsonObject? o, string key)
        => o?[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
}
