namespace Lavanderia.Api.Services;

/// <summary>
/// Mantiene actualizado el Inbox de Marketing sin enviar mensajes. La conexión OAuth
/// puede no existir durante el arranque; en ese caso espera silenciosamente al siguiente ciclo.
/// </summary>
public sealed class GmailInboxSyncWorker(GmailOAuthService gmail, ILogger<GmailInboxSyncWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var total = await gmail.SyncInboxAsync(stoppingToken);
                log.LogInformation("Inbox de Marketing sincronizado: {Total} mensajes revisados", total);
            }
            catch (InvalidOperationException ex)
            {
                log.LogDebug("Sincronización de Gmail pendiente: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "No se pudo sincronizar automáticamente el Inbox de Marketing");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
