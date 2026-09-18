using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
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

    public async Task<int> SyncInboxAsync(CancellationToken ct)
    {
        var token = await AccessTokenAsync(ct); using var list = await GoogleGetAsync("https://gmail.googleapis.com/gmail/v1/users/me/messages?labelIds=INBOX&maxResults=25", token, ct);
        var raw = await list.Content.ReadAsStringAsync(ct); if (!list.IsSuccessStatusCode) throw new InvalidOperationException("No se pudo leer Gmail. Reconecta la cuenta si el permiso fue revocado.");
        using var doc = JsonDocument.Parse(raw); if (!doc.RootElement.TryGetProperty("messages", out var items)) return 0; var inserted=0;
        foreach(var item in items.EnumerateArray()) { var id=item.GetProperty("id").GetString(); if(string.IsNullOrEmpty(id)) continue;
            using var one=await GoogleGetAsync($"https://gmail.googleapis.com/gmail/v1/users/me/messages/{id}?format=metadata&metadataHeaders=From&metadataHeaders=To&metadataHeaders=Subject",token,ct);
            if(!one.IsSuccessStatusCode) continue; using var msg=JsonDocument.Parse(await one.Content.ReadAsStringAsync(ct)); var root=msg.RootElement;
            var thread=root.GetProperty("threadId").GetString()??""; var labels=root.TryGetProperty("labelIds",out var ls)?ls.EnumerateArray().Select(x=>x.GetString()).ToHashSet():[];
            var from=Header(root,"From"); var to=Header(root,"To"); var subject=Header(root,"Subject")??"(sin asunto)"; var snippet=root.TryGetProperty("snippet",out var sn)?sn.GetString():null;
            var at=root.TryGetProperty("internalDate",out var dt)&&long.TryParse(dt.GetString(),out var ms)?DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime:DateTime.UtcNow;
            var unread=labels.Contains("UNREAD"); var direction=labels.Contains("SENT")?"OUTBOUND":"INBOUND";
            await SaveMessageAsync(id,thread,direction,from,to,subject,snippet,at,unread,ct); inserted++;
        }
        await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE communication.Mailbox SET LastSyncAt=SYSUTCDATETIME() WHERE Provider=N'GMAIL'";await q.ExecuteNonQueryAsync(ct); return inserted;
    }
    public async Task<IReadOnlyList<object>> InboxAsync(CancellationToken ct)
    { await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT TOP 100 t.GmailThreadId,t.ProspectId,p.NombreComercial,t.ContactEmail,t.ContactName,t.Subject,t.LastSnippet,t.LastMessageAt,t.IsUnread FROM communication.EmailThread t LEFT JOIN marketing.Prospect p ON p.Id=t.ProspectId ORDER BY t.LastMessageAt DESC";await using var r=await q.ExecuteReaderAsync(ct);var x=new List<object>();while(await r.ReadAsync(ct))x.Add(new{threadId=r.GetString(0),prospectId=r.IsDBNull(1)?(long?)null:r.GetInt64(1),prospect=r.IsDBNull(2)?null:r.GetString(2),email=r.IsDBNull(3)?null:r.GetString(3),name=r.IsDBNull(4)?null:r.GetString(4),subject=r.GetString(5),snippet=r.IsDBNull(6)?null:r.GetString(6),date=r.GetDateTime(7),unread=r.GetBoolean(8)});return x; }
    private async Task<string> AccessTokenAsync(CancellationToken ct)
    { await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT TOP 1 EncryptedCredentials FROM communication.Mailbox WHERE Provider=N'GMAIL' AND Status=N'CONNECTED' ORDER BY Id DESC";var enc=await q.ExecuteScalarAsync(ct) as string; if(string.IsNullOrWhiteSpace(enc)) throw new InvalidOperationException("No hay una cuenta Gmail conectada.");using var stored=JsonDocument.Parse(secrets.Desproteger(enc));var refresh=stored.RootElement.GetProperty("refreshToken").GetString();using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"client_id",ClientId!},{"client_secret",ClientSecret!},{"refresh_token",refresh!},{"grant_type","refresh_token"}});using var response=await http.PostAsync("https://oauth2.googleapis.com/token",form,ct);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Google rechazó el acceso. Reconecta Gmail.");using var token=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));return token.RootElement.GetProperty("access_token").GetString()!; }
    private async Task<HttpResponseMessage> GoogleGetAsync(string url,string token,CancellationToken ct){var req=new HttpRequestMessage(HttpMethod.Get,url);req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);return await http.SendAsync(req,ct);}
    private static string? Header(JsonElement r,string name){if(!r.TryGetProperty("payload",out var p)||!p.TryGetProperty("headers",out var hs))return null;foreach(var h in hs.EnumerateArray())if(string.Equals(h.GetProperty("name").GetString(),name,StringComparison.OrdinalIgnoreCase))return h.GetProperty("value").GetString();return null;}
    private static string? EmailOnly(string? value){if(string.IsNullOrWhiteSpace(value))return value;var left=value.LastIndexOf('<');var right=value.LastIndexOf('>');return left>=0&&right>left?value[(left+1)..right].Trim():value.Trim();}
    private async Task SaveMessageAsync(string id,string thread,string direction,string? from,string? to,string subject,string? snippet,DateTime at,bool unread,CancellationToken ct){var contact=EmailOnly(direction=="INBOUND"?from:to);await using var c=db.Create();await c.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText=@"IF NOT EXISTS(SELECT 1 FROM communication.EmailMessage WHERE GmailMessageId=@id) BEGIN INSERT communication.EmailMessage(GmailMessageId,GmailThreadId,Direction,Sender,Recipient,Subject,Snippet,ReceivedAt,IsUnread) VALUES(@id,@t,@d,@f,@to,@s,@sn,@at,@u); MERGE communication.EmailThread AS x USING(SELECT @t Id) a ON x.GmailThreadId=a.Id WHEN MATCHED THEN UPDATE SET Subject=@s,LastSnippet=@sn,LastMessageAt=@at,IsUnread=@u,ContactEmail=COALESCE(x.ContactEmail,@contact),ProspectId=COALESCE(x.ProspectId,(SELECT TOP 1 Id FROM marketing.Prospect WHERE Email=@contact AND Activo=1)) WHEN NOT MATCHED THEN INSERT(GmailThreadId,ProspectId,ContactEmail,ContactName,Subject,LastSnippet,LastMessageAt,IsUnread) VALUES(@t,(SELECT TOP 1 Id FROM marketing.Prospect WHERE Email=@contact AND Activo=1),@contact,@contact,@s,@sn,@at,@u); END";q.AddParam("@id",id);q.AddParam("@t",thread);q.AddParam("@d",direction);q.AddParam("@f",from);q.AddParam("@to",to);q.AddParam("@contact",contact);q.AddParam("@s",subject);q.AddParam("@sn",snippet);q.AddParam("@at",at);q.AddParam("@u",unread);await q.ExecuteNonQueryAsync(ct);}
}
