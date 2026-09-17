using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public interface IInsumoRepository
{
    Task<List<Insumo>> ListarTodosAsync(int sedeId, CancellationToken ct = default);
    Task<List<Insumo>> ListarBajoStockAsync(int sedeId, CancellationToken ct = default);
    Task<Insumo?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default);
    Task<bool> ExisteNombreAsync(string nombre, int sedeId, int? excluirId = null, CancellationToken ct = default);
    Task<int> CrearAsync(Insumo i, CancellationToken ct = default);
    Task ActualizarAsync(Insumo i, int sedeId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activo, int sedeId, CancellationToken ct = default);
    Task<int> RegistrarMovimientoAsync(MovimientoInsumo m, string? metodoPagoParaGasto, int? tipoGastoIdParaGasto, CancellationToken ct = default);
    /// <summary>Corrige solo la fecha y la nota de un movimiento (no afecta el stock). Si tenía un
    /// gasto de caja vinculado, también le actualiza la fecha para mantener el cuadre consistente.
    /// Devuelve false si el movimiento no existe en la sede.</summary>
    Task<bool> EditarMovimientoAsync(int movimientoId, DateTime fecha, string? descripcion, int sedeId, CancellationToken ct = default);
    /// <summary>Elimina un movimiento y revierte su efecto en el stock (y borra el gasto de caja
    /// vinculado si lo tenía). Lanza InvalidOperationException si revertir dejaría el stock negativo.</summary>
    Task<bool> EliminarMovimientoAsync(int movimientoId, int sedeId, CancellationToken ct = default);
    Task<List<MovimientoInsumo>> ListarMovimientosAsync(int? insumoId, DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
    /// <summary>Suma de cantidades consumidas por día (para las barras de tendencia).</summary>
    Task<Dictionary<DateTime, int>> ContarConsumoPorDiaAsync(DateTime desde, int sedeId, CancellationToken ct = default);
}

