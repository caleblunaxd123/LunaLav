using System.Net.Http.Json;
using System.Text.Json;

namespace Lavanderia.Api.Services;

public record PublicacionPrompt(string? Tipo, string? Temporada, string? Negocio, string? Oferta, string? Zona, string? Contacto, bool Emojis = true);

/// <summary>Genera copys de marketing con un modelo local de Ollama. Gratis, sin API key ni facturación;
/// requiere que el servicio Ollama esté corriendo en la máquina (http://localhost:11434).</summary>
public sealed class OllamaService(HttpClient http, IConfiguration config)
{
    private readonly string _base = (config["Ollama:BaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
    private readonly string _model = config["Ollama:Model"] ?? "llama3.2:latest";

    public async Task<string> GenerarPublicacionAsync(PublicacionPrompt p, CancellationToken ct)
    {
        var body = new { model = _model, prompt = ConstruirPrompt(p), stream = false, options = new { temperature = 0.85 } };
        HttpResponseMessage resp;
        try { resp = await http.PostAsJsonAsync($"{_base}/api/generate", body, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { throw new InvalidOperationException("No se pudo contactar a Ollama. Verifica que el servicio esté corriendo."); }
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama respondió {(int)resp.StatusCode}. Revisa que el modelo '{_model}' esté instalado (ollama pull {_model}).");
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var texto = json.TryGetProperty("response", out var r) ? r.GetString() : null;
        if (string.IsNullOrWhiteSpace(texto)) throw new InvalidOperationException("El modelo no devolvió texto.");
        return texto.Trim().Trim('"');
    }

    private static string ConstruirPrompt(PublicacionPrompt p)
    {
        var tipo = (p.Tipo ?? "promo") switch
        {
            "novedad" => "una novedad o apertura",
            "consejo" => "un consejo o tip útil de lavandería",
            "testimonio" => "un testimonio de cliente satisfecho",
            _ => "una promoción o descuento"
        };
        var temporada = string.IsNullOrWhiteSpace(p.Temporada) || p.Temporada == "ninguna" ? "" : $" Adáptalo a la temporada: {p.Temporada}.";
        var datos = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Negocio)) datos.Add($"negocio: {p.Negocio}");
        if (!string.IsNullOrWhiteSpace(p.Oferta)) datos.Add($"mensaje u oferta: {p.Oferta}");
        if (!string.IsNullOrWhiteSpace(p.Zona)) datos.Add($"zona: {p.Zona}");
        if (!string.IsNullOrWhiteSpace(p.Contacto)) datos.Add($"contacto: {p.Contacto}");
        var emojis = p.Emojis ? "Incluye algunos emojis apropiados." : "No uses emojis.";
        return $"Eres experto en marketing digital para lavanderías en Perú. Escribe UNA sola publicación breve para redes sociales (Instagram y Facebook) en español, con tono cercano y persuasivo, sobre {tipo}.{temporada}\n"
             + $"Datos del negocio: {string.Join("; ", datos)}.\n"
             + $"{emojis} Máximo 60 palabras. Incluye un llamado a la acción claro y termina con 4 a 6 hashtags relevantes. "
             + "Devuelve únicamente el texto listo para publicar, sin comillas, sin títulos y sin explicaciones.";
    }
}
