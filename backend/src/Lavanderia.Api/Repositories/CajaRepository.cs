using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface ICajaRepository
{
    Task<int> RegistrarGastoAsync(MovimientoCaja movimiento, int negocioId, CancellationToken ct = default);
    Task<List<MovimientoCaja>> ListarMovimientosAsync(DateTime fecha, int sedeId, CancellationToken ct = default);
    Task<List<MovimientoCaja>> ListarMovimientosAsync(DateTime fecha, int? usuarioId, int sedeId, CancellationToken ct = default);
    Task<List<UsuarioDelDiaDto>> UsuariosDelDiaAsync(DateTime fecha, int sedeId, CancellationToken ct = default);
    Task<CuadreCaja?> ObtenerCuadreDelUsuarioAsync(DateTime fecha, int usuarioId, int sedeId, CancellationToken ct = default);
    Task<List<TipoGasto>> ListarTiposGastoAsync(int negocioId, CancellationToken ct = default);
    Task<List<TipoGasto>> ListarTodosTiposGastoAsync(int negocioId, CancellationToken ct = default);
    Task<TipoGasto?> ObtenerTipoGastoPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task<bool> ExisteNombreTipoGastoAsync(string nombre, int negocioId, int? excluirId = null, CancellationToken ct = default);
    Task<int> CrearTipoGastoAsync(TipoGasto t, CancellationToken ct = default);
    Task ActualizarTipoGastoAsync(TipoGasto t, int negocioId, CancellationToken ct = default);
    Task CambiarEstadoTipoGastoAsync(int id, bool activo, int negocioId, CancellationToken ct = default);
    Task<int> ContarUsoTipoGastoAsync(int tipoGastoId, int negocioId, CancellationToken ct = default);
    Task<int> GuardarCuadreAsync(CuadreCaja cuadre, CancellationToken ct = default);
    Task<CuadreCaja?> ObtenerCuadreAsync(int id, int sedeId, CancellationToken ct = default);
    Task<CuadreCaja?> ObtenerUltimoAnteriorAsync(DateTime fecha, int sedeId, CancellationToken ct = default);
}

public class CajaRepository : ICajaRepository
{
    private readonly ISqlConnectionFactory _factory;
    public CajaRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<int> RegistrarGastoAsync(MovimientoCaja m, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.MovimientoCaja (SedeId, Fecha, Tipo, MetodoPago, Monto, Descripcion, TipoGastoId, UsuarioId)
            OUTPUT INSERTED.Id
            SELECT @SedeId, @Fecha, 'GASTO', @MetodoPago, @Monto, @Descripcion, @TipoGastoId, @UsuarioId
            WHERE @TipoGastoId IS NULL OR EXISTS (
                SELECT 1 FROM dbo.TipoGasto WHERE Id = @TipoGastoId AND NegocioId = @NegocioId
            );";
        cmd.AddParam("@SedeId", m.SedeId);
        cmd.AddParam("@Fecha", m.Fecha);
        cmd.AddParam("@MetodoPago", m.MetodoPago);
        cmd.AddParam("@Monto", m.Monto);
        cmd.AddParam("@Descripcion", m.Descripcion);
        cmd.AddParam("@TipoGastoId", m.TipoGastoId);
        cmd.AddParam("@UsuarioId", m.UsuarioId);
        cmd.AddParam("@NegocioId", negocioId);
        var id = await cmd.ExecuteScalarAsync(ct);
        if (id is null || id == DBNull.Value) throw new InvalidOperationException("El tipo de gasto no pertenece a este negocio.");
        return Convert.ToInt32(id);
    }

    public Task<List<MovimientoCaja>> ListarMovimientosAsync(DateTime fecha, int sedeId, CancellationToken ct = default)
        => ListarMovimientosAsync(fecha, null, sedeId, ct);

