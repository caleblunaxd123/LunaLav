using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Services.Pagos;

/// <summary>
/// Red de seguridad de los cobros con tarjeta: cada 3 horas consulta a Culqi las suscripciones
/// con cobro automático y registra los cargos que falten. Si un webhook se pierde o se rechaza,
/// el pago igual aparece en el historial y extiende el vencimiento (el registro es idempotente,
/// así que correr en varias instancias de la API no duplica pagos).
/// </summary>
public sealed class ConciliacionCulqiWorker(IServiceScopeFactory scopes, ILogger<ConciliacionCulqiWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await EjecutarAsync(stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Falló la conciliación de cobros con Culqi."); }
            await Task.Delay(TimeSpan.FromHours(3), stoppingToken);
        }
    }

    private async Task EjecutarAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var pagos = scope.ServiceProvider.GetRequiredService<SuscripcionCulqiService>();
        if (!pagos.Configurado) return;
        var db = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();

        var suscripciones = new List<(int NegocioId, string SubscriptionId)>();
        await using (var conn = db.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT NegocioId, SubscriptionId FROM dbo.SuscripcionCulqi
                                WHERE SubscriptionId IS NOT NULL AND Estado IN ('ACTIVA', 'FALLIDA') AND Modo = @Modo";
            cmd.AddParam("@Modo", pagos.Modo);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) suscripciones.Add((r.GetInt32(0), r.GetString(1)));
        }

        foreach (var (negocioId, subscriptionId) in suscripciones)
        {
            try
            {
                var nuevos = await pagos.ConciliarAsync(negocioId, subscriptionId, ct);
                if (nuevos > 0) log.LogInformation("Conciliación Culqi: {Nuevos} pago(s) registrado(s) para el negocio {Negocio}.", nuevos, negocioId);
            }
            catch (CulqiException e) { log.LogWarning("Conciliación Culqi del negocio {Negocio}: {Error}", negocioId, e.Message); }
        }
    }
}
