using Lavanderia.Api.Infrastructure;
using Lavanderia.Api.Repositories;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Services.Pagos;

/// <summary>
/// Envía por correo el aviso de vencimiento con el enlace de pago: 5 días antes, 1 día antes,
/// el día del vencimiento y una vez vencida. Se omite a quien ya tiene el cobro automático activo.
/// Cada etapa se "reserva" con un INSERT único antes de enviar: aunque haya varias instancias
/// de la API corriendo, cada empresa recibe cada aviso una sola vez.
/// </summary>
public sealed class RecordatorioPagoWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<RecordatorioPagoWorker> log)
    : BackgroundService
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Lima");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("RecordatorioPago:Habilitado", true)) return;
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Solo en horario razonable de Lima (8 a. m. a 8 p. m.).
                var hora = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Lima).Hour;
                if (hora is >= 8 and < 20) await EjecutarAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Falló el envío de recordatorios de pago.");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    /// <summary>Etapa del aviso según los días que faltan (null = no toca aviso hoy).</summary>
    public static string? Etapa(int dias) => dias switch
    {
        >= 2 and <= 5 => "D5",
        1 => "D1",
        0 => "D0",
        >= -7 and <= -1 => "VENCIDA",
        _ => null
    };

    public async Task<int> EjecutarAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();
        var negocios = scope.ServiceProvider.GetRequiredService<INegocioRepository>();
        var enlaces = scope.ServiceProvider.GetRequiredService<EnlacePagoService>();
        var gmail = scope.ServiceProvider.GetRequiredService<GmailOAuthService>();

        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, Lima));
        var candidatos = new List<(int Id, DateOnly Vence)>();
        await using (var conn = db.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT n.Id, n.ProximoPago FROM dbo.Negocio n
LEFT JOIN dbo.SuscripcionCulqi s ON s.NegocioId = n.Id
WHERE n.Activo = 1 AND n.ProximoPago IS NOT NULL
  AND n.ProximoPago BETWEEN DATEADD(day, -7, @Hoy) AND DATEADD(day, 5, @Hoy)
  AND n.Slug NOT IN ('demo', 'plataforma-interna')
  AND NULLIF(LTRIM(n.TitularEmail), '') IS NOT NULL
  AND n.EstadoSuscripcion <> 'SUSPENDIDA'
  AND ISNULL(s.Estado, 'SIN_TARJETA') <> 'ACTIVA'";
            cmd.AddParam("@Hoy", hoy.ToDateTime(TimeOnly.MinValue));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) candidatos.Add((r.GetInt32(0), DateOnly.FromDateTime(r.GetDateTime(1))));
        }

        var enviados = 0;
        foreach (var (id, vence) in candidatos)
        {
            var dias = vence.DayNumber - hoy.DayNumber;
            if (Etapa(dias) is not { } etapa) continue;
            if (!await ReservarAsync(db, id, vence, etapa, ct)) continue; // ya enviado (u otra instancia lo tomó)

            var n = await negocios.ObtenerPorIdAsync(id, ct);
            if (n is null) continue;
            try
            {
                var url = await enlaces.CrearAsync(id, "CORREO", ct);
                var (asunto, cuerpo) = EnlacePagoService.Mensaje(n, url, dias);
                await gmail.SendAsync(n.TitularEmail!.Trim(), asunto, cuerpo, null, null, null, null, ct, registrarEnBandeja: false);
                await MarcarAsync(db, id, vence, etapa, true, null, ct);
                enviados++;
                log.LogInformation("Recordatorio de pago {Etapa} enviado a la empresa {Negocio}.", etapa, id);
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
            {
                // Se deja registrado el fallo; no se reintenta en bucle (el siguiente aviso es otra etapa).
                await MarcarAsync(db, id, vence, etapa, false, e.Message.Length > 290 ? e.Message[..290] : e.Message, ct);
                log.LogWarning("No se pudo enviar el recordatorio {Etapa} a la empresa {Negocio}: {Error}", etapa, id, e.Message);
            }
        }
        return enviados;
    }

    private static async Task<bool> ReservarAsync(ISqlConnectionFactory db, int negocioId, DateOnly vence, string etapa, CancellationToken ct)
    {
        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT dbo.RecordatorioPago (NegocioId, Vencimiento, Etapa, Enviado) VALUES (@N, @V, @E, 0)";
        cmd.AddParam("@N", negocioId);
        cmd.AddParam("@V", vence.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("@E", etapa);
        try { await cmd.ExecuteNonQueryAsync(ct); return true; }
        catch (SqlException e) when (e.Number is 2601 or 2627) { return false; }
    }

    private static async Task MarcarAsync(ISqlConnectionFactory db, int negocioId, DateOnly vence, string etapa, bool enviado, string? detalle, CancellationToken ct)
    {
        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.RecordatorioPago SET Enviado = @Ok, Detalle = @D WHERE NegocioId = @N AND Vencimiento = @V AND Etapa = @E";
        cmd.AddParam("@Ok", enviado);
        cmd.AddParam("@D", (object?)detalle ?? DBNull.Value);
        cmd.AddParam("@N", negocioId);
        cmd.AddParam("@V", vence.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("@E", etapa);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
