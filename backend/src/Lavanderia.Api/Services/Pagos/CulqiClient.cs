using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lavanderia.Api.Services.Pagos;

/// <summary>Error de negocio de Culqi con el mensaje apto para el usuario (user_message).</summary>
public sealed class CulqiException(string mensaje, string? codigo = null) : Exception(mensaje)
{
    public string? Codigo { get; } = codigo;
}

/// <summary>Parámetros que devuelve Culqi3DS en el navegador tras autenticar la tarjeta.</summary>
public sealed record Parametros3DS(string Eci, string Xid, string Cavv, string ProtocolVersion, string DirectoryServerTransactionId);

public sealed record CulqiTarjeta(string Id, string? Marca, string? Ultimos4);

/// <summary>Resultado de asociar la tarjeta: o la tarjeta quedó creada, o el banco pide 3DS.</summary>
public sealed record CulqiResultadoTarjeta(CulqiTarjeta? Tarjeta, bool Requiere3DS);

public sealed record CulqiCargo(string ChargeId, bool Exitoso, decimal MontoSoles, DateTimeOffset? Fecha, string? Error);

public sealed record CulqiSuscripcion(string Id, int Estado, IReadOnlyList<CulqiCargo> Cargos)
{
    // Estados de la suscripción según la API: 1 creada, 2 prueba, 3 activa, 4 cancelada, 5 en cola, 6 vencida.
    public bool Cancelada => Estado is 4 or 6;
}

/// <summary>
/// Cliente HTTP de la API v2 de Culqi (https://apidocs.culqi.com, especificación apiculqi.yaml).
/// Usa la llave SECRETA (nunca se envía al navegador). La tarjeta llega ya tokenizada por el
/// checkout de Culqi, así que el número de tarjeta nunca pasa por este servidor.
/// </summary>
public sealed class CulqiClient(HttpClient http, IConfiguration config)
{
    private const string Base = "https://api.culqi.com/v2";

    public string? PublicKey => config.GetValue<string>("Culqi:PublicKey");
    private string? SecretKey => config.GetValue<string>("Culqi:SecretKey");
    public bool Configurado => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(SecretKey);
    /// <summary>TEST o LIVE según la llave: los ids creados con una no existen en la otra.</summary>
    public string Modo => SecretKey?.StartsWith("sk_live", StringComparison.Ordinal) == true ? "LIVE" : "TEST";

    public async Task<string> CrearClienteAsync(string nombre, string apellido, string email, string telefono,
        string direccion, string ciudad, int negocioId, CancellationToken ct)
    {
        var r = await PostAsync("/customers/", new
        {
            first_name = Recortar(nombre, 50), last_name = Recortar(apellido, 50), email = Recortar(email, 50),
            address = Recortar(direccion, 100), address_city = Recortar(ciudad, 30), country_code = "PE",
            phone_number = Recortar(telefono, 15), metadata = new { negocioId = negocioId.ToString() }
        }, ct);
        return Texto(r, "id") ?? throw new CulqiException("Culqi no devolvió el cliente creado.");
    }

    /// <summary>Asocia la tarjeta tokenizada al cliente. Si el banco exige 3DS, Culqi responde REVIEW.</summary>
    public async Task<CulqiResultadoTarjeta> CrearTarjetaAsync(string customerId, string tokenId, Parametros3DS? tds, CancellationToken ct)
    {
        object body = tds is null
            ? new { customer_id = customerId, token_id = tokenId }
            : new
            {
                customer_id = customerId, token_id = tokenId,
                authentication_3DS = new
                {
                    eci = tds.Eci, xid = tds.Xid, cavv = tds.Cavv,
                    protocolVersion = tds.ProtocolVersion, directoryServerTransactionId = tds.DirectoryServerTransactionId
                }
            };
        var r = await PostAsync("/cards/", body, ct);
        if (string.Equals(Texto(r, "action_code"), "REVIEW", StringComparison.OrdinalIgnoreCase))
            return new CulqiResultadoTarjeta(null, true);

        var id = Texto(r, "id") ?? throw new CulqiException("Culqi no devolvió la tarjeta creada.");
        var source = r["source"];
        var marca = source?["iin"]?["card_brand"]?.GetValue<string>();
        var ultimos = source?["last_four"]?.GetValue<string>() ?? source?["card_number"]?.GetValue<string>()?[^4..];
        return new CulqiResultadoTarjeta(new CulqiTarjeta(id, marca, ultimos), false);
    }

