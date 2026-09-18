using System.Globalization;
using System.Text.Json;

namespace Lavanderia.Api.Services;

/// <summary>Descubrimiento de lavanderías con OpenStreetMap (Nominatim + Overpass). Gratis, sin API key ni facturación.
/// Devuelve el mismo formato que <see cref="GooglePlaceResult"/> para que el frontend no distinga el proveedor.</summary>
public sealed class OpenStreetMapPlacesService(HttpClient http, IConfiguration config)
{
    private readonly string _nominatim = (config["Geocodificacion:BaseUrl"] ?? "https://nominatim.openstreetmap.org").TrimEnd('/');
    private readonly string _overpass = config["OpenStreetMap:OverpassUrl"] ?? "https://overpass-api.de/api/interpreter";
    private readonly string _userAgent = config["Geocodificacion:UserAgent"] ?? "LunaLav/1.0 (contacto@lunalav.pe)";

    public async Task<IReadOnlyList<GooglePlaceResult>> SearchLaundriesAsync(string query, int max, CancellationToken ct)
    {
        var (lat, lon, radius) = await ResolveAreaAsync(query, ct);
        return await QueryAroundAsync(lat, lon, radius, max, ct);
    }

    public Task<IReadOnlyList<GooglePlaceResult>> SearchAroundAsync(decimal lat, decimal lon, int max, CancellationToken ct)
        => QueryAroundAsync(lat, lon, 4500, max, ct);

    private async Task<IReadOnlyList<GooglePlaceResult>> QueryAroundAsync(decimal lat, decimal lon, int radius, int max, CancellationToken ct)
    {
        max = Math.Clamp(max, 1, 40);
        var c = CultureInfo.InvariantCulture;
        var overpassQuery = $@"[out:json][timeout:25];
(
  node[""shop""~""laundry|dry_cleaning""](around:{radius},{lat.ToString(c)},{lon.ToString(c)});
  way[""shop""~""laundry|dry_cleaning""](around:{radius},{lat.ToString(c)},{lon.ToString(c)});
);
out center {max};";
        using var req = new HttpRequestMessage(HttpMethod.Post, _overpass) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = overpassQuery }) };
        req.Headers.UserAgent.ParseAdd(_userAgent);
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"OpenStreetMap (Overpass) respondió {(int)resp.StatusCode}. Intenta de nuevo en unos segundos.");
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("elements", out var elements)) return [];
        var list = new List<GooglePlaceResult>();
        foreach (var el in elements.EnumerateArray())
        {
            var tags = el.TryGetProperty("tags", out var t) ? t : default;
            var nombre = TagOrNull(tags, "name");
            if (string.IsNullOrWhiteSpace(nombre)) continue;
            decimal? elat = null, elon = null;
            if (el.TryGetProperty("lat", out var la) && la.TryGetDecimal(out var lav)) elat = lav;
            if (el.TryGetProperty("lon", out var lo) && lo.TryGetDecimal(out var lov)) elon = lov;
            if (elat is null && el.TryGetProperty("center", out var center))
            {
                if (center.TryGetProperty("lat", out var cla) && cla.TryGetDecimal(out var clav)) elat = clav;
                if (center.TryGetProperty("lon", out var clo) && clo.TryGetDecimal(out var clov)) elon = clov;
            }
            var tipo = el.TryGetProperty("type", out var ty) ? ty.GetString() : "node";
            var id = $"osm:{tipo}:{(el.TryGetProperty("id", out var idp) ? idp.GetRawText() : Guid.NewGuid().ToString())}";
            list.Add(new GooglePlaceResult(id, nombre!, ComposeAddress(tags), TagOrNull(tags, "phone") ?? TagOrNull(tags, "contact:phone"),
                null, null, elat, elon, TagOrNull(tags, "website") ?? TagOrNull(tags, "contact:website")));
            if (list.Count >= max) break;
        }
        return list;
    }

    private async Task<(decimal lat, decimal lon, int radius)> ResolveAreaAsync(string query, CancellationToken ct)
    {
        // Lima metropolitana como respaldo cuando no hay ubicación específica.
        var fallback = (-12.0464m, -77.0428m, 18000);
        var clean = CleanLocation(query);
        if (string.IsNullOrWhiteSpace(clean)) return fallback;
        try
        {
            var url = $"{_nominatim}/search?format=jsonv2&limit=1&countrycodes=pe&accept-language=es&q={Uri.EscapeDataString(clean + ", Lima, Perú")}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(_userAgent);
            using var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                foreach (var first in doc.RootElement.EnumerateArray())
                {
                    if (first.TryGetProperty("lat", out var la) && first.TryGetProperty("lon", out var lo)
                        && decimal.TryParse(la.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lat)
                        && decimal.TryParse(lo.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
                        return (lat, lon, 6000);
                    break;
                }
            }
        }
        catch { /* cae al respaldo de Lima */ }
        return fallback;
    }

    private static string CleanLocation(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return "";
        var lowered = q.ToLowerInvariant();
        foreach (var w in new[] { "lavanderías", "lavanderias", "lavandería", "lavanderia", "laundry", "dry cleaning", "negocios", "perú", "peru", "lima" })
            lowered = lowered.Replace(w, " ");
        return lowered.Trim(' ', ',');
    }

    private static string? TagOrNull(JsonElement tags, string key)
        => tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(key, out var v) ? v.GetString() : null;

    private static string? ComposeAddress(JsonElement tags)
    {
        if (tags.ValueKind != JsonValueKind.Object) return null;
        var street = TagOrNull(tags, "addr:street"); var num = TagOrNull(tags, "addr:housenumber");
        var city = TagOrNull(tags, "addr:city") ?? TagOrNull(tags, "addr:suburb");
        var line = string.Join(" ", new[] { street, num }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var addr = string.Join(", ", new[] { line, city }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(addr) ? null : addr;
    }
}
