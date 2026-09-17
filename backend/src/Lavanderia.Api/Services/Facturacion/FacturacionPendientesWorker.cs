using Lavanderia.Api.Repositories;

namespace Lavanderia.Api.Services.Facturacion;

public sealed class FacturacionPendientesWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FacturacionPendientesWorker> _log;

    public FacturacionPendientesWorker(IServiceScopeFactory scopeFactory, ILogger<FacturacionPendientesWorker> log)
    { _scopeFactory = scopeFactory; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SincronizarAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogError(ex, "Fallo el ciclo de sincronizacion de comprobantes"); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task SincronizarAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IFacturacionRepository>();
        var respaldo = scope.ServiceProvider.GetRequiredService<IRespaldoComprobantes>();
        var secretos = scope.ServiceProvider.GetRequiredService<SecretProtector>();
        var providers = scope.ServiceProvider.GetServices<IFacturacionElectronicaProvider>()
            .ToDictionary(x => x.Codigo, StringComparer.OrdinalIgnoreCase);
        foreach (var c in await repo.ListarPendientesGlobalAsync(20, ct))
        {
            if (!providers.TryGetValue(c.Proveedor, out var provider)) continue;
            var config = await repo.ObtenerConfigAsync(c.NegocioId, ct);
            if (config is null) continue;
            string Descifrar(string? value) => string.IsNullOrEmpty(value) ? "" : secretos.Desproteger(value);
            var cred = new CredencialesEmisor(config.Ambiente, config.RucEmisor ?? "", config.RazonSocial ?? "",
                config.SolUsuario ?? "", Descifrar(config.SolClaveCifrada), config.CertificadoPfx ?? [],
                Descifrar(config.CertificadoPasswordCifrada), config.ApiSunatPersonaId ?? "", Descifrar(config.ApiSunatTokenCifrado));
            try
            {
                var r = await provider.ConsultarAsync(c, cred, ct);
                await repo.RegistrarIntentoAsync(c.Id, "CONSULTAR_AUTO", r.Estado, r.Codigo, r.Descripcion, null, ct);
                if (c.EstadoAnulacion == "PENDIENTE" && r.Estado == "ANULADO")
                    await repo.ActualizarAnulacionAsync(c.Id, c.SedeId, "ANULADO", "ANULADO",
                        c.MotivoAnulacion ?? "Anulacion solicitada", r.Codigo, r.Descripcion, r.FechaRespuesta ?? DateTime.Now, ct);
                else if (r.Exitoso)
                {
                    await repo.ActualizarResultadoCompletoAsync(c.Id, c.SedeId, r.Estado, r.Codigo, r.Descripcion,
                        r.XmlFirmado, r.CdrZip, r.HashCpe, c.ExternalId, null, r.FechaRespuesta, ct);
                    // Respaldo local obligatorio (SUNAT): guarda XML+CDR+PDF en disco al quedar aceptado.
                    if (r.Estado == "ACEPTADO")
                        await respaldo.RespaldarAsync(c.Id, c.SedeId, c.NegocioId, ct);
                }
            }
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo sincronizar comprobante {ComprobanteId}", c.Id); }
        }
    }
}