    public async Task<List<MovimientoCaja>> ListarMovimientosAsync(DateTime fecha, int? usuarioId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var whereUsuario = usuarioId.HasValue ? " AND m.UsuarioId = @UsuarioId " : "";
        // Rango de fecha en vez de CAST(m.Fecha AS DATE) = @Fecha: envolver la columna en una
        // expresion la vuelve no-sargable (SQL Server no puede usar IX_MovimientoCaja_Fecha y
        // termina escaneando toda la tabla en vez de un seek acotado al dia).
        cmd.CommandText = @$"
            SELECT m.Id, m.Fecha, m.Tipo, m.MetodoPago, m.Monto, m.Descripcion, m.PedidoId, p.Numero AS PedidoNumero,
                   cl.Nombre AS ClienteNombre, m.UsuarioId, u.NombreCompleto AS UsuarioNombre,
                   m.TipoGastoId, tg.Nombre AS TipoGastoNombre
            FROM dbo.MovimientoCaja m
            LEFT JOIN dbo.TipoGasto tg ON tg.Id = m.TipoGastoId
            LEFT JOIN dbo.Pedido p ON p.Id = m.PedidoId
            LEFT JOIN dbo.Cliente cl ON cl.Id = p.ClienteId
            LEFT JOIN dbo.Usuario u ON u.Id = m.UsuarioId
            WHERE m.Fecha >= @Fecha AND m.Fecha < @FechaSiguiente AND m.SedeId = @SedeId {whereUsuario}
            ORDER BY m.Fecha DESC";
        cmd.AddParam("@Fecha", fecha.Date);
        cmd.AddParam("@FechaSiguiente", fecha.Date.AddDays(1));
        cmd.AddParam("@SedeId", sedeId);
        if (usuarioId.HasValue) cmd.AddParam("@UsuarioId", usuarioId.Value);
        return await cmd.ReadListAsync(r => new MovimientoCaja
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            Tipo = r.GetString(r.GetOrdinal("Tipo")),
            MetodoPago = r.GetString(r.GetOrdinal("MetodoPago")),
            Monto = r.GetDecimal(r.GetOrdinal("Monto")),
            Descripcion = r.GetNullableString("Descripcion"),
            PedidoId = r.GetNullableInt("PedidoId"),
            PedidoNumero = r.GetNullableInt("PedidoNumero"),
            ClienteNombre = r.GetNullableString("ClienteNombre"),
            UsuarioId = r.GetInt32(r.GetOrdinal("UsuarioId")),
            UsuarioNombre = r.GetNullableString("UsuarioNombre"),
            TipoGastoId = r.GetNullableInt("TipoGastoId"),
            TipoGastoNombre = r.GetNullableString("TipoGastoNombre")
        }, ct);
    }

    public async Task<List<TipoGasto>> ListarTiposGastoAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Nombre, Activo FROM dbo.TipoGasto WHERE Activo = 1 AND NegocioId = @NegocioId ORDER BY Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => new TipoGasto
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            Nombre = r.GetString(r.GetOrdinal("Nombre")),
            Activo = r.GetBoolean(r.GetOrdinal("Activo"))
        }, ct);
    }

    private static TipoGasto MapTipoGasto(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Activo = r.GetBoolean(r.GetOrdinal("Activo"))
    };

    public async Task<List<TipoGasto>> ListarTodosTiposGastoAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.Id, t.Nombre, t.Activo,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.MovimientoCaja mc WHERE mc.TipoGastoId = t.Id) THEN 1 ELSE 0 END AS BIT) AS EnUso
            FROM dbo.TipoGasto t WHERE t.NegocioId = @NegocioId ORDER BY t.Activo DESC, t.Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => { var t = MapTipoGasto(r); t.EnUso = r.GetBoolean(r.GetOrdinal("EnUso")); return t; }, ct);
    }

    public async Task<TipoGasto?> ObtenerTipoGastoPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Nombre, Activo FROM dbo.TipoGasto WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(MapTipoGasto, ct);
    }

    public async Task<bool> ExisteNombreTipoGastoAsync(string nombre, int negocioId, int? excluirId = null, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.TipoGasto
                WHERE NegocioId = @NegocioId
                  AND UPPER(LTRIM(RTRIM(Nombre))) = UPPER(@Nombre)
                  AND (@ExcluirId IS NULL OR Id <> @ExcluirId)
            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Nombre", nombre.Trim());
        cmd.AddParam("@ExcluirId", excluirId);
        return await cmd.ReadScalarAsync<bool>(ct);
    }

    public async Task<int> CrearTipoGastoAsync(TipoGasto t, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.TipoGasto (NegocioId, Nombre, Activo)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Nombre, @Activo)";
        cmd.AddParam("@NegocioId", t.NegocioId);
        cmd.AddParam("@Nombre", t.Nombre);
        cmd.AddParam("@Activo", t.Activo);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarTipoGastoAsync(TipoGasto t, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.TipoGasto SET Nombre = @Nombre, Activo = @Activo WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", t.Id);
        cmd.AddParam("@Nombre", t.Nombre);
        cmd.AddParam("@Activo", t.Activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoTipoGastoAsync(int id, bool activo, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.TipoGasto SET Activo = @Activo WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ContarUsoTipoGastoAsync(int tipoGastoId, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM dbo.MovimientoCaja m
            INNER JOIN dbo.Sede s ON s.Id = m.SedeId
            WHERE m.TipoGastoId = @Id AND s.NegocioId = @NegocioId";
        cmd.AddParam("@Id", tipoGastoId);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<int> GuardarCuadreAsync(CuadreCaja c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Upsert: si ya existe cuadre para esa fecha, actualiza; si no, inserta
        cmd.CommandText = @"
            MERGE dbo.CuadreCaja AS target
            USING (SELECT @SedeId AS SedeId, @Fecha AS Fecha, @UsuarioId AS UsuarioId) AS src
                ON target.SedeId = src.SedeId AND target.Fecha = src.Fecha AND target.UsuarioId = src.UsuarioId
            WHEN MATCHED THEN UPDATE SET
                CajaInicial = @CajaInicial,
                PedidosPagadosEfect = @PedidosPagadosEfect,
                Gastos = @Gastos,
                TotalContado = @TotalContado,
                Diferencia = @Diferencia,
                CajaFinal = @CajaFinal,
                Corte = @Corte,
                IngresosDigital = @IngresosDigital,
                IngresosTarjeta = @IngresosTarjeta,
                Nota = @Nota,
                Observaciones = @Observaciones,
                DetalleConteo = @DetalleConteo
            WHEN NOT MATCHED THEN INSERT
                (SedeId, Fecha, UsuarioId, CajaInicial, PedidosPagadosEfect, Gastos, TotalContado, Diferencia, CajaFinal, Corte, IngresosDigital, IngresosTarjeta, Nota, Observaciones, DetalleConteo)
                VALUES
                (@SedeId, @Fecha, @UsuarioId, @CajaInicial, @PedidosPagadosEfect, @Gastos, @TotalContado, @Diferencia, @CajaFinal, @Corte, @IngresosDigital, @IngresosTarjeta, @Nota, @Observaciones, @DetalleConteo)
            OUTPUT INSERTED.Id;";
        cmd.AddParam("@SedeId", c.SedeId);
        cmd.AddParam("@Fecha", c.Fecha.Date);
        cmd.AddParam("@UsuarioId", c.UsuarioId);
        cmd.AddParam("@CajaInicial", c.CajaInicial);
        cmd.AddParam("@PedidosPagadosEfect", c.PedidosPagadosEfect);
        cmd.AddParam("@Gastos", c.Gastos);
        cmd.AddParam("@TotalContado", c.TotalContado);
        cmd.AddParam("@Diferencia", c.Diferencia);
        cmd.AddParam("@CajaFinal", c.CajaFinal);
        cmd.AddParam("@Corte", c.Corte);
        cmd.AddParam("@IngresosDigital", c.IngresosDigital);
        cmd.AddParam("@IngresosTarjeta", c.IngresosTarjeta);
        cmd.AddParam("@Nota", c.Nota);
        cmd.AddParam("@Observaciones", c.Observaciones);
        cmd.AddParam("@DetalleConteo", c.DetalleConteo);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<CuadreCaja?> ObtenerCuadreAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.Id, c.Fecha, c.UsuarioId, u.NombreCompleto AS UsuarioNombre,
                   c.CajaInicial, c.PedidosPagadosEfect, c.Gastos, c.TotalContado,
                   c.Diferencia, c.CajaFinal, c.Corte, c.IngresosDigital, c.IngresosTarjeta, c.Nota, c.Observaciones, c.DetalleConteo, c.FechaCreacion
            FROM dbo.CuadreCaja c
            INNER JOIN dbo.Usuario u ON u.Id = c.UsuarioId
            WHERE c.Id = @Id AND c.SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(r => new CuadreCaja
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            UsuarioId = r.GetInt32(r.GetOrdinal("UsuarioId")),
            UsuarioNombre = r.GetString(r.GetOrdinal("UsuarioNombre")),
            CajaInicial = r.GetDecimal(r.GetOrdinal("CajaInicial")),
            PedidosPagadosEfect = r.GetDecimal(r.GetOrdinal("PedidosPagadosEfect")),
            Gastos = r.GetDecimal(r.GetOrdinal("Gastos")),
            TotalContado = r.GetDecimal(r.GetOrdinal("TotalContado")),
            Diferencia = r.GetDecimal(r.GetOrdinal("Diferencia")),
            CajaFinal = r.GetDecimal(r.GetOrdinal("CajaFinal")),
            Corte = r.GetDecimal(r.GetOrdinal("Corte")),
            IngresosDigital = r.GetDecimal(r.GetOrdinal("IngresosDigital")),
            IngresosTarjeta = r.GetDecimal(r.GetOrdinal("IngresosTarjeta")),
            Nota = r.GetNullableString("Nota"),
            Observaciones = r.GetNullableString("Observaciones"),
            DetalleConteo = r.GetNullableString("DetalleConteo"),
            FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion"))
        }, ct);
    }

    public async Task<List<UsuarioDelDiaDto>> UsuariosDelDiaAsync(DateTime fecha, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Une usuarios con movimientos ese día y usuarios que ya tienen cuadre guardado ese día
        cmd.CommandText = @"
            SELECT u.Id, u.NombreCompleto, r.Nombre AS RolNombre,
                   COALESCE(mv.Movimientos, 0) AS Movimientos,
                   CASE WHEN cq.Id IS NULL THEN 0 ELSE 1 END AS TieneCuadre
            FROM dbo.Usuario u
            INNER JOIN dbo.Rol r ON r.Id = u.RolId
            LEFT JOIN (
                SELECT UsuarioId, COUNT(*) AS Movimientos
                FROM dbo.MovimientoCaja
                WHERE Fecha >= @Fecha AND Fecha < @FechaSiguiente AND SedeId = @SedeId
                GROUP BY UsuarioId
            ) mv ON mv.UsuarioId = u.Id
            LEFT JOIN dbo.CuadreCaja cq ON cq.UsuarioId = u.Id AND cq.Fecha = @Fecha AND cq.SedeId = @SedeId
            WHERE u.Activo = 1 AND (mv.Movimientos > 0 OR cq.Id IS NOT NULL)
            ORDER BY r.Nombre, u.NombreCompleto";
        cmd.AddParam("@Fecha", fecha.Date);
        cmd.AddParam("@FechaSiguiente", fecha.Date.AddDays(1));
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(r => new UsuarioDelDiaDto(
            r.GetInt32(r.GetOrdinal("Id")),
            r.GetString(r.GetOrdinal("NombreCompleto")),
            r.GetString(r.GetOrdinal("RolNombre")),
            r.GetInt32(r.GetOrdinal("Movimientos")),
            r.GetInt32(r.GetOrdinal("TieneCuadre")) == 1
        ), ct);
    }

    public async Task<CuadreCaja?> ObtenerCuadreDelUsuarioAsync(DateTime fecha, int usuarioId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.Id, c.Fecha, c.UsuarioId, u.NombreCompleto AS UsuarioNombre,
                   c.CajaInicial, c.PedidosPagadosEfect, c.Gastos, c.TotalContado,
                   c.Diferencia, c.CajaFinal, c.Corte, c.IngresosDigital, c.IngresosTarjeta, c.Nota, c.Observaciones, c.DetalleConteo, c.FechaCreacion
            FROM dbo.CuadreCaja c
            INNER JOIN dbo.Usuario u ON u.Id = c.UsuarioId
            WHERE c.Fecha = @Fecha AND c.UsuarioId = @UsuarioId AND c.SedeId = @SedeId";
        cmd.AddParam("@Fecha", fecha.Date);
        cmd.AddParam("@UsuarioId", usuarioId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(r => new CuadreCaja
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            UsuarioId = r.GetInt32(r.GetOrdinal("UsuarioId")),
            UsuarioNombre = r.GetString(r.GetOrdinal("UsuarioNombre")),
            CajaInicial = r.GetDecimal(r.GetOrdinal("CajaInicial")),
            PedidosPagadosEfect = r.GetDecimal(r.GetOrdinal("PedidosPagadosEfect")),
            Gastos = r.GetDecimal(r.GetOrdinal("Gastos")),
            TotalContado = r.GetDecimal(r.GetOrdinal("TotalContado")),
            Diferencia = r.GetDecimal(r.GetOrdinal("Diferencia")),
            CajaFinal = r.GetDecimal(r.GetOrdinal("CajaFinal")),
            Corte = r.GetDecimal(r.GetOrdinal("Corte")),
            IngresosDigital = r.GetDecimal(r.GetOrdinal("IngresosDigital")),
            IngresosTarjeta = r.GetDecimal(r.GetOrdinal("IngresosTarjeta")),
            Nota = r.GetNullableString("Nota"),
            Observaciones = r.GetNullableString("Observaciones"),
            DetalleConteo = r.GetNullableString("DetalleConteo"),
            FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion"))
        }, ct);
    }

    public async Task<CuadreCaja?> ObtenerUltimoAnteriorAsync(DateTime fecha, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP 1 c.Id, c.Fecha, c.UsuarioId, u.NombreCompleto AS UsuarioNombre,
                   c.CajaInicial, c.PedidosPagadosEfect, c.Gastos, c.TotalContado,
                   c.Diferencia, c.CajaFinal, c.Corte, c.IngresosDigital, c.IngresosTarjeta, c.Nota, c.Observaciones, c.DetalleConteo, c.FechaCreacion
            FROM dbo.CuadreCaja c
            INNER JOIN dbo.Usuario u ON u.Id = c.UsuarioId
            WHERE c.Fecha < @Fecha AND c.SedeId = @SedeId
            ORDER BY c.Fecha DESC";
        cmd.AddParam("@Fecha", fecha.Date);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(r => new CuadreCaja
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            UsuarioId = r.GetInt32(r.GetOrdinal("UsuarioId")),
            UsuarioNombre = r.GetString(r.GetOrdinal("UsuarioNombre")),
            CajaInicial = r.GetDecimal(r.GetOrdinal("CajaInicial")),
            PedidosPagadosEfect = r.GetDecimal(r.GetOrdinal("PedidosPagadosEfect")),
            Gastos = r.GetDecimal(r.GetOrdinal("Gastos")),
            TotalContado = r.GetDecimal(r.GetOrdinal("TotalContado")),
            Diferencia = r.GetDecimal(r.GetOrdinal("Diferencia")),
            CajaFinal = r.GetDecimal(r.GetOrdinal("CajaFinal")),
            Corte = r.GetDecimal(r.GetOrdinal("Corte")),
            IngresosDigital = r.GetDecimal(r.GetOrdinal("IngresosDigital")),
            IngresosTarjeta = r.GetDecimal(r.GetOrdinal("IngresosTarjeta")),
            Nota = r.GetNullableString("Nota"),
            Observaciones = r.GetNullableString("Observaciones"),
            DetalleConteo = r.GetNullableString("DetalleConteo"),
            FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion"))
        }, ct);
    }
}
