using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Lavanderia.Api.Repositories;

public interface IPedidoRepository
{
    Task<int> CrearAsync(Pedido pedido, CancellationToken ct = default);
    Task<Pedido?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default);
    /// <summary>Conteo de pedidos por día (para las barras de tendencia). Si porEntrega es true
    /// cuenta por FechaEntregaReal de los ENTREGADOS; si no, por FechaIngreso de los no anulados.</summary>
    Task<Dictionary<DateTime, int>> ContarPorDiaAsync(DateTime desde, bool porEntrega, int sedeId, CancellationToken ct = default);
    Task<(List<Pedido> Items, int Total)> ListarPaginadoAsync(string? filtro, string? busqueda, DateTime? desde, DateTime? hasta, string? campoFecha, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default);
    Task<(List<Pedido> Items, int Total)> ListarPorClienteAsync(int clienteId, string? filtro, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default);
    Task<int> SiguienteNumeroAsync(int sedeId, CancellationToken ct = default);
    Task RegistrarHistorialAsync(PedidoHistorial h, SqlConnection conn, SqlTransaction tx, CancellationToken ct = default);
    Task AvanzarAreaAsync(
        int pedidoId, int? areaEsperada, string estadoEsperado,
        int? nuevaAreaId, string nuevoEstado, int? usuarioId, string? nota, string actorTipo,
        int sedeId, CancellationToken ct = default);
    Task<List<PedidoHistorial>> ObtenerHistorialAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task<List<PagoPedidoDto>> ObtenerPagosAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task<Dictionary<string, int>> ContadoresPorEstadoAsync(int sedeId, CancellationToken ct = default);
    Task<Dictionary<int, int>> ConteoPorAreaAsync(int sedeId, CancellationToken ct = default);
    Task<decimal> VentasDelDiaAsync(DateTime fecha, int sedeId, CancellationToken ct = default);
    /// <summary>Ventas (suma de Total de pedidos no anulados) por día desde una fecha, para la tendencia.</summary>
    Task<Dictionary<DateTime, decimal>> VentasPorDiaAsync(DateTime desde, int sedeId, CancellationToken ct = default);
    Task<int> PedidosDelMesAsync(DateTime fecha, int sedeId, CancellationToken ct = default);
    Task RegistrarPagoAsync(int pedidoId, decimal monto, string metodo, int usuarioId, string? descripcion, int sedeId, CancellationToken ct = default);
    /// <summary>Registra una entrega (parcial o final): actualiza CantidadEntregada de cada ítem, guarda
    /// la entrega y su detalle, registra los cobros (pago mixto) y ajusta el estado del pedido
    /// (ENTREGA_PARCIAL si aún quedan prendas, ENTREGADO si ya se entregó todo). Devuelve el estado final.</summary>
    Task<string> EntregarAsync(int pedidoId, List<(int PedidoItemId, decimal Cantidad)> items,
        List<(string Metodo, decimal Monto)> pagos, string? recibidoPor, string? nota,
        int usuarioId, int sedeId, CancellationToken ct = default);
    Task<List<PedidoEntrega>> ObtenerEntregasAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task AgregarItemAsync(int pedidoId, PedidoItem item, int sedeId, CancellationToken ct = default);
    Task AnularAsync(int pedidoId, int usuarioId, string motivo, int sedeId, CancellationToken ct = default);
    Task DonarAsync(int pedidoId, int usuarioId, int sedeId, CancellationToken ct = default);
    Task ReenviarAlmacenAsync(int pedidoId, int usuarioId, int sedeId, CancellationToken ct = default);
    Task ActualizarFechaEntregaAsync(int pedidoId, DateTime nuevaFecha, int? usuarioId, string? motivo, int sedeId, string actorTipo, CancellationToken ct = default);
    Task<List<PedidoAbandonado>> ListarListosAbandonadosAsync(int diasMinimo, int sedeId, CancellationToken ct = default);
    Task<bool> ActualizarDestinoDeliveryAsync(int pedidoId, string direccion, string distrito, string? referencia,
        decimal? latitud, decimal? longitud, int sedeId, CancellationToken ct = default);
    /// <summary>motorizadoId en null desasigna. No valida aqui que el motorizado pertenezca a
    /// la sede — eso lo valida el controller, que ya tiene el objeto Motorizado cargado.</summary>
    Task<bool> AsignarMotorizadoAsync(int pedidoId, int? motorizadoId, int sedeId, CancellationToken ct = default);
}

