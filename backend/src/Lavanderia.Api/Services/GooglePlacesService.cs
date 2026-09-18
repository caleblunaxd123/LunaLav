using System.Net.Http.Json;
using System.Text.Json;

namespace Lavanderia.Api.Services;

public sealed class GooglePlacesOptions { public string? ApiKey { get; set; } }
public record GooglePlaceResult(string ProviderPlaceId,string Nombre,string? Direccion,string? Telefono,decimal? Rating,int? Resenas,decimal? Latitud,decimal? Longitud,string? SitioWeb);

/// <summary>Adaptador de Places API (New). La clave queda solo en configuración local/variables de entorno.</summary>
public sealed class GooglePlacesService(HttpClient http,IConfiguration config)
{
    private string? Key => config.GetValue<string>("GooglePlaces:ApiKey");
    public bool Configured => !string.IsNullOrWhiteSpace(Key);

    public async Task<IReadOnlyList<GooglePlaceResult>> SearchTextAsync(string text,int max,CancellationToken ct)
    {
        if(!Configured) throw new InvalidOperationException("Google Places no está configurado. Agrega GooglePlaces:ApiKey en appsettings.Local.json o una variable de entorno.");
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://places.googleapis.com/v1/places:searchText");
        request.Headers.Add("X-Goog-Api-Key",Key);request.Headers.Add("X-Goog-FieldMask","places.id,places.displayName,places.formattedAddress,places.location,places.rating,places.userRatingCount,places.nationalPhoneNumber,places.websiteUri");
        request.Content=JsonContent.Create(new{textQuery=text,languageCode="es",regionCode="PE",maxResultCount=Math.Clamp(max,1,20)});
        using var response=await http.SendAsync(request,ct);var json=await response.Content.ReadAsStringAsync(ct);if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"Google Places respondió {(int)response.StatusCode}: {json[..Math.Min(json.Length,400)]}");
        using var doc=JsonDocument.Parse(json);if(!doc.RootElement.TryGetProperty("places",out var places))return [];
        return places.EnumerateArray().Select(p=>new GooglePlaceResult(
            p.TryGetProperty("id",out var id)?id.GetString()??"":"",
            p.TryGetProperty("displayName",out var n)&&n.TryGetProperty("text",out var nt)?nt.GetString()??"Sin nombre":"Sin nombre",
            p.TryGetProperty("formattedAddress",out var a)?a.GetString():null,
            p.TryGetProperty("nationalPhoneNumber",out var phone)?phone.GetString():null,
            p.TryGetProperty("rating",out var rat)&&rat.TryGetDecimal(out var rv)?rv:null,
            p.TryGetProperty("userRatingCount",out var reviews)&&reviews.TryGetInt32(out var rc)?rc:null,
            p.TryGetProperty("location",out var loc)&&loc.TryGetProperty("latitude",out var lat)&&lat.TryGetDecimal(out var lv)?lv:null,
            p.TryGetProperty("location",out loc)&&loc.TryGetProperty("longitude",out var lng)&&lng.TryGetDecimal(out var lnv)?lnv:null,
            p.TryGetProperty("websiteUri",out var web)?web.GetString():null)).ToList();
    }
}