    /// <summary>Plan mensual (interval_unit_time 3 = mensual; interval_count 0 = sin fin en producción).</summary>
    public async Task<string> CrearPlanMensualAsync(int montoCentimos, CancellationToken ct)
    {
        var soles = (montoCentimos / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var r = await PostAsync("/recurrent/plans/create", new
        {
            // Culqi rechaza "/" y otros símbolos en el nombre: solo letras, números y espacios.
            name = $"LunaLav mensual {soles} soles",
            short_name = $"lunalav-mensual-{montoCentimos}",
            description = $"Suscripcion mensual a LunaLav {soles} soles",
            amount = montoCentimos,
            currency = "PEN",
            interval_unit_time = 3,
            // 0 = cobro indefinido; el entorno de integración de Culqi solo admite 1 a 3.
            interval_count = Modo == "LIVE" ? 0 : 1,
            initial_cycles = new { count = 0, has_initial_charge = false, amount = 0, interval_unit_time = 3 }
        }, ct);
        return Texto(r, "id") ?? throw new CulqiException("Culqi no devolvió el plan creado.");
    }

    public async Task<string> CrearSuscripcionAsync(string cardId, string planId, int negocioId, CancellationToken ct)
    {
        var r = await PostAsync("/recurrent/subscriptions/create",
            new { card_id = cardId, plan_id = planId, tyc = true, metadata = new { negocioId = negocioId.ToString() } }, ct);
        return Texto(r, "id") ?? throw new CulqiException("Culqi no devolvió la suscripción creada.");
    }

    public async Task<CulqiSuscripcion> ObtenerSuscripcionAsync(string subscriptionId, CancellationToken ct)
    {
        var r = await EnviarAsync(HttpMethod.Get, $"/recurrent/subscriptions/{Uri.EscapeDataString(subscriptionId)}/", null, ct);
        var cargos = new List<CulqiCargo>();
        // "periods" y "charges" llegan como lista (o como objeto único en algunos ejemplos).
        foreach (var periodo in ComoLista(r["periods"]))
            foreach (var c in ComoLista(periodo?["charges"]))
            {
                var chargeId = c?["charge_id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(chargeId)) continue;
                var estado = Entero(c?["charger_status"]) ?? 0; // 1 exitoso, 2 fallido
                var monto = (Entero(c?["amount"]) ?? 0) / 100m;
                var dia = Entero64(c?["charge_day"]);
                cargos.Add(new CulqiCargo(chargeId, estado == 1, monto, dia is long s ? DesdeUnix(s) : null, c?["error"]?.GetValue<string>()));
            }
        return new CulqiSuscripcion(Texto(r, "id") ?? subscriptionId, Entero(r["status"]) ?? 0, cargos);
    }

    /// <summary>Cancela el cobro automático (en Culqi la cancelación es inmediata e irreversible).</summary>
    public Task CancelarSuscripcionAsync(string subscriptionId, CancellationToken ct)
        => EnviarAsync(HttpMethod.Delete, $"/recurrent/subscriptions/{Uri.EscapeDataString(subscriptionId)}/", null, ct);

    /// <summary>Consulta un evento por id con la llave secreta: así se valida un webhook sin confiar en su contenido.</summary>
    public Task<JsonObject> ObtenerEventoAsync(string eventId, CancellationToken ct)
        => EnviarAsync(HttpMethod.Get, $"/events/{Uri.EscapeDataString(eventId)}", null, ct);

    private Task<JsonObject> PostAsync(string path, object body, CancellationToken ct) => EnviarAsync(HttpMethod.Post, path, body, ct);

    private async Task<JsonObject> EnviarAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!Configurado) throw new CulqiException("El pago con tarjeta no está configurado todavía.");
        using var request = new HttpRequestMessage(method, Base + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        JsonObject? json = null;
        try { json = string.IsNullOrWhiteSpace(raw) ? new JsonObject() : JsonNode.Parse(raw) as JsonObject; } catch (JsonException) { }
        if (!response.IsSuccessStatusCode)
        {
            var mensaje = json?["user_message"]?.GetValue<string>() ?? json?["merchant_message"]?.GetValue<string>()
                ?? "No pudimos procesar el pago con Culqi. Inténtalo nuevamente.";
            throw new CulqiException(mensaje, json?["code"]?.GetValue<string>() ?? json?["decline_code"]?.GetValue<string>());
        }
        return json ?? new JsonObject();
    }

    private static IEnumerable<JsonNode?> ComoLista(JsonNode? node) => node switch
    {
        JsonArray a => a,
        JsonObject o => new JsonNode?[] { o },
        _ => Array.Empty<JsonNode?>()
    };
    private static string? Texto(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static int? Entero(JsonNode? n) => n is JsonValue v ? (v.TryGetValue<int>(out var i) ? i : v.TryGetValue<decimal>(out var d) ? (int)d : v.TryGetValue<string>(out var s) && int.TryParse(s, out var p) ? p : null) : null;
    private static long? Entero64(JsonNode? n) => n is JsonValue v ? (v.TryGetValue<long>(out var l) ? l : v.TryGetValue<string>(out var s) && long.TryParse(s, out var p) ? p : null) : null;
    // Culqi usa segundos o milisegundos UNIX según el recurso.
    private static DateTimeOffset DesdeUnix(long valor) => valor > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(valor) : DateTimeOffset.FromUnixTimeSeconds(valor);
    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
