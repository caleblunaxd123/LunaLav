using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Services.Pagos;

/// <summary>
/// Enlaces de pago de la suscripción: abren la página de pago para celular sin iniciar sesión.
/// El enlace solo permite ver y pagar la suscripción de esa empresa (nada más de la cuenta),
/// vence a los <see cref="DiasVigencia"/> días y deja de servir en cuanto se activa el pago.
/// Solo se guarda el hash del token.
/// </summary>
public sealed class EnlacePagoService(ISqlConnectionFactory db, IConfiguration config)
{
    public const int DiasVigencia = 7;

    private string UrlApp => (config.GetValue<string>("Plataforma:UrlApp") ?? "https://app.lunalav.pe").TrimEnd('/');

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Crea un enlace nuevo y devuelve la URL completa.</summary>
    public async Task<string> CrearAsync(int negocioId, string canal, CancellationToken ct)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT dbo.EnlacePago (NegocioId, TokenHash, Expira, Canal)
                            VALUES (@N, @H, DATEADD(day, @D, SYSUTCDATETIME()), @C)";
        cmd.AddParam("@N", negocioId);
        cmd.AddParam("@H", Hash(token));
        cmd.AddParam("@D", DiasVigencia);
        cmd.AddParam("@C", canal);
        await cmd.ExecuteNonQueryAsync(ct);
        return $"{UrlApp}/pagar/{token}";
    }

    /// <summary>Empresa del enlace, o null si no existe, venció o fue revocado.</summary>
    public async Task<int?> ResolverAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 100) return null;
        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE dbo.EnlacePago SET UltimoUso = SYSUTCDATETIME()
                            OUTPUT INSERTED.NegocioId
                            WHERE TokenHash = @H AND Revocado = 0 AND Expira > SYSUTCDATETIME()";
        cmd.AddParam("@H", Hash(token));
        var id = await cmd.ReadScalarAsync<int>(ct);
        return id > 0 ? id : null;
    }

    /// <summary>Invalida los enlaces de la empresa (se llama al activar el pago).</summary>
    public async Task RevocarAsync(int negocioId, CancellationToken ct)
    {
        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.EnlacePago SET Revocado = 1 WHERE NegocioId = @N AND Revocado = 0";
        cmd.AddParam("@N", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static readonly CultureInfo Peru = CultureInfo.GetCultureInfo("es-PE");

    /// <summary>Texto del aviso (correo o WhatsApp) según cuántos días faltan para el vencimiento.</summary>
    public static (string Asunto, string Cuerpo) Mensaje(Negocio n, string url, int? dias)
    {
        var saludo = string.IsNullOrWhiteSpace(n.TitularNombre) ? "Hola" : $"Hola {n.TitularNombre!.Trim().Split(' ')[0]}";
        var monto = n.MontoMensual.ToString("0.00", CultureInfo.InvariantCulture);
        var fecha = n.ProximoPago?.ToString("d 'de' MMMM", Peru);
        var prueba = string.Equals(n.EstadoSuscripcion, "PRUEBA", StringComparison.OrdinalIgnoreCase);
        var que = prueba ? "tu prueba gratis de LunaLav" : "tu suscripción a LunaLav";
        var (asunto, estado) = dias switch
        {
            < 0 => ($"Tu suscripción a LunaLav venció", $"{Capitalizar(que)} venció el {fecha}. Renuévala para volver a usar el sistema."),
            0 => ($"Tu suscripción a LunaLav vence hoy", $"{Capitalizar(que)} vence hoy."),
            1 => ($"Tu suscripción a LunaLav vence mañana", $"{Capitalizar(que)} vence mañana ({fecha})."),
            int d => ($"Tu suscripción a LunaLav vence en {d} días", $"{Capitalizar(que)} vence el {fecha}."),
            null => ("Renueva tu suscripción a LunaLav", $"Aquí tienes el enlace para renovar {que}.")
        };
        var cuerpo =
            $"{saludo},\n\n{estado}\n\n" +
            $"Plan: S/ {monto} al mes ({n.Nombre}).\n\n" +
            $"Renueva en un minuto desde tu celular, sin iniciar sesión:\n{url}\n\n" +
            $"Pagas con tarjeta y se renueva sola cada mes; puedes desactivarlo cuando quieras. " +
            $"El enlace es personal y vence en {DiasVigencia} días.\n\n" +
            "¿Dudas? Responde este mensaje o escríbenos a contacto@lunalav.pe.\n\nEquipo LunaLav";
        return (asunto, cuerpo);
    }

    private static string Capitalizar(string s) => char.ToUpper(s[0], Peru) + s[1..];

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
