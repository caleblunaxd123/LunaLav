using Lavanderia.Api.Auth;
using Lavanderia.Api.Infrastructure;
using Lavanderia.Api.Repositories;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Services.Pagos;

/// <summary>Estado del pago automático de una empresa (tabla SuscripcionCulqi).</summary>
public sealed record PagoAutomatico(string Estado, string? SubscriptionId, string? CustomerId, string? CardId,
    string? TarjetaMarca, string? TarjetaUltimos4, string? UltimoError, string? Modo);

public sealed record DatosTitular(string Nombre, string Apellido, string Email, string Telefono, string Direccion, string Ciudad);

/// <summary>Resultado de activar el pago automático: listo, o el banco pide autenticar la tarjeta (3DS).</summary>
public sealed record ResultadoActivacion(bool Activado, bool Requiere3DS, int PagosRegistrados);

/// <summary>
/// Cobro recurrente de la mensualidad con Culqi. Culqi cobra solo cada mes; aquí se crea la
/// suscripción y se CONCILIAN los cobros: cada cargo exitoso se registra una sola vez
/// (ReferenciaExterna = charge_id) y extiende el próximo pago un mes, igual que el registro
/// manual del panel de la plataforma (NegociosController.RegistrarPago).
/// </summary>
public sealed class SuscripcionCulqiService(
    CulqiClient culqi, ISqlConnectionFactory db, INegocioRepository negocios,
    INegocioAccessValidator accessValidator, ILogger<SuscripcionCulqiService> log)
{
    public bool Configurado => culqi.Configurado;
    public string? PublicKey => culqi.PublicKey;
    public string Modo => culqi.Modo;

    public async Task<PagoAutomatico> ObtenerAsync(int negocioId, CancellationToken ct)
    {
        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Estado, SubscriptionId, CustomerId, CardId, TarjetaMarca, TarjetaUltimos4, UltimoError, Modo
                            FROM dbo.SuscripcionCulqi WHERE NegocioId = @N";
        cmd.AddParam("@N", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(r => new PagoAutomatico(
            r.GetString(0), r.GetNullableString("SubscriptionId"), r.GetNullableString("CustomerId"), r.GetNullableString("CardId"),
            r.GetNullableString("TarjetaMarca"), r.GetNullableString("TarjetaUltimos4"), r.GetNullableString("UltimoError"),
            r.GetNullableString("Modo")), ct)
            ?? new PagoAutomatico("SIN_TARJETA", null, null, null, null, null, null, null);
    }

    /// <summary>
    /// Asocia la tarjeta tokenizada y crea la suscripción mensual. Reintentable: si falla a mitad
    /// (p. ej. el banco pide 3DS), el cliente de Culqi ya creado se reutiliza en el siguiente intento.
    /// </summary>
    public async Task<ResultadoActivacion> ActivarAsync(int negocioId, string tokenId, DatosTitular titular,
        Parametros3DS? tds, CancellationToken ct)
    {
        var negocio = await negocios.ObtenerPorIdAsync(negocioId, ct)
            ?? throw new CulqiException("Empresa no encontrada.");
        if (negocio.MontoMensual < 3)
            throw new CulqiException("Tu plan no tiene un monto mensual configurado. Escríbenos para activarlo.");

        var actual = await ObtenerAsync(negocioId, ct);
        if (actual.Estado == "ACTIVA" && actual.SubscriptionId is not null && actual.Modo == culqi.Modo)
            throw new CulqiException("El pago automático ya está activo. Si quieres cambiar de tarjeta, primero desactívalo.");

        // Los ids de Culqi de prueba no existen en producción (y viceversa): si cambió el modo, se empieza de cero.
        var customerId = actual.Modo == culqi.Modo ? actual.CustomerId : null;
        customerId ??= await culqi.CrearClienteAsync(titular.Nombre, titular.Apellido, titular.Email, titular.Telefono,
            titular.Direccion, titular.Ciudad, negocioId, ct);
        await GuardarAsync(negocioId, "SIN_TARJETA", customerId: customerId, ct: ct);

        var tarjeta = await culqi.CrearTarjetaAsync(customerId, tokenId, tds, ct);
        if (tarjeta.Requiere3DS) return new ResultadoActivacion(false, true, 0);

        var montoCentimos = (int)Math.Round(negocio.MontoMensual * 100m);
        var planId = await PlanMensualAsync(montoCentimos, ct);
        var subscriptionId = await culqi.CrearSuscripcionAsync(tarjeta.Tarjeta!.Id, planId, negocioId, ct);
        await GuardarAsync(negocioId, "ACTIVA", customerId, tarjeta.Tarjeta.Id, subscriptionId, planId,
            tarjeta.Tarjeta.Marca, tarjeta.Tarjeta.Ultimos4, error: null, ct: ct);

        // El primer cargo puede ya estar hecho: se concilia de inmediato (el webhook lo repetiría sin duplicar).
        var registrados = await ConciliarAsync(negocioId, subscriptionId, ct);
        return new ResultadoActivacion(true, false, registrados);
    }

    /// <summary>Desactiva el cobro automático. La cobertura ya pagada se mantiene hasta su fecha.</summary>
    public async Task CancelarAsync(int negocioId, CancellationToken ct)
    {
        var actual = await ObtenerAsync(negocioId, ct);
        if (actual.SubscriptionId is null || actual.Estado != "ACTIVA") return;
        await culqi.CancelarSuscripcionAsync(actual.SubscriptionId, ct);
        await GuardarAsync(negocioId, "CANCELADA", error: null, ct: ct);
    }

    /// <summary>
    /// Empresa y suscripción a las que pertenece un evento de Culqi, a partir de los ids que trae
    /// (sxn_ de la suscripción; o crd_/cus_ de la tarjeta o el cliente, en los eventos de cargo).
    /// </summary>
    public async Task<(int NegocioId, string SubscriptionId)?> BuscarPorIdsCulqiAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return null;
        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var nombres = ids.Take(20).Select((id, i) => { cmd.AddParam($"@I{i}", id); return $"@I{i}"; }).ToList();
        var lista = string.Join(",", nombres);
        cmd.CommandText = $@"SELECT TOP 1 NegocioId, SubscriptionId FROM dbo.SuscripcionCulqi
                             WHERE SubscriptionId IS NOT NULL
                               AND (SubscriptionId IN ({lista}) OR CardId IN ({lista}) OR CustomerId IN ({lista}))
                             ORDER BY FechaActualizacion DESC";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (r.GetInt32(0), r.GetString(1)) : null;
    }

    /// <summary>
    /// Trae la suscripción desde Culqi (fuente de verdad) y registra cada cargo exitoso que aún no
    /// esté en el historial. Devuelve cuántos pagos nuevos registró.
    /// </summary>
    public async Task<int> ConciliarAsync(int negocioId, string subscriptionId, CancellationToken ct)
    {
        var suscripcion = await culqi.ObtenerSuscripcionAsync(subscriptionId, ct);
        var nuevos = 0;
        foreach (var cargo in suscripcion.Cargos.Where(c => c.Exitoso).OrderBy(c => c.Fecha))
            if (await RegistrarCargoAsync(negocioId, cargo, ct)) nuevos++;

        var ultimoFallido = suscripcion.Cargos.Where(c => !c.Exitoso).OrderByDescending(c => c.Fecha).FirstOrDefault();
        var ultimoExitoso = suscripcion.Cargos.Where(c => c.Exitoso).OrderByDescending(c => c.Fecha).FirstOrDefault();
        var falloReciente = ultimoFallido is not null && (ultimoExitoso is null || ultimoFallido.Fecha > ultimoExitoso.Fecha);

        if (suscripcion.Cancelada)
            await GuardarAsync(negocioId, "CANCELADA", error: falloReciente ? MensajeFallo(ultimoFallido!) : null, ct: ct);
        else if (falloReciente)
            await GuardarAsync(negocioId, "FALLIDA", error: MensajeFallo(ultimoFallido!), ct: ct);
        else if (nuevos > 0)
            await GuardarAsync(negocioId, "ACTIVA", error: null, ct: ct);

        if (nuevos > 0) accessValidator.Invalidar(negocioId);
        return nuevos;
    }

    private static string MensajeFallo(CulqiCargo c) =>
        string.IsNullOrWhiteSpace(c.Error) ? "El último cobro mensual fue rechazado por el banco." : c.Error!;

    /// <summary>Registra un cargo exitoso una sola vez y extiende la cobertura un mes.</summary>
    private async Task<bool> RegistrarCargoAsync(int negocioId, CulqiCargo cargo, CancellationToken ct)
    {
        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            // UPDLOCK sobre el negocio: dos webhooks simultáneos del mismo cargo no extienden dos veces.
            cmd.CommandText = @"
SET XACT_ABORT ON;
DECLARE @Proximo DATE = (SELECT ProximoPago FROM dbo.Negocio WITH (UPDLOCK, HOLDLOCK) WHERE Id = @N);
IF EXISTS (SELECT 1 FROM dbo.PagoSuscripcion WHERE ReferenciaExterna = @Ref) BEGIN SELECT 0; RETURN; END
DECLARE @Hoy DATE = CAST(GETDATE() AS date);
-- El período arranca donde termina la cobertura vigente (pagar en la prueba no hace perder días).
DECLARE @Desde DATE = CASE WHEN @Proximo IS NOT NULL AND @Proximo > @Hoy THEN @Proximo ELSE @Hoy END;
DECLARE @Hasta DATE = DATEADD(month, 1, @Desde);
INSERT dbo.PagoSuscripcion (NegocioId, Fecha, Monto, Metodo, PeriodoDesde, PeriodoHasta, Nota, ReferenciaExterna)
VALUES (@N, @Hoy, @Monto, N'TARJETA', @Desde, @Hasta, N'Cobro automático con tarjeta (Culqi)', @Ref);
UPDATE dbo.Negocio SET EstadoSuscripcion = 'ACTIVA', ProximoPago = @Hasta WHERE Id = @N;
SELECT 1;";
            cmd.AddParam("@N", negocioId);
            cmd.AddParam("@Ref", cargo.ChargeId);
            cmd.AddParam("@Monto", cargo.MontoSoles);
            var registrado = await cmd.ReadScalarAsync<int>(ct) == 1;
            await tx.CommitAsync(ct);
            if (registrado) log.LogInformation("Cobro Culqi {Charge} registrado para el negocio {Negocio}.", cargo.ChargeId, negocioId);
            return registrado;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await tx.RollbackAsync(ct); // otro proceso lo registró primero
            return false;
        }
    }

    private async Task<string> PlanMensualAsync(int montoCentimos, CancellationToken ct)
    {
        await using (var conn = db.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CulqiPlanId FROM dbo.CulqiPlan WHERE MontoCentimos = @M AND Moneda = N'PEN' AND Modo = @Modo";
            cmd.AddParam("@M", montoCentimos);
            cmd.AddParam("@Modo", culqi.Modo);
            var existente = await cmd.ReadScalarAsync<string>(ct);
            if (!string.IsNullOrWhiteSpace(existente)) return existente;
        }

        var planId = await culqi.CrearPlanMensualAsync(montoCentimos, ct);
        await using (var conn = db.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"IF NOT EXISTS (SELECT 1 FROM dbo.CulqiPlan WHERE MontoCentimos = @M AND Moneda = N'PEN' AND Modo = @Modo)
                                INSERT dbo.CulqiPlan (MontoCentimos, Moneda, CulqiPlanId, Modo) VALUES (@M, N'PEN', @P, @Modo);
                                SELECT CulqiPlanId FROM dbo.CulqiPlan WHERE MontoCentimos = @M AND Moneda = N'PEN' AND Modo = @Modo;";
            cmd.AddParam("@M", montoCentimos);
            cmd.AddParam("@P", planId);
            cmd.AddParam("@Modo", culqi.Modo);
            return await cmd.ReadScalarAsync<string>(ct) ?? planId;
        }
    }

    private async Task GuardarAsync(int negocioId, string estado, string? customerId = null, string? cardId = null,
        string? subscriptionId = null, string? planId = null, string? marca = null, string? ultimos4 = null,
        string? error = null, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Los ids solo se sobrescriben cuando llegan; el estado y el error siempre se actualizan.
        cmd.CommandText = @"
MERGE dbo.SuscripcionCulqi AS t
USING (SELECT @N AS NegocioId) AS s ON t.NegocioId = s.NegocioId
WHEN MATCHED THEN UPDATE SET
    Estado = @Estado,
    CustomerId = COALESCE(@Customer, t.CustomerId),
    CardId = COALESCE(@Card, t.CardId),
    SubscriptionId = COALESCE(@Sub, t.SubscriptionId),
    CulqiPlanId = COALESCE(@Plan, t.CulqiPlanId),
    TarjetaMarca = COALESCE(@Marca, t.TarjetaMarca),
    TarjetaUltimos4 = COALESCE(@Ult, t.TarjetaUltimos4),
    UltimoError = @Error,
    Modo = @Modo,
    FechaActualizacion = SYSDATETIME()
WHEN NOT MATCHED THEN INSERT (NegocioId, Estado, CustomerId, CardId, SubscriptionId, CulqiPlanId, TarjetaMarca, TarjetaUltimos4, UltimoError, Modo)
    VALUES (@N, @Estado, @Customer, @Card, @Sub, @Plan, @Marca, @Ult, @Error, @Modo);";
        cmd.AddParam("@N", negocioId);
        cmd.AddParam("@Estado", estado);
        cmd.AddParam("@Customer", (object?)customerId ?? DBNull.Value);
        cmd.AddParam("@Card", (object?)cardId ?? DBNull.Value);
        cmd.AddParam("@Sub", (object?)subscriptionId ?? DBNull.Value);
        cmd.AddParam("@Plan", (object?)planId ?? DBNull.Value);
        cmd.AddParam("@Marca", (object?)marca ?? DBNull.Value);
        cmd.AddParam("@Ult", (object?)ultimos4 ?? DBNull.Value);
        cmd.AddParam("@Error", (object?)error ?? DBNull.Value);
        cmd.AddParam("@Modo", culqi.Modo);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
