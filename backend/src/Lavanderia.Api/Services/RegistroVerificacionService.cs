using System.Security.Cryptography;
using System.Text;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Services;

/// <summary>Resultado de comprobar un código: válido (con su id para marcarlo usado) o el motivo del rechazo.</summary>
public sealed record VerificacionCodigo(int? Id, string? Error);

/// <summary>
/// Código de 6 dígitos enviado por correo antes de crear una lavandería: confirma que el correo
/// es del titular y frena las altas automatizadas. Solo se guarda el hash; vence a los
/// 10 minutos, admite 5 intentos, un envío por minuto y 6 por día por correo.
/// </summary>
public sealed class RegistroVerificacionService(ISqlConnectionFactory db, GmailOAuthService gmail,
    IConfiguration config, ILogger<RegistroVerificacionService> log)
{
    public const int MinutosVigencia = 10;
    public const int MaxIntentos = 5;
    private const int EnviosPorDia = 6;

    /// <summary>Se puede desactivar (Registro:RequiereVerificacion=false) si el correo de salida no está disponible.</summary>
    public bool Requerida => config.GetValue("Registro:RequiereVerificacion", true);

    public static string Normalizar(string email) => email.Trim().ToLowerInvariant();

    private static string Hash(string email, string codigo)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{email}|{codigo}")));

    /// <summary>Genera y envía el código. Devuelve un mensaje de error para el usuario, o null si se envió.</summary>
    public async Task<string?> EnviarAsync(string emailEntrada, string? ip, CancellationToken ct)
    {
        var email = Normalizar(emailEntrada);
        await using (var conn = db.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT
                (SELECT COUNT(*) FROM dbo.RegistroVerificacion WHERE Email = @E AND FechaCreacion > DATEADD(minute, -1, SYSUTCDATETIME())),
                (SELECT COUNT(*) FROM dbo.RegistroVerificacion WHERE Email = @E AND FechaCreacion > DATEADD(day, -1, SYSUTCDATETIME())),
                (SELECT COUNT(*) FROM dbo.Negocio WHERE LOWER(TitularEmail) = @E)";
            cmd.AddParam("@E", email);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            if (r.GetInt32(2) > 0) return "Ya existe una lavandería registrada con este correo. Inicia sesión o usa otro correo.";
            if (r.GetInt32(0) > 0) return "Ya te enviamos un código. Espera un minuto antes de pedir otro.";
            if (r.GetInt32(1) >= EnviosPorDia) return "Pediste demasiados códigos hoy. Inténtalo mañana o escríbenos a contacto@lunalav.pe.";
        }

        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        try
        {
            await gmail.SendAsync(email, $"Tu código de LunaLav: {codigo}",
                $"Hola,\n\nTu código para crear tu lavandería en LunaLav es:\n\n    {codigo}\n\n" +
                $"Vence en {MinutosVigencia} minutos. Si no fuiste tú, ignora este correo.\n\nEquipo LunaLav",
                null, null, null, null, ct, registrarEnBandeja: false);
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
        {
            log.LogError("No se pudo enviar el código de registro: {Error}", e.Message);
            return "No pudimos enviar el código ahora. Inténtalo en unos minutos.";
        }

        await using (var conn = db.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT dbo.RegistroVerificacion (Email, CodigoHash, Expira, IpOrigen)
                                VALUES (@E, @H, DATEADD(minute, @M, SYSUTCDATETIME()), @Ip)";
            cmd.AddParam("@E", email);
            cmd.AddParam("@H", Hash(email, codigo));
            cmd.AddParam("@M", MinutosVigencia);
            cmd.AddParam("@Ip", (object?)ip ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return null;
    }

    /// <summary>Comprueba el último código vigente del correo; un fallo consume un intento.</summary>
    public async Task<VerificacionCodigo> VerificarAsync(string emailEntrada, string? codigoEntrada, CancellationToken ct)
    {
        var email = Normalizar(emailEntrada);
        var codigo = new string((codigoEntrada ?? "").Where(char.IsDigit).ToArray());
        if (codigo.Length != 6) return new(null, "Ingresa el código de 6 dígitos que enviamos a tu correo.");

        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT TOP 1 Id, CodigoHash, Intentos FROM dbo.RegistroVerificacion
                            WHERE Email = @E AND Usado = 0 AND Expira > SYSUTCDATETIME()
                            ORDER BY FechaCreacion DESC";
        cmd.AddParam("@E", email);
        int id, intentos; string hash;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return new(null, "El código venció o ya se usó. Pide uno nuevo.");
            (id, hash, intentos) = (r.GetInt32(0), r.GetString(1), r.GetInt32(2));
        }
        if (intentos >= MaxIntentos) return new(null, "Demasiados intentos con este código. Pide uno nuevo.");

        if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(Hash(email, codigo))))
            return new(id, null);

        await using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE dbo.RegistroVerificacion SET Intentos = Intentos + 1 WHERE Id = @Id";
        upd.AddParam("@Id", id);
        await upd.ExecuteNonQueryAsync(ct);
        var restantes = MaxIntentos - intentos - 1;
        return new(null, restantes > 0 ? $"Código incorrecto. Te quedan {restantes} intentos." : "Código incorrecto. Pide uno nuevo.");
    }
}
