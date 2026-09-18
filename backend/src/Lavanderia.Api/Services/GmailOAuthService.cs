using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lavanderia.Api.Infrastructure;
using Lavanderia.Api.Services.Facturacion;

namespace Lavanderia.Api.Services;

public sealed class GmailOAuthService(HttpClient http, IConfiguration config, ISqlConnectionFactory db, SecretProtector secrets)
{
    private string? ClientId => config["Gmail:ClientId"];
    private string? ClientSecret => config["Gmail:ClientSecret"];
    private string CallbackUrl => config["Gmail:RedirectUri"] ?? "https://marketing.lunalav.pe/api/marketing/google/gmail/callback";
    public bool Configured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public async Task<string> StartAsync(string requestedBy, CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException("Gmail aún no está configurado. Agrega Gmail:ClientId y Gmail:ClientSecret como secretos locales.");
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var c = db.Create(); await c.OpenAsync(ct); await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO communication.OAuthState(Provider,State,RequestedBy,ExpiresAt) VALUES(N'GMAIL',@s,@u,DATEADD(MINUTE,10,SYSUTCDATETIME()))";
        cmd.AddParam("@s", state); cmd.AddParam("@u", requestedBy); await cmd.ExecuteNonQueryAsync(ct);
        var scope = Uri.EscapeDataString("https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.send");
        return $"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(ClientId!)}&redirect_uri={Uri.EscapeDataString(CallbackUrl)}&response_type=code&scope={scope}&access_type=offline&prompt=consent&state={state}";
    }

    public async Task CompleteAsync(string code, string state, CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException("La configuración de Gmail no está completa.");
        await using var c = db.Create(); await c.OpenAsync(ct); await using var check = c.CreateCommand();
        check.CommandText = "UPDATE communication.OAuthState SET UsedAt=SYSUTCDATETIME() WHERE Provider=N'GMAIL' AND State=@s AND UsedAt IS NULL AND ExpiresAt>SYSUTCDATETIME()";
        check.AddParam("@s", state); if (await check.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("La autorización expiró o no es válida. Inicia la conexión otra vez.");
        using var form = new FormUrlEncodedContent(new Dictionary<string,string> { ["code"] = code, ["client_id"] = ClientId!, ["client_secret"] = ClientSecret!, ["redirect_uri"] = CallbackUrl, ["grant_type"] = "authorization_code" });
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        var raw = await response.Content.ReadAsStringAsync(ct); if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Google no aceptó la autorización de Gmail.");
        using var doc = JsonDocument.Parse(raw); var root = doc.RootElement;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrWhiteSpace(refresh)) throw new InvalidOperationException("Google no entregó acceso permanente. Vuelve a conectar y acepta el permiso solicitado.");
        var creds = JsonSerializer.Serialize(new { refreshToken = refresh, accessToken = root.GetProperty("access_token").GetString(), connectedAt = DateTime.UtcNow });
        await using var save = c.CreateCommand();
        save.CommandText = @"MERGE communication.Mailbox AS target USING (SELECT N'contacto@lunalav.pe' Address) AS source ON target.Address=source.Address
WHEN MATCHED THEN UPDATE SET Provider=N'GMAIL',Status=N'CONNECTED',DisplayName=N'LunaLav Ventas',EncryptedCredentials=@cred,LastSyncAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT(Address,Provider,Status,DisplayName,EncryptedCredentials,LastSyncAt) VALUES(N'contacto@lunalav.pe',N'GMAIL',N'CONNECTED',N'LunaLav Ventas',@cred,SYSUTCDATETIME());";
        save.AddParam("@cred", secrets.Proteger(creds)); await save.ExecuteNonQueryAsync(ct);
    }

    public async Task<object> StatusAsync(CancellationToken ct)
    {
        await using var c = db.Create(); await c.OpenAsync(ct); await using var q = c.CreateCommand();
        q.CommandText = "SELECT TOP 1 Address,Status,LastSyncAt FROM communication.Mailbox WHERE Provider=N'GMAIL' ORDER BY Id DESC";
        await using var r = await q.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new { configured = Configured, connected = r.GetString(1) == "CONNECTED", address = r.GetString(0), lastSyncAt = r.IsDBNull(2) ? (DateTime?)null : r.GetDateTime(2) } : new { configured = Configured, connected = false, address = "contacto@lunalav.pe", lastSyncAt = (DateTime?)null };
    }
}
