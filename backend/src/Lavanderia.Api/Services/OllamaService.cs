using System.Net.Http.Json;
using System.Text.Json;

namespace Lavanderia.Api.Services;

public record PublicacionPrompt(string? Tipo, string? Temporada, string? Negocio, string? Oferta, string? Zona, string? Contacto, bool Emojis = true, string? Modelo = null);

/// <summary>Genera copys de marketing con un modelo local de Ollama. Gratis, sin API key ni facturación;
/// requiere que el servicio Ollama esté corriendo en la máquina (http://localhost:11434).</summary>
public sealed class OllamaService(HttpClient http, IConfiguration config)
{
    private readonly string _base = (config["Ollama:BaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
    private readonly string _model = config["Ollama:Model"] ?? "qwen3:8b";
    private static readonly HashSet<string> Permitidos = new(StringComparer.OrdinalIgnoreCase)
    { "qwen3:8b", "llama3.2:latest", "llama3.1:8b", "gemma4:latest", "qwen2.5-coder:14b" };

    public Task<string> GenerarPublicacionAsync(PublicacionPrompt p, CancellationToken ct)
        => GenerateRawAsync(ModeloValido(p.Modelo), ConstruirPrompt(p), ct);

    public Task<string> GenerarRespuestaAsync(string contexto, string? instruccion, string? modelo, CancellationToken ct)
        => GenerateRawAsync(ModeloValido(modelo), ConstruirRespuesta(contexto ?? "", instruccion), ct);

    public Task<string> GenerarLibreAsync(string prompt, string? modelo, CancellationToken ct)
        => GenerateRawAsync(ModeloValido(modelo), prompt, ct);

    private string ModeloValido(string? m) => !string.IsNullOrWhiteSpace(m) && Permitidos.Contains(m!) ? m! : _model;

    private async Task<string> GenerateRawAsync(string model, string prompt, CancellationToken ct)
    {
        var body = new { model, prompt, stream = false, think = false, options = new { temperature = 0.8 } };
        HttpResponseMessage resp;
        try { resp = await http.PostAsJsonAsync($"{_base}/api/generate", body, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { throw new InvalidOperationException("No se pudo contactar a Ollama. Verifica que el servicio esté corriendo."); }
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama respondió {(int)resp.StatusCode}. Revisa que el modelo '{model}' esté instalado (ollama pull {model}).");
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var texto = json.TryGetProperty("response", out var r) ? r.GetString() : null;
        if (string.IsNullOrWhiteSpace(texto)) throw new InvalidOperationException("El modelo no devolvió texto.");
        return Limpiar(texto);
    }

    private static string ConstruirRespuesta(string contexto, string? instruccion)
    {
        var extra = string.IsNullOrWhiteSpace(instruccion) ? "" : $" Instrucción adicional: {instruccion}.";
        var ctx = contexto.Length > 1500 ? contexto[..1500] : contexto;
        return "Eres del equipo de ventas de LunaLav, un SISTEMA DE GESTIÓN (software) para lavanderías (no una lavandería). "
             + "Redacta una respuesta breve, cordial y profesional en español al siguiente correo de un posible cliente (dueño de lavandería)."
             + extra + " No inventes datos ni precios; si preguntan por precios, ofrece coordinar una llamada o demo. Termina firmando como 'Equipo LunaLav'.\n\n"
             + "Correo recibido:\n\"\"\"\n" + ctx + "\n\"\"\"\n\nDevuelve solo el texto de la respuesta, sin asunto, sin comillas y sin explicaciones.";
    }

    // Modelos "thinking" (qwen3) pueden anteponer un bloque <think>...</think>: lo removemos.
    private static string Limpiar(string texto)
    {
        var i = texto.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (i >= 0) texto = texto[(i + "</think>".Length)..];
        texto = texto.Replace("**", "").Replace("__", "");
        return texto.Trim().Trim('"').Trim();
    }

    private static string ConstruirPrompt(PublicacionPrompt p)
    {
        var marca = string.IsNullOrWhiteSpace(p.Negocio) ? "LunaLav" : p.Negocio!.Trim();
        var tipo = (p.Tipo ?? "promo") switch
        {
            "novedad" => "una nueva función o novedad del sistema",
            "consejo" => "un consejo de gestión para dueños de lavanderías",
            "testimonio" => $"un testimonio de un dueño de lavandería que usa {marca}",
            _ => "una promoción del sistema"
        };
        var temporada = string.IsNullOrWhiteSpace(p.Temporada) || p.Temporada == "ninguna" ? "" : $" Relaciónalo con la temporada {p.Temporada} (más carga de trabajo para la lavandería).";
        var datos = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Oferta)) datos.Add($"mensaje u oferta: {p.Oferta}");
        if (!string.IsNullOrWhiteSpace(p.Zona)) datos.Add($"zona: {p.Zona}");
        if (!string.IsNullOrWhiteSpace(p.Contacto)) datos.Add($"contacto: {p.Contacto}");
        var emojis = p.Emojis ? "Incluye algunos emojis apropiados." : "No uses emojis.";
        return $"Contexto: {marca} es un SISTEMA DE GESTIÓN (software en la nube) para lavanderías. Ayuda a los dueños de lavanderías a administrar pedidos, clientes, inventario, cobros/facturación, reportes y avisos por WhatsApp. MUY IMPORTANTE: {marca} NO es una lavandería y NO lava ropa; es el software que usan las lavanderías.\n"
             + $"Tarea: escribe UNA sola publicación breve para redes sociales (Instagram y Facebook) en español, dirigida a DUEÑOS de lavanderías en Perú para que se interesen en {marca}, sobre {tipo}.{temporada}\n"
             + $"Datos: {(datos.Count > 0 ? string.Join("; ", datos) : "sin datos extra")}.\n"
             + $"Reglas: destaca beneficios del sistema (ahorro de tiempo, orden, menos errores, más ventas, control desde el celular). NO inventes servicios que {marca} no ofrece: nada de lavavajillas, secado, planchado ni delivery de ropa. {emojis} Máximo 60 palabras, incluye un llamado a la acción (ej. pedir una demo) y termina con 4 a 6 hashtags relevantes como #Lavanderias #GestionDeLavanderia #SoftwareParaLavanderias. Devuelve únicamente el texto listo para publicar, sin comillas, sin títulos y sin explicaciones.";
    }
}
