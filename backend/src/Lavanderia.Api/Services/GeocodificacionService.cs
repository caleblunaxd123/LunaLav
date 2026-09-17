using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Lavanderia.Api.Services;

public record ResultadoGeocodificacion(
    string Id,
    decimal Latitud,
    decimal Longitud,
    string Etiqueta,
    string? Direccion,
    string? Distrito);

public class GeocodificacionService
{
    private static readonly SemaphoreSlim SolicitudExterna = new(1, 1);
    private static DateTime _ultimaSolicitudUtc = DateTime.MinValue;

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly string _baseUrl;
    private readonly string _userAgent;

    public GeocodificacionService(HttpClient http, IMemoryCache cache, IConfiguration config)
    {
        _http = http;
        _cache = cache;
        _baseUrl = (config["Geocodificacion:BaseUrl"] ?? "https://nominatim.openstreetmap.org").TrimEnd('/');
        _userAgent = config["Geocodificacion:UserAgent"] ?? "LunaLav/1.0 (LunaIT Solution)";
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<IReadOnlyList<ResultadoGeocodificacion>> BuscarAsync(
        string direccion, string distrito, CancellationToken ct)
    {
        var consulta = $"{direccion.Trim()}, {distrito.Trim()}, Lima, Perú";
        var clave = $"geo:buscar:{consulta.ToLowerInvariant()}";
        if (_cache.TryGetValue(clave, out List<ResultadoGeocodificacion>? cacheados) && cacheados is not null)
            return cacheados;

        var query = new Dictionary<string, string?>
        {
            ["format"] = "jsonv2",
            ["addressdetails"] = "1",
            ["limit"] = "5",
            ["countrycodes"] = "pe",
            ["layer"] = "address",
            ["dedupe"] = "1",
            ["accept-language"] = "es",
            ["q"] = consulta
        };
        var url = $"{_baseUrl}/search?{CrearQuery(query)}";
        var json = await ObtenerJsonAsync(url, ct);
        var resultados = LeerResultados(json).Take(5).ToList();
        _cache.Set(clave, resultados, TimeSpan.FromHours(24));
        return resultados;
    }

    public async Task<ResultadoGeocodificacion?> ReversaAsync(decimal latitud, decimal longitud, CancellationToken ct)
    {
        var lat = Math.Round(latitud, 5);
        var lon = Math.Round(longitud, 5);
        var clave = $"geo:reversa:{lat}:{lon}";
        if (_cache.TryGetValue(clave, out ResultadoGeocodificacion? cacheado)) return cacheado;

        var query = new Dictionary<string, string?>
        {
            ["format"] = "jsonv2",
            ["addressdetails"] = "1",
            ["accept-language"] = "es",
            ["lat"] = lat.ToString(CultureInfo.InvariantCulture),
            ["lon"] = lon.ToString(CultureInfo.InvariantCulture)
        };
        using var json = await ObtenerJsonAsync($"{_baseUrl}/reverse?{CrearQuery(query)}", ct);
        var resultado = LeerResultado(json.RootElement);
        if (resultado is not null) _cache.Set(clave, resultado, TimeSpan.FromHours(24));
        return resultado;
    }

    private async Task<JsonDocument> ObtenerJsonAsync(string url, CancellationToken ct)
    {
        await SolicitudExterna.WaitAsync(ct);
        try
        {
            var espera = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _ultimaSolicitudUtc);
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(_userAgent);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        finally
        {
            _ultimaSolicitudUtc = DateTime.UtcNow;
            SolicitudExterna.Release();
        }
    }

    private static IEnumerable<ResultadoGeocodificacion> LeerResultados(JsonDocument json)
    {
        using (json)
        {
            if (json.RootElement.ValueKind != JsonValueKind.Array) yield break;
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var resultado = LeerResultado(item);
                if (resultado is not null) yield return resultado;
            }
        }
    }

    private static ResultadoGeocodificacion? LeerResultado(JsonElement item)
    {
        if (!TryDecimal(item, "lat", out var latitud) || !TryDecimal(item, "lon", out var longitud)) return null;
        var etiqueta = Texto(item, "display_name");
        if (string.IsNullOrWhiteSpace(etiqueta)) return null;

        var address = item.TryGetProperty("address", out var detalle) ? detalle : default;
        var via = PrimerTexto(address, "road", "pedestrian", "residential", "path", "place", "neighbourhood");
        var numero = Texto(address, "house_number");
        var direccion = string.Join(' ', new[] { via, numero }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(direccion))
            direccion = string.Join(',', etiqueta.Split(',').Take(2)).Trim();
        var distrito = PrimerTexto(address, "city_district", "suburb", "town", "quarter", "neighbourhood", "village");
        var id = Texto(item, "place_id") ?? $"{latitud}:{longitud}";
        return new ResultadoGeocodificacion(id, latitud, longitud, etiqueta, direccion, distrito);
    }

    private static string CrearQuery(IReadOnlyDictionary<string, string?> valores)
        => string.Join('&', valores.Where(x => x.Value is not null)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));

    private static bool TryDecimal(JsonElement item, string propiedad, out decimal valor)
    {
        valor = 0;
        return item.TryGetProperty(propiedad, out var elemento)
               && decimal.TryParse(elemento.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out valor);
    }

    private static string? PrimerTexto(JsonElement item, params string[] propiedades)
        => propiedades.Select(p => Texto(item, p)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string? Texto(JsonElement item, string propiedad)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(propiedad, out var valor)) return null;
        return valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString(),
            JsonValueKind.Number => valor.GetRawText(),
            _ => null
        };
    }
}