public class PedidoRepository : IPedidoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PedidoRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<int> SiguienteNumeroAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ISNULL(MAX(Numero), 0) + 1 FROM dbo.Pedido WHERE SedeId = @SedeId";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<int> CrearAsync(Pedido p, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            // El número correlativo se calcula DENTRO de la transacción con bloqueo de rango:
            // dos registros simultáneos en la misma sede ya no pueden obtener el mismo número
            // (antes se calculaba en una conexión aparte y chocaba contra el UNIQUE con un 500).
            await using var cmdNum = conn.CreateCommand();
            cmdNum.Transaction = tx;
            cmdNum.CommandText = @"
                SELECT ISNULL(MAX(Numero), 0) + 1
                  FROM dbo.Pedido WITH (UPDLOCK, HOLDLOCK)
                 WHERE SedeId = @SedeId";
            cmdNum.AddParam("@SedeId", p.SedeId);
            p.Numero = await cmdNum.ReadScalarAsync<int>(ct);

            // Insert Pedido
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO dbo.Pedido (
                    SedeId, Numero, ClienteId, UsuarioId, FechaIngreso, FechaEntregaEst, Modalidad,
                    DireccionEntrega, DistritoEntrega, ReferenciaEntrega, LatitudEntrega, LongitudEntrega,
                    Subtotal, Descuento, EsUrgente, RecargoUrgente, Redondeo, Total, MontoPagado, EstadoPago, EstadoProceso,
                    AreaActualId, Observaciones, CodigoAntiguo
                )
                OUTPUT INSERTED.Id
                VALUES (
                    @SedeId, @Numero, @ClienteId, @UsuarioId, @FechaIngreso, @FechaEntregaEst, @Modalidad,
                    @DireccionEntrega, @DistritoEntrega, @ReferenciaEntrega, @LatitudEntrega, @LongitudEntrega,
                    @Subtotal, @Descuento, @EsUrgente, @RecargoUrgente, @Redondeo, @Total, @MontoPagado, @EstadoPago, @EstadoProceso,
                    @AreaActualId, @Observaciones, @CodigoAntiguo
                );";
            cmd.AddParam("@SedeId", p.SedeId);
            cmd.AddParam("@Numero", p.Numero);
            cmd.AddParam("@ClienteId", p.ClienteId);
            cmd.AddParam("@UsuarioId", p.UsuarioId);
            cmd.AddParam("@FechaIngreso", p.FechaIngreso);
            cmd.AddParam("@FechaEntregaEst", p.FechaEntregaEst);
            cmd.AddParam("@Modalidad", p.Modalidad);
            cmd.AddParam("@DireccionEntrega", p.DireccionEntrega);
            cmd.AddParam("@DistritoEntrega", p.DistritoEntrega);
            cmd.AddParam("@ReferenciaEntrega", p.ReferenciaEntrega);
            cmd.AddParam("@LatitudEntrega", p.LatitudEntrega);
            cmd.AddParam("@LongitudEntrega", p.LongitudEntrega);
            cmd.AddParam("@Subtotal", p.Subtotal);
            cmd.AddParam("@Descuento", p.Descuento);
            cmd.AddParam("@EsUrgente", p.EsUrgente);
            cmd.AddParam("@RecargoUrgente", p.RecargoUrgente);
            cmd.AddParam("@Redondeo", p.Redondeo);
            cmd.AddParam("@Total", p.Total);
            cmd.AddParam("@MontoPagado", p.MontoPagado);
            cmd.AddParam("@EstadoPago", p.EstadoPago);
            cmd.AddParam("@EstadoProceso", p.EstadoProceso);
            cmd.AddParam("@AreaActualId", p.AreaActualId);
            cmd.AddParam("@Observaciones", p.Observaciones);
            cmd.AddParam("@CodigoAntiguo", p.CodigoAntiguo);

            var pedidoId = await cmd.ReadScalarAsync<int>(ct);
            p.Id = pedidoId;

            // Insert items
            foreach (var it in p.Items)
            {
                await using var cmdItem = conn.CreateCommand();
                cmdItem.Transaction = tx;
                cmdItem.CommandText = @"
                    INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion)
                    VALUES (@PedidoId, @ServicioId, @Cantidad, @PrecioUnit, @Total, @Descripcion);";
                cmdItem.AddParam("@PedidoId", pedidoId);
                cmdItem.AddParam("@ServicioId", it.ServicioId);
                cmdItem.AddParam("@Cantidad", it.Cantidad);
                cmdItem.AddParam("@PrecioUnit", it.PrecioUnit);
                cmdItem.AddParam("@Total", it.Total);
                cmdItem.AddParam("@Descripcion", it.Descripcion);
                await cmdItem.ExecuteNonQueryAsync(ct);
            }

            // Historial inicial
            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                AreaId = p.AreaActualId,
                EstadoProceso = p.EstadoProceso,
                UsuarioId = p.UsuarioId,
                Fecha = DateTime.Now,
                Nota = "Ingreso de pedido"
            }, conn, tx, ct);

            // Movimiento de caja por el pago inicial (si el cliente pagó algo al registrar el pedido)
            if (p.MontoPagado > 0)
            {
                await using var cmdMov = conn.CreateCommand();
                cmdMov.Transaction = tx;
                cmdMov.CommandText = @"
                    INSERT INTO dbo.MovimientoCaja
                           (SedeId, Fecha, Tipo, MetodoPago, Monto, Descripcion, PedidoId, UsuarioId)
                    VALUES (@SedeId, SYSDATETIME(), 'INGRESO', @Metodo, @Monto, @Descripcion, @PedidoId, @UsuarioId)";
                cmdMov.AddParam("@SedeId", p.SedeId);
                cmdMov.AddParam("@Metodo", p.MetodoPagoInicial);
                cmdMov.AddParam("@Monto", p.MontoPagado);
                cmdMov.AddParam("@Descripcion", $"Pago inicial de pedido #{p.Numero}");
                cmdMov.AddParam("@PedidoId", pedidoId);
                cmdMov.AddParam("@UsuarioId", p.UsuarioId);
                await cmdMov.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return pedidoId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task RegistrarHistorialAsync(PedidoHistorial h, SqlConnection conn, SqlTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO dbo.PedidoHistorial
                (PedidoId, AreaId, EstadoProceso, UsuarioId, ActorTipo, ActorDescripcion, Fecha, Nota, NotificadoWsp)
            VALUES
                (@PedidoId, @AreaId, @EstadoProceso, @UsuarioId, @ActorTipo, @ActorDescripcion, @Fecha, @Nota, @NotificadoWsp);";
        cmd.AddParam("@PedidoId", h.PedidoId);
        cmd.AddParam("@AreaId", h.AreaId);
        cmd.AddParam("@EstadoProceso", h.EstadoProceso);
        cmd.AddParam("@UsuarioId", h.UsuarioId);
        cmd.AddParam("@ActorTipo", h.ActorTipo);
        cmd.AddParam("@ActorDescripcion", h.ActorDescripcion);
        cmd.AddParam("@Fecha", h.Fecha);
        cmd.AddParam("@Nota", h.Nota);
        cmd.AddParam("@NotificadoWsp", h.NotificadoWsp);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Pedido?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.SedeId, p.Numero, p.ClienteId, c.Nombre AS ClienteNombre, c.Celular AS ClienteCelular, c.Dni AS ClienteDni, c.Puntos AS ClientePuntos,
                   p.UsuarioId, u.NombreCompleto AS UsuarioNombre, p.FechaIngreso, p.FechaEntregaEst, p.Modalidad,
                   p.DireccionEntrega, p.DistritoEntrega, p.ReferenciaEntrega, p.LatitudEntrega, p.LongitudEntrega,
                   p.Subtotal, p.Descuento, p.EsUrgente, p.RecargoUrgente, p.Redondeo, p.Total, p.MontoPagado, p.EstadoPago, p.EstadoProceso,
                   p.AreaActualId, a.Nombre AS AreaActualNombre,
                   p.Observaciones, p.FechaEntregaReal, p.Anulado, p.MotivoAnulacion, p.CodigoAntiguo
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN dbo.AreaLavado a ON a.Id = p.AreaActualId
            LEFT JOIN dbo.Usuario u ON u.Id = p.UsuarioId
            WHERE p.Id = @Id AND p.SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);

        var pedido = await cmd.ReadFirstOrDefaultAsync(MapPedido, ct);
        if (pedido == null) return null;

        // Motorizado asignado: consulta aparte (no se agrega a MapPedido, que reutilizan otras
        // consultas de esta clase con su propia lista de columnas — agregarlo ahi rompería esas).
        await using (var cmdMoto = conn.CreateCommand())
        {
            cmdMoto.CommandText = @"
                SELECT p.MotorizadoId, m.Nombre, m.Celular
                FROM dbo.Pedido p
                LEFT JOIN dbo.Motorizado m ON m.Id = p.MotorizadoId
                WHERE p.Id = @Id";
            cmdMoto.AddParam("@Id", id);
            await using var rMoto = await cmdMoto.ExecuteReaderAsync(ct);
            if (await rMoto.ReadAsync(ct) && !rMoto.IsDBNull(rMoto.GetOrdinal("MotorizadoId")))
            {
                pedido.MotorizadoId = rMoto.GetInt32(rMoto.GetOrdinal("MotorizadoId"));
                pedido.MotorizadoNombre = rMoto.GetNullableString("Nombre");
                pedido.MotorizadoCelular = rMoto.GetNullableString("Celular");
            }
        }

        await using var cmdItems = conn.CreateCommand();
        cmdItems.CommandText = @"
            SELECT i.Id, i.PedidoId, i.ServicioId, s.Nombre AS ServicioNombre, s.Unidad AS ServicioUnidad,
                   i.Cantidad, i.PrecioUnit, i.Total, i.Descripcion, i.CantidadEntregada
            FROM dbo.PedidoItem i
            INNER JOIN dbo.Servicio s ON s.Id = i.ServicioId
            WHERE i.PedidoId = @PedidoId";
        cmdItems.AddParam("@PedidoId", id);
        pedido.Items = await cmdItems.ReadListAsync(MapItem, ct);

        return pedido;
    }

    public async Task<(List<Pedido> Items, int Total)> ListarPaginadoAsync(
        string? filtro, string? busqueda, DateTime? desde, DateTime? hasta, string? campoFecha, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        string where;
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            where = @" WHERE p.SedeId = @SedeId AND (
                CAST(p.Numero AS NVARCHAR(20)) = @Busqueda
                OR c.Celular LIKE @BusquedaLike
                OR c.Nombre LIKE @BusquedaLike
                OR c.Dni LIKE @BusquedaLike
                OR p.CodigoAntiguo LIKE @BusquedaLike
            ) ";
            cmd.AddParam("@Busqueda", busqueda.Trim());
            cmd.AddParam("@BusquedaLike", $"%{busqueda.Trim()}%");
        }
        else
        {
            where = filtro?.ToLowerInvariant() switch
            {
                "pendientes" => " WHERE p.SedeId = @SedeId AND p.EstadoProceso IN ('PENDIENTE','EN_PROCESO','LISTO','ENTREGA_PARCIAL') AND p.Anulado = 0 ",
                "listos" => " WHERE p.SedeId = @SedeId AND p.EstadoProceso IN ('LISTO','ENTREGA_PARCIAL') AND p.Anulado = 0 ",
                "entregados" => " WHERE p.SedeId = @SedeId AND p.EstadoProceso = 'ENTREGADO' ",
                // "Otros": entregados + anulados + donados (todo lo que no es un pedido activo)
                "otros" => " WHERE p.SedeId = @SedeId AND (p.EstadoProceso = 'ENTREGADO' OR p.Anulado = 1) ",
                // "Últimos": los 500 más recientes sin filtro (limitado por paginación)
                "ultimos" => " WHERE p.SedeId = @SedeId ",
                _ => " WHERE p.SedeId = @SedeId AND p.Anulado = 0 "
            };

            if (string.Equals(filtro, "fecha", StringComparison.OrdinalIgnoreCase))
            {
                var desdeFiltro = (desde ?? DateTime.Today.AddDays(-30)).Date;
                var hastaExclusivo = (hasta ?? DateTime.Today).Date.AddDays(1);
                var columnaFecha = campoFecha == "entrega" ? "p.FechaEntregaEst" : "p.FechaIngreso";
                where = $@" WHERE p.SedeId = @SedeId
                    AND p.Anulado = 0
                    AND {columnaFecha} IS NOT NULL
                    AND {columnaFecha} >= @DesdeFecha
                    AND {columnaFecha} < @HastaFecha ";
                cmd.AddParam("@DesdeFecha", desdeFiltro);
                cmd.AddParam("@HastaFecha", hastaExclusivo);
            }
        }
        cmd.AddParam("@SedeId", sedeId);

        cmd.CommandText = @$"
            SELECT
                p.Id, p.Numero, p.ClienteId, c.Nombre AS ClienteNombre, c.Celular AS ClienteCelular, c.Dni AS ClienteDni, c.Puntos AS ClientePuntos,
                p.UsuarioId, u.NombreCompleto AS UsuarioNombre, p.FechaIngreso, p.FechaEntregaEst, p.Modalidad,
                p.DireccionEntrega, p.DistritoEntrega, p.ReferenciaEntrega, p.LatitudEntrega, p.LongitudEntrega,
                p.Subtotal, p.Descuento, p.EsUrgente, p.RecargoUrgente, p.Redondeo, p.Total, p.MontoPagado, p.EstadoPago, p.EstadoProceso,
                p.AreaActualId, a.Nombre AS AreaActualNombre,
                p.Observaciones, p.FechaEntregaReal, p.Anulado, p.MotivoAnulacion, p.CodigoAntiguo,
                COUNT(*) OVER() AS TotalRegistros
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN dbo.AreaLavado a ON a.Id = p.AreaActualId
            LEFT JOIN dbo.Usuario u ON u.Id = p.UsuarioId
            {where}
            ORDER BY p.EsUrgente DESC, p.FechaIngreso DESC
            OFFSET @Salto ROWS FETCH NEXT @Tamano ROWS ONLY";
        cmd.AddParam("@Salto", (pagina - 1) * tamanoPagina);
        cmd.AddParam("@Tamano", tamanoPagina);

        var total = 0;
        var items = await cmd.ReadListAsync(r =>
        {
            total = r.GetInt32(r.GetOrdinal("TotalRegistros"));
            return MapPedido(r);
        }, ct);

        return (items, total);
    }

    public async Task<(List<Pedido> Items, int Total)> ListarPorClienteAsync(
        int clienteId, string? filtro, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = filtro?.ToLowerInvariant() switch
        {
            "pendientes" => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId AND p.EstadoProceso IN ('PENDIENTE','EN_PROCESO','LISTO','ENTREGA_PARCIAL') AND p.Anulado = 0 ",
            "en-proceso" => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId AND p.EstadoProceso IN ('PENDIENTE','EN_PROCESO','LISTO','ENTREGA_PARCIAL') AND p.Anulado = 0 ",
            "con-deuda" => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId AND p.Anulado = 0 AND p.MontoPagado + 0.01 < p.Total ",
            "entregados" => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId AND p.EstadoProceso = 'ENTREGADO' ",
            _ => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId "
        };
        cmd.AddParam("@ClienteId", clienteId);
        cmd.AddParam("@SedeId", sedeId);

        cmd.CommandText = @$"
            SELECT
                p.Id, p.Numero, p.ClienteId, c.Nombre AS ClienteNombre, c.Celular AS ClienteCelular, c.Dni AS ClienteDni, c.Puntos AS ClientePuntos,
                p.UsuarioId, u.NombreCompleto AS UsuarioNombre, p.FechaIngreso, p.FechaEntregaEst, p.Modalidad,
                p.DireccionEntrega, p.DistritoEntrega, p.ReferenciaEntrega, p.LatitudEntrega, p.LongitudEntrega,
                p.Subtotal, p.Descuento, p.EsUrgente, p.RecargoUrgente, p.Redondeo, p.Total, p.MontoPagado, p.EstadoPago, p.EstadoProceso,
                p.AreaActualId, a.Nombre AS AreaActualNombre,
                p.Observaciones, p.FechaEntregaReal, p.Anulado, p.MotivoAnulacion, p.CodigoAntiguo,
                COUNT(*) OVER() AS TotalRegistros
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN dbo.AreaLavado a ON a.Id = p.AreaActualId
            LEFT JOIN dbo.Usuario u ON u.Id = p.UsuarioId
            {where}
            ORDER BY p.FechaIngreso DESC
            OFFSET @Salto ROWS FETCH NEXT @Tamano ROWS ONLY";
        cmd.AddParam("@Salto", (pagina - 1) * tamanoPagina);
        cmd.AddParam("@Tamano", tamanoPagina);

        var total = 0;
        var items = await cmd.ReadListAsync(r =>
        {
            total = r.GetInt32(r.GetOrdinal("TotalRegistros"));
            return MapPedido(r);
        }, ct);

        return (items, total);
    }

    public async Task<List<PedidoAbandonado>> ListarListosAbandonadosAsync(int diasMinimo, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            ;WITH UltimoListo AS (
                SELECT PedidoId, MAX(Fecha) AS FechaListo
                FROM dbo.PedidoHistorial
                WHERE EstadoProceso = 'LISTO'
                GROUP BY PedidoId
            )
            SELECT p.Id AS PedidoId, p.Numero, c.Nombre AS ClienteNombre, c.Celular AS ClienteCelular,
                   p.Total, p.MontoPagado, ul.FechaListo
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            INNER JOIN UltimoListo ul ON ul.PedidoId = p.Id
            WHERE p.EstadoProceso = 'LISTO' AND p.Anulado = 0 AND p.SedeId = @SedeId
              AND ul.FechaListo <= DATEADD(day, -@Dias, SYSDATETIME())
            ORDER BY ul.FechaListo ASC";
        cmd.AddParam("@Dias", diasMinimo);
        cmd.AddParam("@SedeId", sedeId);

        return await cmd.ReadListAsync(r =>
        {
            var fechaListo = r.GetDateTime(r.GetOrdinal("FechaListo"));
            return new PedidoAbandonado
            {
                PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
                Numero = r.GetInt32(r.GetOrdinal("Numero")),
                ClienteNombre = r.GetString(r.GetOrdinal("ClienteNombre")),
                ClienteCelular = r.GetNullableString("ClienteCelular"),
                Total = r.GetDecimal(r.GetOrdinal("Total")),
                MontoPagado = r.GetDecimal(r.GetOrdinal("MontoPagado")),
                FechaListo = fechaListo,
                DiasEsperando = (int)(DateTime.Now - fechaListo).TotalDays
            };
        }, ct);
    }

    public async Task AvanzarAreaAsync(
        int pedidoId, int? areaEsperada, string estadoEsperado,
        int? nuevaAreaId, string nuevoEstado, int? usuarioId, string? nota, string actorTipo,
        int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // El bloqueo serializa dobles clics y evita dos entradas de historial para el mismo paso.
            await using var cmdActual = conn.CreateCommand();
            cmdActual.Transaction = tx;
            cmdActual.CommandText = @"
                SELECT AreaActualId, EstadoProceso, Anulado
                  FROM dbo.Pedido WITH (UPDLOCK, ROWLOCK)
                 WHERE Id = @Id AND SedeId = @SedeId";
            cmdActual.AddParam("@Id", pedidoId);
            cmdActual.AddParam("@SedeId", sedeId);

            int? areaActualDb = null;
            string? estadoActualDb = null;
            var anuladoDb = false;
            await using (var r = await cmdActual.ExecuteReaderAsync(ct))
            {
                if (await r.ReadAsync(ct))
                {
                    areaActualDb = r.IsDBNull(0) ? null : r.GetInt32(0);
                    estadoActualDb = r.GetString(1);
                    anuladoDb = r.GetBoolean(2);
                }
            }

            if (estadoActualDb is null)
            {
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException("Pedido no encontrado.");
            }

            if (anuladoDb || estadoActualDb is "ENTREGADO" or "ANULADO" or "DONADO")
            {
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException("El pedido está finalizado y no puede volver a avanzar.");
            }

            if (areaActualDb != areaEsperada || estadoActualDb != estadoEsperado)
            {
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException(
                    "El pedido cambió de etapa mientras se procesaba la solicitud. Actualiza la lista antes de intentarlo otra vez.");
            }

            // 2. Si no hay cambio real, no escribimos nada (evita historial duplicado)
            var mismaArea = areaActualDb == nuevaAreaId;
            var mismoEstado = estadoActualDb == nuevoEstado;
            if (mismaArea && mismoEstado)
            {
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException("El pedido ya está en ese estado. No hay nada que actualizar.");
            }

            // 3. Actualizar
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE dbo.Pedido
                   SET AreaActualId = @AreaId,
                       EstadoProceso = @Estado,
                       FechaEntregaReal = CASE WHEN @Estado = 'ENTREGADO' THEN SYSDATETIME() ELSE FechaEntregaReal END,
                       TokenRuta = CASE WHEN @Estado = 'ENTREGADO' THEN NULL ELSE TokenRuta END,
                       TokenRutaExpiraEn = CASE WHEN @Estado = 'ENTREGADO' THEN NULL ELSE TokenRutaExpiraEn END
                 WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0";
            cmd.AddParam("@AreaId", nuevaAreaId);
            cmd.AddParam("@Estado", nuevoEstado);
            cmd.AddParam("@Id", pedidoId);
            cmd.AddParam("@SedeId", sedeId);
            await cmd.ExecuteNonQueryAsync(ct);

            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                AreaId = nuevaAreaId,
                EstadoProceso = nuevoEstado,
                UsuarioId = usuarioId,
                ActorTipo = actorTipo,
                ActorDescripcion = actorTipo == "USUARIO" ? null : actorTipo,
                Fecha = DateTime.Now,
                Nota = nota
            }, conn, tx, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<PedidoHistorial>> ObtenerHistorialAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT h.Id, h.PedidoId, h.AreaId, a.Nombre AS AreaNombre,
                   h.EstadoProceso, h.UsuarioId, h.ActorTipo, h.ActorDescripcion, h.Fecha, h.Nota, h.NotificadoWsp
            FROM dbo.PedidoHistorial h
            INNER JOIN dbo.Pedido p ON p.Id = h.PedidoId
            LEFT JOIN dbo.AreaLavado a ON a.Id = h.AreaId
            WHERE h.PedidoId = @PedidoId AND p.SedeId = @SedeId
            ORDER BY h.Fecha ASC";
        cmd.AddParam("@PedidoId", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(r => new PedidoHistorial
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
            AreaId = r.GetNullableInt("AreaId"),
            AreaNombre = r.GetNullableString("AreaNombre"),
            EstadoProceso = r.GetString(r.GetOrdinal("EstadoProceso")),
            UsuarioId = r.GetNullableInt("UsuarioId"),
            ActorTipo = r.GetString(r.GetOrdinal("ActorTipo")),
            ActorDescripcion = r.GetNullableString("ActorDescripcion"),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            Nota = r.GetNullableString("Nota"),
            NotificadoWsp = r.GetBoolean(r.GetOrdinal("NotificadoWsp"))
        }, ct);
    }

    /// <summary>
    /// Cobros registrados de un pedido, con su metodo de pago. Se leen de MovimientoCaja,
    /// que es donde queda la plata: el pedido solo guarda el acumulado (MontoPagado) y por eso
    /// un adelanto en Yape no se distinguia de uno en efectivo al mirar la orden.
    /// </summary>
    public async Task<List<PagoPedidoDto>> ObtenerPagosAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT m.Id, m.Fecha, m.MetodoPago, m.Monto, m.Descripcion, u.NombreCompleto AS UsuarioNombre
            FROM dbo.MovimientoCaja m
            INNER JOIN dbo.Pedido p ON p.Id = m.PedidoId
            LEFT JOIN dbo.Usuario u ON u.Id = m.UsuarioId
            WHERE m.PedidoId = @PedidoId AND m.Tipo = 'INGRESO' AND p.SedeId = @SedeId
            ORDER BY m.Fecha ASC";
        cmd.AddParam("@PedidoId", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(r => new PagoPedidoDto
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            MetodoPago = r.GetString(r.GetOrdinal("MetodoPago")),
            Monto = r.GetDecimal(r.GetOrdinal("Monto")),
            Descripcion = r.GetNullableString("Descripcion"),
            UsuarioNombre = r.GetNullableString("UsuarioNombre")
        }, ct);
    }

    public async Task<Dictionary<string, int>> ContadoresPorEstadoAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT EstadoProceso, COUNT(1) AS Total
            FROM dbo.Pedido
            WHERE Anulado = 0 AND SedeId = @SedeId
            GROUP BY EstadoProceso";
        cmd.AddParam("@SedeId", sedeId);
        var dict = new Dictionary<string, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dict[reader.GetString(0)] = reader.GetInt32(1);
        return dict;
    }

    public async Task<Dictionary<int, int>> ConteoPorAreaAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT AreaActualId, COUNT(1) AS Total
            FROM dbo.Pedido
            WHERE Anulado = 0
              AND EstadoProceso IN ('PENDIENTE','EN_PROCESO')
              AND AreaActualId IS NOT NULL
              AND SedeId = @SedeId
            GROUP BY AreaActualId";
        cmd.AddParam("@SedeId", sedeId);
        var dict = new Dictionary<int, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dict[reader.GetInt32(0)] = reader.GetInt32(1);
        return dict;
    }

    public async Task<decimal> VentasDelDiaAsync(DateTime fecha, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ISNULL(SUM(Total), 0)
            FROM dbo.Pedido
            WHERE Anulado = 0 AND CAST(FechaIngreso AS DATE) = CAST(@Fecha AS DATE) AND SedeId = @SedeId";
        cmd.AddParam("@Fecha", fecha.Date);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadScalarAsync<decimal>(ct);
    }

    public async Task<Dictionary<DateTime, decimal>> VentasPorDiaAsync(DateTime desde, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // FechaIngreso >= @Desde (sargable, usa índice) y se agrupa por día.
        cmd.CommandText = @"
            SELECT CAST(FechaIngreso AS DATE) AS Dia, ISNULL(SUM(Total), 0) AS Total
            FROM dbo.Pedido
            WHERE Anulado = 0 AND SedeId = @SedeId AND FechaIngreso >= @Desde
            GROUP BY CAST(FechaIngreso AS DATE)";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Desde", desde.Date);
        var dict = new Dictionary<DateTime, decimal>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dict[reader.GetDateTime(0).Date] = reader.GetDecimal(1);
        return dict;
    }

    public async Task<Dictionary<DateTime, int>> ContarPorDiaAsync(DateTime desde, bool porEntrega, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var col = porEntrega ? "FechaEntregaReal" : "FechaIngreso";
        var filtro = porEntrega ? "EstadoProceso = 'ENTREGADO' AND FechaEntregaReal IS NOT NULL" : "Anulado = 0";
        cmd.CommandText = $@"
            SELECT CAST({col} AS DATE) AS Dia, COUNT(1) AS Cant
            FROM dbo.Pedido
            WHERE {filtro} AND SedeId = @SedeId AND CAST({col} AS DATE) >= @Desde
            GROUP BY CAST({col} AS DATE)";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Desde", desde.Date);
        var dict = new Dictionary<DateTime, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dict[reader.GetDateTime(0)] = reader.GetInt32(1);
        return dict;
    }

    public async Task<int> PedidosDelMesAsync(DateTime fecha, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM dbo.Pedido
            WHERE Anulado = 0
              AND YEAR(FechaIngreso) = YEAR(@Fecha)
              AND MONTH(FechaIngreso) = MONTH(@Fecha)
              AND SedeId = @SedeId";
        cmd.AddParam("@Fecha", fecha.Date);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task RegistrarPagoAsync(int pedidoId, decimal monto, string metodo, int usuarioId, string? descripcion, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Sumar monto pagado y recalcular estado
            await using var cmdPed = conn.CreateCommand();
            cmdPed.Transaction = tx;
            // El tope contra Total va en el propio WHERE (chequeo atomico): evita que dos pagos
            // concurrentes para el mismo pedido, cada uno validado por separado antes de llegar
            // aqui, sumen mas del total (TOCTOU si solo se valida afuera de la transaccion).
            cmdPed.CommandText = @"
                UPDATE dbo.Pedido
                   SET MontoPagado = MontoPagado + @Monto,
                       EstadoPago = CASE
                                      WHEN (MontoPagado + @Monto) >= Total THEN 'PAGADO'
                                      WHEN (MontoPagado + @Monto) > 0 THEN 'PARCIAL'
                                      ELSE 'PENDIENTE'
                                    END
                 WHERE Id = @PedidoId AND SedeId = @SedeId AND Anulado = 0
                   AND EstadoProceso NOT IN ('ANULADO', 'DONADO')
                   AND MontoPagado + @Monto <= Total + 0.01";
            cmdPed.AddParam("@Monto", monto);
            cmdPed.AddParam("@PedidoId", pedidoId);
            cmdPed.AddParam("@SedeId", sedeId);
            var filas = await cmdPed.ExecuteNonQueryAsync(ct);
            if (filas == 0)
            {
                await using var cmdExiste = conn.CreateCommand();
                cmdExiste.Transaction = tx;
                cmdExiste.CommandText = @"
                    SELECT CASE
                             WHEN Anulado = 1 OR EstadoProceso IN ('ANULADO','DONADO') THEN 2
                             ELSE 1
                           END
                      FROM dbo.Pedido
                     WHERE Id = @PedidoId AND SedeId = @SedeId";
                cmdExiste.AddParam("@PedidoId", pedidoId);
                cmdExiste.AddParam("@SedeId", sedeId);
                var resultado = await cmdExiste.ReadScalarAsync<int>(ct);
                throw new InvalidOperationException(resultado switch
                {
                    2 => "El pedido está anulado o donado y no admite nuevos pagos.",
                    1 => "El monto excede el saldo pendiente del pedido.",
                    _ => "Pedido no encontrado."
                });
            }

            // 2) Registrar en MovimientoCaja
            await using var cmdMov = conn.CreateCommand();
            cmdMov.Transaction = tx;
            cmdMov.CommandText = @"
                INSERT INTO dbo.MovimientoCaja
                       (SedeId, Fecha, Tipo, MetodoPago, Monto, Descripcion, PedidoId, UsuarioId)
                VALUES (@SedeId, SYSDATETIME(), 'INGRESO', @Metodo, @Monto, @Descripcion, @PedidoId, @UsuarioId)";
            cmdMov.AddParam("@SedeId", sedeId);
            cmdMov.AddParam("@Metodo", metodo);
            cmdMov.AddParam("@Monto", monto);
            cmdMov.AddParam("@Descripcion", descripcion ?? $"Pago de pedido");
            cmdMov.AddParam("@PedidoId", pedidoId);
            cmdMov.AddParam("@UsuarioId", usuarioId);
            await cmdMov.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<string> EntregarAsync(int pedidoId, List<(int PedidoItemId, decimal Cantidad)> items,
        List<(string Metodo, decimal Monto)> pagos, string? recibidoPor, string? nota,
        int usuarioId, int sedeId, CancellationToken ct = default)
    {
        var totalCobrado = pagos.Sum(p => p.Monto);

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Sumar lo entregado a cada ítem (con tope atómico contra la cantidad total).
            foreach (var (itemId, cantidad) in items)
            {
                await using var cmdItem = conn.CreateCommand();
                cmdItem.Transaction = tx;
                cmdItem.CommandText = @"
                    UPDATE dbo.PedidoItem
                       SET CantidadEntregada = CantidadEntregada + @Cant
                     WHERE Id = @ItemId AND PedidoId = @PedidoId
                       AND CantidadEntregada + @Cant <= Cantidad + 0.01";
                cmdItem.AddParam("@Cant", cantidad);
                cmdItem.AddParam("@ItemId", itemId);
                cmdItem.AddParam("@PedidoId", pedidoId);
                if (await cmdItem.ExecuteNonQueryAsync(ct) == 0)
                    throw new InvalidOperationException("La cantidad a entregar de un ítem supera lo que queda pendiente. Actualiza el pedido e inténtalo de nuevo.");
            }

            // 2) ¿Cuánto queda pendiente por entregar y se llegó a entregar algo alguna vez?
            await using var cmdPend = conn.CreateCommand();
            cmdPend.Transaction = tx;
            cmdPend.CommandText = @"
                SELECT ISNULL(SUM(Cantidad - CantidadEntregada), 0) AS Pendiente,
                       ISNULL(SUM(CantidadEntregada), 0) AS Entregado
                FROM dbo.PedidoItem WHERE PedidoId = @PedidoId";
            cmdPend.AddParam("@PedidoId", pedidoId);
            decimal pendiente, entregadoTotal;
            await using (var rp = await cmdPend.ExecuteReaderAsync(ct))
            {
                await rp.ReadAsync(ct);
                pendiente = rp.GetDecimal(0);
                entregadoTotal = rp.GetDecimal(1);
            }

            // Estado resultante: todo entregado => ENTREGADO; si se entregó algo pero falta => ENTREGA_PARCIAL;
            // si no se entregó nada (solo se cobró) => se conserva el estado actual (LISTO / ENTREGA_PARCIAL).
            await using var cmdEstadoActual = conn.CreateCommand();
            cmdEstadoActual.Transaction = tx;
            cmdEstadoActual.CommandText = "SELECT EstadoProceso FROM dbo.Pedido WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0";
            cmdEstadoActual.AddParam("@Id", pedidoId);
            cmdEstadoActual.AddParam("@SedeId", sedeId);
            var estadoActual = (await cmdEstadoActual.ReadScalarAsync<string>(ct))
                ?? throw new InvalidOperationException("Pedido no encontrado.");

            var nuevoEstado = pendiente <= 0.01m ? "ENTREGADO"
                : entregadoTotal > 0.01m ? "ENTREGA_PARCIAL"
                : estadoActual;
            var esFinal = nuevoEstado == "ENTREGADO";

            // 3) Actualizar pedido: acumular pago, recalcular estado de pago y estado de proceso.
            await using var cmdPed = conn.CreateCommand();
            cmdPed.Transaction = tx;
            cmdPed.CommandText = @"
                UPDATE dbo.Pedido
                   SET MontoPagado = MontoPagado + @Monto,
                       EstadoPago = CASE
                                      WHEN (MontoPagado + @Monto) >= Total THEN 'PAGADO'
                                      WHEN (MontoPagado + @Monto) > 0 THEN 'PARCIAL'
                                      ELSE 'PENDIENTE'
                                    END,
                       EstadoProceso = @Estado,
                       FechaEntregaReal = CASE WHEN @Estado = 'ENTREGADO' THEN SYSDATETIME() ELSE FechaEntregaReal END,
                       TokenRuta = CASE WHEN @Estado = 'ENTREGADO' THEN NULL ELSE TokenRuta END,
                       TokenRutaExpiraEn = CASE WHEN @Estado = 'ENTREGADO' THEN NULL ELSE TokenRutaExpiraEn END
                 WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0
                   AND EstadoProceso NOT IN ('ANULADO', 'DONADO')
                   AND MontoPagado + @Monto <= Total + 0.01";
            cmdPed.AddParam("@Monto", totalCobrado);
            cmdPed.AddParam("@Estado", nuevoEstado);
            cmdPed.AddParam("@Id", pedidoId);
            cmdPed.AddParam("@SedeId", sedeId);
            if (await cmdPed.ExecuteNonQueryAsync(ct) == 0)
                throw new InvalidOperationException("No se pudo registrar la entrega (el monto cobrado excede el saldo o el pedido cambió de estado).");

            // 4) Registrar cada cobro en MovimientoCaja (pago mixto = varias filas).
            foreach (var (metodo, monto) in pagos)
            {
                await using var cmdMov = conn.CreateCommand();
                cmdMov.Transaction = tx;
                cmdMov.CommandText = @"
                    INSERT INTO dbo.MovimientoCaja
                           (SedeId, Fecha, Tipo, MetodoPago, Monto, Descripcion, PedidoId, UsuarioId)
                    VALUES (@SedeId, SYSDATETIME(), 'INGRESO', @Metodo, @Monto, @Descripcion, @PedidoId, @UsuarioId)";
                cmdMov.AddParam("@SedeId", sedeId);
                cmdMov.AddParam("@Metodo", metodo);
                cmdMov.AddParam("@Monto", monto);
                cmdMov.AddParam("@Descripcion", esFinal ? "Cobro en entrega del pedido" : "Cobro en entrega parcial");
                cmdMov.AddParam("@PedidoId", pedidoId);
                cmdMov.AddParam("@UsuarioId", usuarioId);
                await cmdMov.ExecuteNonQueryAsync(ct);
            }

            // 5) Guardar la entrega (cabecera) y su detalle de ítems.
            await using var cmdEnt = conn.CreateCommand();
            cmdEnt.Transaction = tx;
            cmdEnt.CommandText = @"
                INSERT INTO dbo.PedidoEntrega (PedidoId, SedeId, Fecha, UsuarioId, RecibidoPor, Nota, EsFinal, MontoCobrado)
                OUTPUT INSERTED.Id
                VALUES (@PedidoId, @SedeId, SYSDATETIME(), @UsuarioId, @RecibidoPor, @Nota, @EsFinal, @Monto)";
            cmdEnt.AddParam("@PedidoId", pedidoId);
            cmdEnt.AddParam("@SedeId", sedeId);
            cmdEnt.AddParam("@UsuarioId", usuarioId);
            cmdEnt.AddParam("@RecibidoPor", recibidoPor);
            cmdEnt.AddParam("@Nota", nota);
            cmdEnt.AddParam("@EsFinal", esFinal);
            cmdEnt.AddParam("@Monto", totalCobrado);
            var entregaId = await cmdEnt.ReadScalarAsync<int>(ct);

            foreach (var (itemId, cantidad) in items)
            {
                await using var cmdDet = conn.CreateCommand();
                cmdDet.Transaction = tx;
                cmdDet.CommandText = @"
                    INSERT INTO dbo.PedidoEntregaDetalle (EntregaId, PedidoItemId, Cantidad)
                    VALUES (@EntregaId, @ItemId, @Cant)";
                cmdDet.AddParam("@EntregaId", entregaId);
                cmdDet.AddParam("@ItemId", itemId);
                cmdDet.AddParam("@Cant", cantidad);
                await cmdDet.ExecuteNonQueryAsync(ct);
            }

            // 6) Historial legible del movimiento.
            var notaHist = esFinal
                ? (entregadoTotal > 0.01m && items.Count == 0 ? "Entrega final (sin prendas nuevas)" : "Entrega final: se completó la entrega del pedido")
                : "Entrega parcial";
            if (!string.IsNullOrWhiteSpace(nota)) notaHist += $" — {nota!.Trim()}";
            if (!string.IsNullOrWhiteSpace(recibidoPor)) notaHist += $" (recibió: {recibidoPor!.Trim()})";
            if (totalCobrado > 0) notaHist += $" · Cobrado S/ {totalCobrado:0.00}";

            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                AreaId = null,
                EstadoProceso = nuevoEstado,
                UsuarioId = usuarioId,
                ActorTipo = "USUARIO",
                ActorDescripcion = null,
                Fecha = DateTime.Now,
                Nota = notaHist
            }, conn, tx, ct);

            await tx.CommitAsync(ct);
            return nuevoEstado;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<PedidoEntrega>> ObtenerEntregasAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT e.Id, e.Fecha, e.RecibidoPor, e.Nota, e.EsFinal, e.MontoCobrado, u.NombreCompleto AS UsuarioNombre
            FROM dbo.PedidoEntrega e
            INNER JOIN dbo.Pedido p ON p.Id = e.PedidoId
            LEFT JOIN dbo.Usuario u ON u.Id = e.UsuarioId
            WHERE e.PedidoId = @PedidoId AND p.SedeId = @SedeId
            ORDER BY e.Fecha ASC";
        cmd.AddParam("@PedidoId", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        var entregas = await cmd.ReadListAsync(r => new PedidoEntrega
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            PedidoId = pedidoId,
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            RecibidoPor = r.GetNullableString("RecibidoPor"),
            Nota = r.GetNullableString("Nota"),
            EsFinal = r.GetBoolean(r.GetOrdinal("EsFinal")),
            MontoCobrado = r.GetDecimal(r.GetOrdinal("MontoCobrado")),
            UsuarioNombre = r.GetNullableString("UsuarioNombre")
        }, ct);

        if (entregas.Count == 0) return entregas;

        await using var cmdDet = conn.CreateCommand();
        cmdDet.CommandText = @"
            SELECT d.EntregaId, d.PedidoItemId, d.Cantidad, s.Nombre AS ServicioNombre, s.Unidad AS ServicioUnidad
            FROM dbo.PedidoEntregaDetalle d
            INNER JOIN dbo.PedidoEntrega e ON e.Id = d.EntregaId
            INNER JOIN dbo.PedidoItem i ON i.Id = d.PedidoItemId
            INNER JOIN dbo.Servicio s ON s.Id = i.ServicioId
            WHERE e.PedidoId = @PedidoId";
        cmdDet.AddParam("@PedidoId", pedidoId);
        var detalles = await cmdDet.ReadListAsync(r => new PedidoEntregaDetalle
        {
            EntregaId = r.GetInt32(r.GetOrdinal("EntregaId")),
            PedidoItemId = r.GetInt32(r.GetOrdinal("PedidoItemId")),
            Cantidad = r.GetDecimal(r.GetOrdinal("Cantidad")),
            ServicioNombre = r.GetNullableString("ServicioNombre"),
            ServicioUnidad = r.GetNullableString("ServicioUnidad")
        }, ct);

        foreach (var e in entregas)
            e.Items = detalles.Where(d => d.EntregaId == e.Id).ToList();
        return entregas;
    }

    public async Task AgregarItemAsync(int pedidoId, PedidoItem item, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmdCheck = conn.CreateCommand();
            cmdCheck.Transaction = tx;
            cmdCheck.CommandText = @"
                SELECT COUNT(1)
                FROM dbo.Pedido WITH (UPDLOCK, HOLDLOCK)
                WHERE Id = @PedidoId AND SedeId = @SedeId AND Anulado = 0
                  AND EstadoProceso NOT IN ('ENTREGADO', 'ANULADO', 'DONADO')";
            cmdCheck.AddParam("@PedidoId", pedidoId);
            cmdCheck.AddParam("@SedeId", sedeId);
            if (await cmdCheck.ReadScalarAsync<int>(ct) == 0)
                throw new InvalidOperationException("El pedido no existe, está anulado o ya se encuentra finalizado.");

            await using var cmdItem = conn.CreateCommand();
            cmdItem.Transaction = tx;
            cmdItem.CommandText = @"
                INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion)
                VALUES (@PedidoId, @ServicioId, @Cantidad, @PrecioUnit, @Total, @Descripcion);";
            cmdItem.AddParam("@PedidoId", pedidoId);
            cmdItem.AddParam("@ServicioId", item.ServicioId);
            cmdItem.AddParam("@Cantidad", item.Cantidad);
            cmdItem.AddParam("@PrecioUnit", item.PrecioUnit);
            cmdItem.AddParam("@Total", item.Total);
            cmdItem.AddParam("@Descripcion", item.Descripcion);
            await cmdItem.ExecuteNonQueryAsync(ct);

            await using var cmdRecalc = conn.CreateCommand();
            cmdRecalc.Transaction = tx;
            // Preserva Descuento y RecargoUrgente (ya fijados al crear el pedido) y vuelve a
            // aplicar el redondeo a 10 centimos sobre el nuevo total, igual que PedidoService.CrearAsync.
            // La version anterior perdia el recargo urgente y el redondeo al recalcular (bug).
            cmdRecalc.CommandText = @"
                ;WITH Calc AS (
                    SELECT p.Id, t.SumaTotal,
                           (t.SumaTotal - p.Descuento + p.RecargoUrgente) AS TotalSinRedondear
                    FROM dbo.Pedido p
                    JOIN (SELECT PedidoId, SUM(Total) AS SumaTotal
                            FROM dbo.PedidoItem
                           WHERE PedidoId = @PedidoId
                          GROUP BY PedidoId) t ON t.PedidoId = p.Id
                    WHERE p.Id = @PedidoId AND p.SedeId = @SedeId
                )
                UPDATE p
                   SET Subtotal = c.SumaTotal,
                       Total = ROUND(c.TotalSinRedondear * 10, 0) / 10.0,
                       Redondeo = ROUND(c.TotalSinRedondear * 10, 0) / 10.0 - c.TotalSinRedondear,
                       EstadoPago = CASE
                                      WHEN p.MontoPagado >= ROUND(c.TotalSinRedondear * 10, 0) / 10.0 THEN 'PAGADO'
                                      WHEN p.MontoPagado > 0 THEN 'PARCIAL'
                                      ELSE 'PENDIENTE'
                                    END
                  FROM dbo.Pedido p
                  JOIN Calc c ON c.Id = p.Id";
            cmdRecalc.AddParam("@PedidoId", pedidoId);
            cmdRecalc.AddParam("@SedeId", sedeId);
            await cmdRecalc.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task ActualizarFechaEntregaAsync(int pedidoId, DateTime nuevaFecha, int? usuarioId, string? motivo, int sedeId, string actorTipo, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE dbo.Pedido
                   SET FechaEntregaEst = @Fecha
                 WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0
                   AND EstadoProceso NOT IN ('ENTREGADO', 'ANULADO', 'DONADO')";
            cmd.AddParam("@Fecha", nuevaFecha);
            cmd.AddParam("@Id", pedidoId);
            cmd.AddParam("@SedeId", sedeId);
            var filas = await cmd.ExecuteNonQueryAsync(ct);
            if (filas == 0) throw new InvalidOperationException("El pedido no existe, está anulado o ya se encuentra finalizado.");

            var nota = string.IsNullOrWhiteSpace(motivo)
                ? $"Fecha entrega actualizada a {nuevaFecha:dd/MM/yyyy HH:mm}"
                : $"Fecha entrega -> {nuevaFecha:dd/MM/yyyy HH:mm}. Motivo: {motivo}";

            // Registrar en historial (usamos el estado actual para no cambiar el flujo)
            await using var cmdEstado = conn.CreateCommand();
            cmdEstado.Transaction = tx;
            cmdEstado.CommandText = "SELECT EstadoProceso, AreaActualId FROM dbo.Pedido WHERE Id = @Id AND SedeId = @SedeId";
            cmdEstado.AddParam("@Id", pedidoId);
            cmdEstado.AddParam("@SedeId", sedeId);
            string estado = "PENDIENTE";
            int? areaId = null;
            await using (var r = await cmdEstado.ExecuteReaderAsync(ct))
            {
                if (await r.ReadAsync(ct))
                {
                    estado = r.GetString(0);
                    if (!r.IsDBNull(1)) areaId = r.GetInt32(1);
                }
            }

            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                AreaId = areaId,
                EstadoProceso = estado,
                UsuarioId = usuarioId,
                ActorTipo = actorTipo,
                ActorDescripcion = actorTipo == "USUARIO" ? null : actorTipo,
                Fecha = DateTime.Now,
                Nota = nota
            }, conn, tx, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> ActualizarDestinoDeliveryAsync(
        int pedidoId, string direccion, string distrito, string? referencia,
        decimal? latitud, decimal? longitud, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Pedido
               SET Modalidad = 'Delivery',
                   DireccionEntrega = @Direccion,
                   DistritoEntrega = @Distrito,
                   ReferenciaEntrega = @Referencia,
                   LatitudEntrega = @Latitud,
                   LongitudEntrega = @Longitud
             WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0
               AND EstadoProceso NOT IN ('ENTREGADO', 'ANULADO', 'DONADO')";
        cmd.AddParam("@Direccion", direccion);
        cmd.AddParam("@Distrito", distrito);
        cmd.AddParam("@Referencia", referencia);
        cmd.AddParam("@Latitud", latitud);
        cmd.AddParam("@Longitud", longitud);
        cmd.AddParam("@Id", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> AsignarMotorizadoAsync(int pedidoId, int? motorizadoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Pedido
               SET MotorizadoId = @MotorizadoId
             WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0
               AND Modalidad IN ('Recojo', 'Delivery')
               AND EstadoProceso NOT IN ('ENTREGADO', 'ANULADO', 'DONADO')";
        cmd.AddParam("@MotorizadoId", motorizadoId);
        cmd.AddParam("@Id", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task AnularAsync(int pedidoId, int usuarioId, string motivo, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            // Las reglas de negocio (sin pagos, no entregado) también van en el WHERE:
            // el service las valida antes, pero un pago o una entrega concurrentes entre esa
            // validación y este UPDATE dejarían un pedido anulado con dinero cobrado (TOCTOU).
            cmd.CommandText = @"
                UPDATE dbo.Pedido
                   SET Anulado = 1,
                       EstadoProceso = 'ANULADO',
                       MotivoAnulacion = @Motivo,
                       TokenRuta = NULL,
                       TokenRutaExpiraEn = NULL
                 WHERE Id = @Id AND Anulado = 0 AND SedeId = @SedeId
                   AND EstadoProceso <> 'ENTREGADO'
                   AND MontoPagado <= 0.01";
            cmd.AddParam("@Motivo", motivo);
            cmd.AddParam("@Id", pedidoId);
            cmd.AddParam("@SedeId", sedeId);
            var filas = await cmd.ExecuteNonQueryAsync(ct);
            if (filas == 0) throw new InvalidOperationException(
                "No se pudo anular: el pedido no existe, ya está anulado, fue entregado o registró un pago mientras se procesaba.");

            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                EstadoProceso = "ANULADO",
                UsuarioId = usuarioId,
                Fecha = DateTime.Now,
                Nota = $"Anulado: {motivo}"
            }, conn, tx, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DonarAsync(int pedidoId, int usuarioId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Lee el monto pagado con bloqueo (para dejar rastro en el historial). El OUTPUT del
            // UPDATE no se puede usar aquí porque la tabla Pedido tiene triggers habilitados.
            await using var cmdLeer = conn.CreateCommand();
            cmdLeer.Transaction = tx;
            cmdLeer.CommandText = @"
                SELECT MontoPagado FROM dbo.Pedido WITH (UPDLOCK, ROWLOCK)
                 WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0 AND EstadoProceso = 'LISTO'";
            cmdLeer.AddParam("@Id", pedidoId);
            cmdLeer.AddParam("@SedeId", sedeId);
            var montoObj = await cmdLeer.ExecuteScalarAsync(ct);
            if (montoObj is null) throw new InvalidOperationException("El pedido no está en almacén (LISTO) o no existe.");
            var montoPagado = (decimal)montoObj;

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
            UPDATE dbo.Pedido SET EstadoProceso = 'DONADO', TokenRuta = NULL, TokenRutaExpiraEn = NULL
                 WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0 AND EstadoProceso = 'LISTO'";
            cmd.AddParam("@Id", pedidoId);
            cmd.AddParam("@SedeId", sedeId);
            var filas = await cmd.ExecuteNonQueryAsync(ct);
            if (filas == 0) throw new InvalidOperationException("El pedido no está en almacén (LISTO) o no existe.");

            var nota = montoPagado > 0.01m
                ? $"Enviado a donación por tiempo en custodia. El cliente había abonado S/ {montoPagado:F2} antes de abandonar el pedido."
                : "Enviado a donación por tiempo en custodia";

            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                EstadoProceso = "DONADO",
                UsuarioId = usuarioId,
                Fecha = DateTime.Now,
                Nota = nota
            }, conn, tx, ct);
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task ReenviarAlmacenAsync(int pedidoId, int usuarioId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            // Mueve un pedido pendiente/en proceso directo a almacén (LISTO para recojo).
            cmd.CommandText = @"
                UPDATE dbo.Pedido SET EstadoProceso = 'LISTO', AreaActualId = NULL
                 WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0 AND EstadoProceso IN ('PENDIENTE','EN_PROCESO')";
            cmd.AddParam("@Id", pedidoId);
            cmd.AddParam("@SedeId", sedeId);
            var filas = await cmd.ExecuteNonQueryAsync(ct);
            if (filas == 0) throw new InvalidOperationException("El pedido no está pendiente/en proceso o no existe.");

            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                EstadoProceso = "LISTO",
                UsuarioId = usuarioId,
                Fecha = DateTime.Now,
                Nota = "Reenviado a almacén (listo para recojo)"
            }, conn, tx, ct);
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    private static Pedido MapPedido(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Numero = r.GetInt32(r.GetOrdinal("Numero")),
        ClienteId = r.GetInt32(r.GetOrdinal("ClienteId")),
        ClienteNombre = r.GetNullableString("ClienteNombre"),
        ClienteCelular = r.GetNullableString("ClienteCelular"),
        ClienteDni = r.GetNullableString("ClienteDni"),
        ClientePuntos = r.GetInt32(r.GetOrdinal("ClientePuntos")),
        UsuarioId = r.GetInt32(r.GetOrdinal("UsuarioId")),
        UsuarioNombre = r.GetNullableString("UsuarioNombre"),
        FechaIngreso = r.GetDateTime(r.GetOrdinal("FechaIngreso")),
        FechaEntregaEst = r.GetNullableDateTime("FechaEntregaEst"),
        Modalidad = r.GetString(r.GetOrdinal("Modalidad")),
        DireccionEntrega = r.GetNullableString("DireccionEntrega"),
        DistritoEntrega = r.GetNullableString("DistritoEntrega"),
        ReferenciaEntrega = r.GetNullableString("ReferenciaEntrega"),
        LatitudEntrega = r.GetNullableDecimal("LatitudEntrega"),
        LongitudEntrega = r.GetNullableDecimal("LongitudEntrega"),
        Subtotal = r.GetDecimal(r.GetOrdinal("Subtotal")),
        Descuento = r.GetDecimal(r.GetOrdinal("Descuento")),
        EsUrgente = r.GetBoolean(r.GetOrdinal("EsUrgente")),
        RecargoUrgente = r.GetDecimal(r.GetOrdinal("RecargoUrgente")),
        Redondeo = r.GetDecimal(r.GetOrdinal("Redondeo")),
        Total = r.GetDecimal(r.GetOrdinal("Total")),
        MontoPagado = r.GetDecimal(r.GetOrdinal("MontoPagado")),
        EstadoPago = r.GetString(r.GetOrdinal("EstadoPago")),
        EstadoProceso = r.GetString(r.GetOrdinal("EstadoProceso")),
        AreaActualId = r.GetNullableInt("AreaActualId"),
        AreaActualNombre = r.GetNullableString("AreaActualNombre"),
        Observaciones = r.GetNullableString("Observaciones"),
        FechaEntregaReal = r.GetNullableDateTime("FechaEntregaReal"),
        Anulado = r.GetBoolean(r.GetOrdinal("Anulado")),
        MotivoAnulacion = r.GetNullableString("MotivoAnulacion"),
        CodigoAntiguo = r.GetNullableString("CodigoAntiguo")
    };

    private static PedidoItem MapItem(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
        ServicioId = r.GetInt32(r.GetOrdinal("ServicioId")),
        ServicioNombre = r.GetNullableString("ServicioNombre"),
        ServicioUnidad = r.GetNullableString("ServicioUnidad"),
        Cantidad = r.GetDecimal(r.GetOrdinal("Cantidad")),
        PrecioUnit = r.GetDecimal(r.GetOrdinal("PrecioUnit")),
        Total = r.GetDecimal(r.GetOrdinal("Total")),
        Descripcion = r.GetNullableString("Descripcion"),
        CantidadEntregada = r.GetDecimal(r.GetOrdinal("CantidadEntregada"))
    };
}