public class InsumoRepository : IInsumoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public InsumoRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static Insumo Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        UnidadMedida = r.GetString(r.GetOrdinal("UnidadMedida")),
        Clase = r.GetString(r.GetOrdinal("Clase")),
        ContenidoValor = r.GetNullableDecimal("ContenidoValor"),
        ContenidoUnidad = r.GetNullableString("ContenidoUnidad"),
        StockActual = r.GetDecimal(r.GetOrdinal("StockActual")),
        StockMinimo = r.GetDecimal(r.GetOrdinal("StockMinimo")),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        UltimaCompra = r.GetNullableDateTime("UltimaCompra"),
        FechaIngreso = r.GetNullableDateTime("FechaIngreso") is DateTime fi ? DateOnly.FromDateTime(fi) : null,
        FechaVencimiento = r.GetNullableDateTime("FechaVencimiento") is DateTime fv ? DateOnly.FromDateTime(fv) : null,
        EnUso = r.GetBoolean(r.GetOrdinal("EnUso"))
    };

    private const string Select = @"SELECT Id, Nombre, UnidadMedida, Clase, ContenidoValor, ContenidoUnidad, StockActual, StockMinimo, Activo, FechaIngreso, FechaVencimiento,
        (SELECT MAX(m.Fecha) FROM dbo.MovimientoInsumo m WHERE m.InsumoId = dbo.Insumo.Id AND m.Tipo = 'COMPRA') AS UltimaCompra,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.MovimientoInsumo mu WHERE mu.InsumoId = dbo.Insumo.Id) THEN 1 ELSE 0 END AS BIT) AS EnUso
        FROM dbo.Insumo";

    public async Task<List<Insumo>> ListarTodosAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE SedeId = @SedeId ORDER BY Activo DESC, Nombre";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<List<Insumo>> ListarBajoStockAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE Activo = 1 AND StockActual <= StockMinimo AND SedeId = @SedeId ORDER BY Nombre";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<Insumo?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<bool> ExisteNombreAsync(string nombre, int sedeId, int? excluirId = null, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM dbo.Insumo
            WHERE SedeId = @SedeId
              AND LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI
              AND (@ExcluirId IS NULL OR Id <> @ExcluirId)";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Nombre", nombre);
        cmd.AddParam("@ExcluirId", excluirId);
        return await cmd.ReadScalarAsync<int>(ct) > 0;
    }

    public async Task<int> CrearAsync(Insumo i, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Insumo (SedeId, Nombre, UnidadMedida, Clase, ContenidoValor, ContenidoUnidad, StockActual, StockMinimo, Activo, FechaIngreso, FechaVencimiento)
            OUTPUT INSERTED.Id
            VALUES (@SedeId, @Nombre, @UnidadMedida, @Clase, @ContenidoValor, @ContenidoUnidad, @StockActual, @StockMinimo, @Activo, @FechaIngreso, @FechaVencimiento)";
        cmd.AddParam("@SedeId", i.SedeId);
        cmd.AddParam("@Nombre", i.Nombre);
        cmd.AddParam("@UnidadMedida", i.UnidadMedida);
        cmd.AddParam("@Clase", i.Clase);
        cmd.AddParam("@ContenidoValor", (object?)i.ContenidoValor ?? DBNull.Value);
        cmd.AddParam("@ContenidoUnidad", (object?)i.ContenidoUnidad ?? DBNull.Value);
        cmd.AddParam("@StockActual", i.StockActual);
        cmd.AddParam("@StockMinimo", i.StockMinimo);
        cmd.AddParam("@Activo", i.Activo);
        cmd.AddParam("@FechaIngreso", (object?)(i.FechaIngreso?.ToDateTime(TimeOnly.MinValue)) ?? DBNull.Value);
        cmd.AddParam("@FechaVencimiento", (object?)(i.FechaVencimiento?.ToDateTime(TimeOnly.MinValue)) ?? DBNull.Value);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Insumo i, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Insumo
            SET Nombre = @Nombre, UnidadMedida = @UnidadMedida, Clase = @Clase,
                ContenidoValor = @ContenidoValor, ContenidoUnidad = @ContenidoUnidad,
                StockMinimo = @StockMinimo, Activo = @Activo, FechaIngreso = @FechaIngreso, FechaVencimiento = @FechaVencimiento
            WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", i.Id);
        cmd.AddParam("@Nombre", i.Nombre);
        cmd.AddParam("@UnidadMedida", i.UnidadMedida);
        cmd.AddParam("@Clase", i.Clase);
        cmd.AddParam("@ContenidoValor", (object?)i.ContenidoValor ?? DBNull.Value);
        cmd.AddParam("@ContenidoUnidad", (object?)i.ContenidoUnidad ?? DBNull.Value);
        cmd.AddParam("@StockMinimo", i.StockMinimo);
        cmd.AddParam("@Activo", i.Activo);
        cmd.AddParam("@FechaIngreso", (object?)(i.FechaIngreso?.ToDateTime(TimeOnly.MinValue)) ?? DBNull.Value);
        cmd.AddParam("@FechaVencimiento", (object?)(i.FechaVencimiento?.ToDateTime(TimeOnly.MinValue)) ?? DBNull.Value);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Insumo SET Activo = @Activo WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> RegistrarMovimientoAsync(MovimientoInsumo m, string? metodoPagoParaGasto, int? tipoGastoIdParaGasto, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int? movimientoCajaId = null;

            // Si es una compra con costo y metodo de pago, tambien se registra como gasto de caja
            if (m.Tipo == "COMPRA" && m.CostoTotal is > 0 && metodoPagoParaGasto is not null)
            {
                await using var cmdGasto = conn.CreateCommand();
                cmdGasto.Transaction = tx;
                cmdGasto.CommandText = @"
                    INSERT INTO dbo.MovimientoCaja (SedeId, Fecha, Tipo, MetodoPago, Monto, Descripcion, TipoGastoId, UsuarioId)
                    OUTPUT INSERTED.Id
                    VALUES (@SedeId, @Fecha, 'GASTO', @MetodoPago, @Monto, @Descripcion, @TipoGastoId, @UsuarioId);";
                cmdGasto.AddParam("@SedeId", m.SedeId);
                cmdGasto.AddParam("@Fecha", m.Fecha);
                cmdGasto.AddParam("@MetodoPago", metodoPagoParaGasto);
                cmdGasto.AddParam("@Monto", m.CostoTotal.Value);
                cmdGasto.AddParam("@Descripcion", m.Descripcion ?? $"Compra de {m.InsumoNombre}");
                cmdGasto.AddParam("@TipoGastoId", tipoGastoIdParaGasto);
                cmdGasto.AddParam("@UsuarioId", m.UsuarioId);
                movimientoCajaId = await cmdGasto.ReadScalarAsync<int>(ct);
            }

            await using var cmdMov = conn.CreateCommand();
            cmdMov.Transaction = tx;
            cmdMov.CommandText = @"
                INSERT INTO dbo.MovimientoInsumo (SedeId, InsumoId, Tipo, Cantidad, CostoTotal, Fecha, UsuarioId, Descripcion, MovimientoCajaId)
                OUTPUT INSERTED.Id
                VALUES (@SedeId, @InsumoId, @Tipo, @Cantidad, @CostoTotal, @Fecha, @UsuarioId, @Descripcion, @MovimientoCajaId);";
            cmdMov.AddParam("@SedeId", m.SedeId);
            cmdMov.AddParam("@InsumoId", m.InsumoId);
            cmdMov.AddParam("@Tipo", m.Tipo);
            cmdMov.AddParam("@Cantidad", m.Cantidad);
            cmdMov.AddParam("@CostoTotal", m.CostoTotal);
            cmdMov.AddParam("@Fecha", m.Fecha);
            cmdMov.AddParam("@UsuarioId", m.UsuarioId);
            cmdMov.AddParam("@Descripcion", m.Descripcion);
            cmdMov.AddParam("@MovimientoCajaId", movimientoCajaId);
            var id = await cmdMov.ReadScalarAsync<int>(ct);

            var delta = m.Tipo switch
            {
                "COMPRA" => m.Cantidad,
                "CONSUMO" => -m.Cantidad,
                _ => m.Cantidad // AJUSTE: la cantidad ya viene con el signo deseado
            };

            // El tope de stock >= 0 va en el propio WHERE (chequeo atomico): evita que dos
            // consumos concurrentes, cada uno validado por separado antes de llegar aqui, dejen
            // el stock negativo (TOCTOU si solo se valida afuera de la transaccion).
            await using var cmdStock = conn.CreateCommand();
            cmdStock.Transaction = tx;
            cmdStock.CommandText = @"
                UPDATE dbo.Insumo
                   SET StockActual = StockActual + @Delta
                 WHERE Id = @InsumoId AND SedeId = @SedeId AND StockActual + @Delta >= 0";
            cmdStock.AddParam("@Delta", delta);
            cmdStock.AddParam("@InsumoId", m.InsumoId);
            cmdStock.AddParam("@SedeId", m.SedeId);
            var stockRows = await cmdStock.ExecuteNonQueryAsync(ct);
            if (stockRows == 0)
            {
                await using var cmdExiste = conn.CreateCommand();
                cmdExiste.Transaction = tx;
                cmdExiste.CommandText = "SELECT COUNT(1) FROM dbo.Insumo WHERE Id = @InsumoId AND SedeId = @SedeId";
                cmdExiste.AddParam("@InsumoId", m.InsumoId);
                cmdExiste.AddParam("@SedeId", m.SedeId);
                var existe = await cmdExiste.ReadScalarAsync<int>(ct) > 0;
                throw new InvalidOperationException(existe
                    ? "Stock insuficiente para este movimiento."
                    : "Insumo no encontrado en esta sede.");
            }

            await tx.CommitAsync(ct);
            return id;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> EditarMovimientoAsync(int movimientoId, DateTime fecha, string? descripcion, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Lee el movimiento (para conservar la hora y ubicar el gasto de caja vinculado).
            await using var cmdGet = conn.CreateCommand();
            cmdGet.Transaction = tx;
            cmdGet.CommandText = "SELECT Fecha, MovimientoCajaId FROM dbo.MovimientoInsumo WHERE Id = @Id AND SedeId = @SedeId";
            cmdGet.AddParam("@Id", movimientoId);
            cmdGet.AddParam("@SedeId", sedeId);
            DateTime horaOriginal; int? movCajaId = null;
            await using (var r = await cmdGet.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) { await tx.RollbackAsync(ct); return false; }
                horaOriginal = r.GetDateTime(0);
                if (!r.IsDBNull(1)) movCajaId = r.GetInt32(1);
            }
            var nuevaFecha = fecha.Date + horaOriginal.TimeOfDay; // conserva la hora, cambia el día

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE dbo.MovimientoInsumo SET Fecha = @Fecha, Descripcion = @Desc WHERE Id = @Id AND SedeId = @SedeId";
            cmd.AddParam("@Fecha", nuevaFecha);
            cmd.AddParam("@Desc", (object?)descripcion ?? DBNull.Value);
            cmd.AddParam("@Id", movimientoId);
            cmd.AddParam("@SedeId", sedeId);
            await cmd.ExecuteNonQueryAsync(ct);

            // Mantener el gasto de caja vinculado en la misma fecha (para que salga en el cuadre correcto).
            if (movCajaId is int cajaId)
            {
                await using var cmdCaja = conn.CreateCommand();
                cmdCaja.Transaction = tx;
                cmdCaja.CommandText = "UPDATE dbo.MovimientoCaja SET Fecha = @Fecha WHERE Id = @Id AND SedeId = @SedeId";
                cmdCaja.AddParam("@Fecha", nuevaFecha);
                cmdCaja.AddParam("@Id", cajaId);
                cmdCaja.AddParam("@SedeId", sedeId);
                await cmdCaja.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return true;
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task<bool> EliminarMovimientoAsync(int movimientoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmdGet = conn.CreateCommand();
            cmdGet.Transaction = tx;
            cmdGet.CommandText = "SELECT InsumoId, Tipo, Cantidad, MovimientoCajaId FROM dbo.MovimientoInsumo WHERE Id = @Id AND SedeId = @SedeId";
            cmdGet.AddParam("@Id", movimientoId);
            cmdGet.AddParam("@SedeId", sedeId);
            int insumoId; string tipo; decimal cantidad; int? movCajaId = null;
            await using (var r = await cmdGet.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) { await tx.RollbackAsync(ct); return false; }
                insumoId = r.GetInt32(0);
                tipo = r.GetString(1);
                cantidad = r.GetDecimal(2);
                if (!r.IsDBNull(3)) movCajaId = r.GetInt32(3);
            }

            // Efecto original en stock: COMPRA +, CONSUMO −, AJUSTE con su signo. Para revertir, el opuesto.
            var deltaOriginal = tipo switch { "COMPRA" => cantidad, "CONSUMO" => -cantidad, _ => cantidad };
            var reversa = -deltaOriginal;

            await using var cmdStock = conn.CreateCommand();
            cmdStock.Transaction = tx;
            cmdStock.CommandText = @"
                UPDATE dbo.Insumo SET StockActual = StockActual + @Delta
                 WHERE Id = @InsumoId AND SedeId = @SedeId AND StockActual + @Delta >= 0";
            cmdStock.AddParam("@Delta", reversa);
            cmdStock.AddParam("@InsumoId", insumoId);
            cmdStock.AddParam("@SedeId", sedeId);
            if (await cmdStock.ExecuteNonQueryAsync(ct) == 0)
                throw new InvalidOperationException("No se puede eliminar: al revertirlo el stock quedaría negativo. Primero ajusta el stock.");

            await using var cmdDel = conn.CreateCommand();
            cmdDel.Transaction = tx;
            cmdDel.CommandText = "DELETE FROM dbo.MovimientoInsumo WHERE Id = @Id AND SedeId = @SedeId";
            cmdDel.AddParam("@Id", movimientoId);
            cmdDel.AddParam("@SedeId", sedeId);
            await cmdDel.ExecuteNonQueryAsync(ct);

            // Si tenía un gasto de caja vinculado (compra con costo), también se elimina.
            if (movCajaId is int cajaId)
            {
                await using var cmdCaja = conn.CreateCommand();
                cmdCaja.Transaction = tx;
                cmdCaja.CommandText = "DELETE FROM dbo.MovimientoCaja WHERE Id = @Id AND SedeId = @SedeId";
                cmdCaja.AddParam("@Id", cajaId);
                cmdCaja.AddParam("@SedeId", sedeId);
                await cmdCaja.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return true;
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task<List<MovimientoInsumo>> ListarMovimientosAsync(int? insumoId, DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var where = " WHERE m.Fecha >= @Desde AND m.Fecha < @Hasta AND m.SedeId = @SedeId ";
        if (insumoId is int id) where += " AND m.InsumoId = @InsumoId ";
        cmd.CommandText = @$"
            SELECT m.Id, m.InsumoId, i.Nombre AS InsumoNombre, m.Tipo, m.Cantidad, m.CostoTotal,
                   m.Fecha, u.NombreCompleto AS UsuarioNombre, m.Descripcion
            FROM dbo.MovimientoInsumo m
            INNER JOIN dbo.Insumo i ON i.Id = m.InsumoId
            INNER JOIN dbo.Usuario u ON u.Id = m.UsuarioId
            {where}
            ORDER BY m.Fecha DESC";
        cmd.AddParam("@Desde", desde);
        cmd.AddParam("@Hasta", hasta);
        cmd.AddParam("@SedeId", sedeId);
        if (insumoId is int idVal) cmd.AddParam("@InsumoId", idVal);
        return await cmd.ReadListAsync(r => new MovimientoInsumo
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            InsumoId = r.GetInt32(r.GetOrdinal("InsumoId")),
            InsumoNombre = r.GetNullableString("InsumoNombre"),
            Tipo = r.GetString(r.GetOrdinal("Tipo")),
            Cantidad = r.GetDecimal(r.GetOrdinal("Cantidad")),
            CostoTotal = r.GetNullableDecimal("CostoTotal"),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            UsuarioNombre = r.GetNullableString("UsuarioNombre"),
            Descripcion = r.GetNullableString("Descripcion")
        }, ct);
    }

    public async Task<Dictionary<DateTime, int>> ContarConsumoPorDiaAsync(DateTime desde, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CAST(Fecha AS DATE) AS Dia, CAST(ROUND(SUM(Cantidad), 0) AS INT) AS Cant
            FROM dbo.MovimientoInsumo
            WHERE Tipo = 'CONSUMO' AND SedeId = @SedeId AND CAST(Fecha AS DATE) >= @Desde
            GROUP BY CAST(Fecha AS DATE)";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Desde", desde.Date);
        var dict = new Dictionary<DateTime, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dict[reader.GetDateTime(0)] = reader.GetInt32(1);
        return dict;
    }
}
