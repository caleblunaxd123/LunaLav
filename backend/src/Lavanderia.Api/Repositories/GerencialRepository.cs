using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

/// <summary>
/// Analitica "gerencial": tablero de SLA/cuellos de botella (basado en PedidoHistorial) y la
/// vista unificada que junta caja + inventario + facturacion + cobranza en una sola pantalla.
/// Vive separado de IReporteRepository porque devuelve DTOs tipados para un dashboard visual,
/// no el formato generico de tabla (Columnas/Filas) que usan los reportes exportables.
/// </summary>
public interface IGerencialRepository
{
    Task<TableroSlaDto> ObtenerTableroSlaAsync(int sedeId, DateTime desde, DateTime hasta, CancellationToken ct = default);
    Task<VistaGerencialDto> ObtenerVistaGerencialAsync(int negocioId, int sedeId, CancellationToken ct = default);
    Task<DashboardExtrasDto> ObtenerDashboardExtrasAsync(int negocioId, int sedeId, CancellationToken ct = default);
    Task<List<ConsolidadoSedeDto>> ObtenerConsolidadoAsync(int negocioId, CancellationToken ct = default);
}

public class GerencialRepository : IGerencialRepository
{
    private readonly ISqlConnectionFactory _factory;
    public GerencialRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<TableroSlaDto> ObtenerTableroSlaAsync(int sedeId, DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        var resultado = new TableroSlaDto();

        // Tiempo promedio real por area: para cada fila de PedidoHistorial se mide contra la
        // fila SIGUIENTE del mismo pedido (LEAD) — esa diferencia de tiempo es lo que el pedido
        // realmente estuvo en esa area/estado. Se compara contra AreaLavado.TiempoEstMinutos.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                ;WITH Transiciones AS (
                    SELECT h.AreaId,
                           DATEDIFF(MINUTE, h.Fecha, LEAD(h.Fecha) OVER (PARTITION BY h.PedidoId ORDER BY h.Fecha)) AS MinutosReal
                    FROM dbo.PedidoHistorial h
                    INNER JOIN dbo.Pedido p ON p.Id = h.PedidoId
                    WHERE p.SedeId = @SedeId AND p.FechaIngreso >= @Desde AND p.FechaIngreso < @Hasta AND p.Anulado = 0
                )
                SELECT a.Id AS AreaId, a.Nombre AS AreaNombre, a.Orden, a.TiempoEstMinutos,
                       AVG(CAST(t.MinutosReal AS FLOAT)) AS MinutosPromedioReal,
                       COUNT(*) AS PedidosProcesados
                FROM Transiciones t
                INNER JOIN dbo.AreaLavado a ON a.Id = t.AreaId
                WHERE t.MinutosReal IS NOT NULL
                GROUP BY a.Id, a.Nombre, a.Orden, a.TiempoEstMinutos
                ORDER BY a.Orden";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Desde", desde.Date);
            cmd.AddParam("@Hasta", hasta.Date.AddDays(1));
            resultado.Areas = await cmd.ReadListAsync(r => new SlaAreaDto(
                r.GetInt32(r.GetOrdinal("AreaId")),
                r.GetString(r.GetOrdinal("AreaNombre")),
                r.GetInt32(r.GetOrdinal("Orden")),
                r.GetInt32(r.GetOrdinal("TiempoEstMinutos")),
                r.GetDouble(r.GetOrdinal("MinutosPromedioReal")),
                r.GetInt32(r.GetOrdinal("PedidosProcesados"))
            ), ct);
        }

        // Pedidos "estancados" ahora mismo: llevan mas de 1.5x el tiempo estimado de su area
        // actual sin haber avanzado. El factor 1.5 es deliberadamente generoso (una demora
        // corta es normal) para que la alerta solo suene ante un cuello de botella real.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                ;WITH UltimoCambio AS (
                    SELECT PedidoId, MAX(Fecha) AS FechaUltimoCambio
                    FROM dbo.PedidoHistorial
                    GROUP BY PedidoId
                )
                SELECT p.Id AS PedidoId, p.Numero, c.Nombre AS ClienteNombre, a.Id AS AreaId, a.Nombre AS AreaNombre,
                       DATEDIFF(MINUTE, u.FechaUltimoCambio, SYSDATETIME()) AS MinutosEnArea,
                       a.TiempoEstMinutos
                FROM dbo.Pedido p
                INNER JOIN UltimoCambio u ON u.PedidoId = p.Id
                INNER JOIN dbo.AreaLavado a ON a.Id = p.AreaActualId
                INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
                WHERE p.SedeId = @SedeId AND p.EstadoProceso = 'EN_PROCESO' AND p.Anulado = 0
                  AND DATEDIFF(MINUTE, u.FechaUltimoCambio, SYSDATETIME()) > a.TiempoEstMinutos * 1.5
                ORDER BY MinutosEnArea DESC";
            cmd.AddParam("@SedeId", sedeId);
            resultado.Estancados = await cmd.ReadListAsync(r => new PedidoEstancadoDto(
                r.GetInt32(r.GetOrdinal("PedidoId")),
                r.GetInt32(r.GetOrdinal("Numero")),
                r.GetString(r.GetOrdinal("ClienteNombre")),
                r.GetInt32(r.GetOrdinal("AreaId")),
                r.GetString(r.GetOrdinal("AreaNombre")),
                r.GetInt32(r.GetOrdinal("MinutosEnArea")),
                r.GetInt32(r.GetOrdinal("TiempoEstMinutos"))
            ), ct);
        }

        return resultado;
    }

    public async Task<VistaGerencialDto> ObtenerVistaGerencialAsync(int negocioId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        var hoy = DateTime.Today;
        var inicioSemana = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        var inicioMesAnt = inicioMes.AddMonths(-1);
        var diaDelMes = hoy.Day; // para comparar "mes anterior hasta el mismo día"
        var inicio14 = hoy.AddDays(-13);
        var dto = new VistaGerencialDto();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    ISNULL(SUM(CASE WHEN CAST(FechaIngreso AS DATE) = @Hoy THEN Total ELSE 0 END), 0) AS VentasHoy,
                    ISNULL(SUM(CASE WHEN FechaIngreso >= @InicioMes THEN Total ELSE 0 END), 0) AS VentasMes,
                    COUNT(CASE WHEN FechaIngreso >= @InicioMes THEN 1 END) AS PedidosMesCount,
                    ISNULL(SUM(CASE WHEN FechaIngreso >= @InicioMesAnt AND FechaIngreso < @InicioMes THEN Total ELSE 0 END), 0) AS VentasMesAnt,
                    ISNULL(SUM(CASE WHEN FechaIngreso >= @InicioMesAnt AND FechaIngreso < @CorteMesAnt THEN Total ELSE 0 END), 0) AS VentasMesAntAlDia,
                    ISNULL(SUM(Total - MontoPagado), 0) AS SaldoPorCobrar,
                    COUNT(CASE WHEN EstadoProceso = 'PENDIENTE' THEN 1 END) AS PedidosPendientes,
                    COUNT(CASE WHEN EstadoProceso = 'EN_PROCESO' THEN 1 END) AS PedidosEnProceso,
                    COUNT(CASE WHEN EstadoProceso IN ('PENDIENTE', 'EN_PROCESO') THEN 1 END) AS PedidosActivos,
                    COUNT(CASE WHEN EstadoProceso = 'LISTO' THEN 1 END) AS PedidosListos
                FROM dbo.Pedido
                WHERE SedeId = @SedeId AND Anulado = 0";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Hoy", hoy);
            cmd.AddParam("@InicioMes", inicioMes);
            cmd.AddParam("@InicioMesAnt", inicioMesAnt);
            // corte = mismo día del mes anterior (acota para no pasarnos de su fin de mes)
            cmd.AddParam("@CorteMesAnt", inicioMesAnt.AddDays(Math.Min(diaDelMes, DateTime.DaysInMonth(inicioMesAnt.Year, inicioMesAnt.Month))));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.VentasHoy = r.GetDecimal(r.GetOrdinal("VentasHoy"));
                dto.VentasMes = r.GetDecimal(r.GetOrdinal("VentasMes"));
                dto.PedidosMesCount = r.GetInt32(r.GetOrdinal("PedidosMesCount"));
                dto.VentasMesAnterior = r.GetDecimal(r.GetOrdinal("VentasMesAnt"));
                dto.VentasMesAnteriorAlDia = r.GetDecimal(r.GetOrdinal("VentasMesAntAlDia"));
                dto.SaldoPorCobrar = r.GetDecimal(r.GetOrdinal("SaldoPorCobrar"));
                dto.PedidosPendientes = r.GetInt32(r.GetOrdinal("PedidosPendientes"));
                dto.PedidosEnProceso = r.GetInt32(r.GetOrdinal("PedidosEnProceso"));
                dto.PedidosActivos = r.GetInt32(r.GetOrdinal("PedidosActivos"));
                dto.PedidosListosSinRecoger = r.GetInt32(r.GetOrdinal("PedidosListos"));
            }
            dto.TicketPromedioMes = dto.PedidosMesCount > 0 ? Math.Round(dto.VentasMes / dto.PedidosMesCount, 2) : 0;
        }

        // Tendencia de ventas de los últimos 14 días (por día de ingreso). Se rellenan con 0
        // los días sin ventas para que la barra/serie quede continua en el frontend.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT CAST(FechaIngreso AS DATE) AS Dia, ISNULL(SUM(Total), 0) AS Total
                FROM dbo.Pedido
                WHERE SedeId = @SedeId AND Anulado = 0 AND CAST(FechaIngreso AS DATE) >= @Inicio14
                GROUP BY CAST(FechaIngreso AS DATE)";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Inicio14", inicio14);
            var porDia = new Dictionary<DateTime, decimal>();
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    porDia[r.GetDateTime(r.GetOrdinal("Dia")).Date] = r.GetDecimal(r.GetOrdinal("Total"));
            }
            for (var d = inicio14; d <= hoy; d = d.AddDays(1))
                dto.VentasUltimos14Dias.Add(new PuntoTendenciaDto(d.ToString("yyyy-MM-dd"),
                    porDia.TryGetValue(d, out var v) ? v : 0m));
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(CASE WHEN CAST(p.FechaEntregaReal AS DATE) = @Hoy THEN 1 END) AS PedidosHoy,
                    COUNT(CASE WHEN CAST(p.FechaEntregaReal AS DATE) = @Hoy AND p.Modalidad <> 'Delivery' THEN 1 END) AS PedidosTiendaHoy,
                    COUNT(CASE WHEN CAST(p.FechaEntregaReal AS DATE) = @Hoy AND p.Modalidad = 'Delivery' THEN 1 END) AS PedidosDomicilioHoy,
                    COUNT(CASE WHEN p.FechaEntregaReal >= @InicioSemana THEN 1 END) AS PedidosSemana,
                    COUNT(CASE WHEN p.FechaEntregaReal >= @InicioMes THEN 1 END) AS PedidosMes
                FROM dbo.Pedido p
                WHERE p.SedeId = @SedeId AND p.Anulado = 0 AND p.EstadoProceso = 'ENTREGADO'
                  AND p.FechaEntregaReal IS NOT NULL";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Hoy", hoy);
            cmd.AddParam("@InicioSemana", inicioSemana);
            cmd.AddParam("@InicioMes", inicioMes);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.PedidosEntregadosHoy = r.GetInt32(r.GetOrdinal("PedidosHoy"));
                dto.PedidosEntregadosTiendaHoy = r.GetInt32(r.GetOrdinal("PedidosTiendaHoy"));
                dto.PedidosEntregadosDomicilioHoy = r.GetInt32(r.GetOrdinal("PedidosDomicilioHoy"));
                dto.PedidosEntregadosSemana = r.GetInt32(r.GetOrdinal("PedidosSemana"));
                dto.PedidosEntregadosMes = r.GetInt32(r.GetOrdinal("PedidosMes"));
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    ISNULL(SUM(CASE WHEN Tipo = 'GASTO' AND Fecha >= @InicioMes THEN Monto ELSE 0 END), 0) AS GastosMes,
                    ISNULL(SUM(CASE WHEN Tipo = 'INGRESO' AND CAST(Fecha AS DATE) = @Hoy THEN Monto ELSE 0 END), 0) AS CobradoHoy,
                    ISNULL(SUM(CASE WHEN Tipo = 'INGRESO' AND MetodoPago = 'EFECTIVO' AND CAST(Fecha AS DATE) = @Hoy THEN Monto ELSE 0 END), 0)
                        - ISNULL(SUM(CASE WHEN Tipo = 'GASTO' AND MetodoPago = 'EFECTIVO' AND CAST(Fecha AS DATE) = @Hoy THEN Monto ELSE 0 END), 0)
                        AS CajaEsperadaHoy,
                    ISNULL(SUM(CASE WHEN Tipo = 'INGRESO' AND MetodoPago = 'EFECTIVO' AND Fecha >= @InicioMes THEN Monto ELSE 0 END), 0) AS IngEfectivoMes,
                    ISNULL(SUM(CASE WHEN Tipo = 'INGRESO' AND MetodoPago IN ('YAPE','PLIN','TRANSFERENCIA') AND Fecha >= @InicioMes THEN Monto ELSE 0 END), 0) AS IngDigitalMes,
                    ISNULL(SUM(CASE WHEN Tipo = 'INGRESO' AND MetodoPago IN ('POS','TARJETA') AND Fecha >= @InicioMes THEN Monto ELSE 0 END), 0) AS IngTarjetaMes
                FROM dbo.MovimientoCaja
                WHERE SedeId = @SedeId";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Hoy", hoy);
            cmd.AddParam("@InicioMes", inicioMes);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.GastosMes = r.GetDecimal(r.GetOrdinal("GastosMes"));
                dto.CobradoHoy = r.GetDecimal(r.GetOrdinal("CobradoHoy"));
                dto.CajaEsperadaHoy = r.GetDecimal(r.GetOrdinal("CajaEsperadaHoy"));
                dto.IngresosEfectivoMes = r.GetDecimal(r.GetOrdinal("IngEfectivoMes"));
                dto.IngresosDigitalMes = r.GetDecimal(r.GetOrdinal("IngDigitalMes"));
                dto.IngresosTarjetaMes = r.GetDecimal(r.GetOrdinal("IngTarjetaMes"));
            }
        }
        dto.UtilidadMes = dto.VentasMes - dto.GastosMes;

        // Top 5 servicios por facturación del mes (qué es lo que más deja dinero al negocio).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT TOP 5 s.Nombre AS Nombre, SUM(i.Cantidad) AS Cantidad, SUM(i.Total) AS Total
                FROM dbo.PedidoItem i
                INNER JOIN dbo.Pedido p ON p.Id = i.PedidoId
                INNER JOIN dbo.Servicio s ON s.Id = i.ServicioId
                WHERE p.SedeId = @SedeId AND p.Anulado = 0 AND p.FechaIngreso >= @InicioMes
                GROUP BY s.Nombre
                ORDER BY SUM(i.Total) DESC";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@InicioMes", inicioMes);
            dto.TopServiciosMes = await cmd.ReadListAsync(r => new TopServicioGerencialDto(
                r.GetString(r.GetOrdinal("Nombre")),
                r.GetDecimal(r.GetOrdinal("Cantidad")),
                r.GetDecimal(r.GetOrdinal("Total"))
            ), ct);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(CASE WHEN Estado = 'PENDIENTE' THEN 1 END) AS Pendientes,
                    COUNT(CASE WHEN Estado IN ('RECHAZADO', 'ERROR') THEN 1 END) AS Rechazados
                FROM dbo.ComprobanteElectronico
                WHERE SedeId = @SedeId";
            cmd.AddParam("@SedeId", sedeId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.ComprobantesPendientes = r.GetInt32(r.GetOrdinal("Pendientes"));
                dto.ComprobantesRechazados = r.GetInt32(r.GetOrdinal("Rechazados"));
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(1) FROM dbo.Insumo WHERE SedeId = @SedeId AND Activo = 1 AND StockActual <= StockMinimo";
            cmd.AddParam("@SedeId", sedeId);
            dto.InsumosBajoStock = await cmd.ReadScalarAsync<int>(ct);
        }

        return dto;
    }

    /// <summary>Piezas visuales del dashboard: comparativos hoy/ayer y mes/mes anterior, total de
    /// clientes, la serie de ventas de la semana (lun→dom) y las últimas órdenes ingresadas.</summary>
    public async Task<DashboardExtrasDto> ObtenerDashboardExtrasAsync(int negocioId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        var hoy = DateTime.Today;
        var ayer = hoy.AddDays(-1);
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        var inicioMesAnt = inicioMes.AddMonths(-1);
        var inicioSemana = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7)); // lunes de esta semana
        var dto = new DashboardExtrasDto();

        // Órdenes hoy / ayer + ventas de ayer (para los "▲% vs ayer")
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(CASE WHEN CAST(FechaIngreso AS DATE) = @Hoy THEN 1 END) AS OrdenesHoy,
                    COUNT(CASE WHEN CAST(FechaIngreso AS DATE) = @Ayer THEN 1 END) AS OrdenesAyer,
                    ISNULL(SUM(CASE WHEN CAST(FechaIngreso AS DATE) = @Ayer THEN Total ELSE 0 END), 0) AS VentasAyer
                FROM dbo.Pedido
                WHERE SedeId = @SedeId AND Anulado = 0 AND CAST(FechaIngreso AS DATE) >= @Ayer";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Hoy", hoy);
            cmd.AddParam("@Ayer", ayer);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.OrdenesHoy = r.GetInt32(r.GetOrdinal("OrdenesHoy"));
                dto.OrdenesAyer = r.GetInt32(r.GetOrdinal("OrdenesAyer"));
                dto.VentasAyer = r.GetDecimal(r.GetOrdinal("VentasAyer"));
            }
        }

        // Clientes del negocio (compartidos entre sedes) + altas del mes vs mes anterior
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(*) AS Total,
                    COUNT(CASE WHEN FechaCreacion >= @InicioMes THEN 1 END) AS NuevosMes,
                    COUNT(CASE WHEN FechaCreacion >= @InicioMesAnt AND FechaCreacion < @InicioMes THEN 1 END) AS NuevosMesAnt
                FROM dbo.Cliente
                WHERE NegocioId = @NegocioId AND Activo = 1";
            cmd.AddParam("@NegocioId", negocioId);
            cmd.AddParam("@InicioMes", inicioMes);
            cmd.AddParam("@InicioMesAnt", inicioMesAnt);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.TotalClientes = r.GetInt32(r.GetOrdinal("Total"));
                dto.ClientesNuevosMes = r.GetInt32(r.GetOrdinal("NuevosMes"));
                dto.ClientesNuevosMesAnterior = r.GetInt32(r.GetOrdinal("NuevosMesAnt"));
            }
        }

        // Serie de ventas de la semana (lunes→domingo), rellenando días sin ventas con 0
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT CAST(FechaIngreso AS DATE) AS Dia, ISNULL(SUM(Total), 0) AS Total
                FROM dbo.Pedido
                WHERE SedeId = @SedeId AND Anulado = 0 AND CAST(FechaIngreso AS DATE) >= @InicioSemana
                GROUP BY CAST(FechaIngreso AS DATE)";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@InicioSemana", inicioSemana);
            var porDia = new Dictionary<DateTime, decimal>();
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    porDia[r.GetDateTime(r.GetOrdinal("Dia")).Date] = r.GetDecimal(r.GetOrdinal("Total"));
            }
            for (var i = 0; i < 7; i++)
            {
                var d = inicioSemana.AddDays(i);
                dto.VentasSemana.Add(new PuntoTendenciaDto(d.ToString("yyyy-MM-dd"),
                    porDia.TryGetValue(d, out var v) ? v : 0m));
            }
        }

        // Últimas 5 órdenes ingresadas, con su servicio principal (el ítem de mayor monto)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT TOP 5 p.Numero, c.Nombre AS ClienteNombre, p.EstadoProceso, p.Total,
                    ISNULL((SELECT TOP 1 s.Nombre FROM dbo.PedidoItem i
                            INNER JOIN dbo.Servicio s ON s.Id = i.ServicioId
                            WHERE i.PedidoId = p.Id ORDER BY i.Total DESC), '—') AS ServicioPrincipal
                FROM dbo.Pedido p
                INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
                WHERE p.SedeId = @SedeId AND p.Anulado = 0
                ORDER BY p.FechaIngreso DESC";
            cmd.AddParam("@SedeId", sedeId);
            dto.OrdenesRecientes = await cmd.ReadListAsync(r => new OrdenRecienteDto(
                r.GetInt32(r.GetOrdinal("Numero")),
                r.GetString(r.GetOrdinal("ClienteNombre")),
                r.GetString(r.GetOrdinal("ServicioPrincipal")),
                r.GetString(r.GetOrdinal("EstadoProceso")),
                r.GetDecimal(r.GetOrdinal("Total"))
            ), ct);
        }

        return dto;
    }

    /// <summary>KPIs por sede de todo el negocio, para que el dueño con varias sucursales vea el
    /// panorama junto (una fila por sede activa; el total lo arma el frontend).</summary>
    public async Task<List<ConsolidadoSedeDto>> ObtenerConsolidadoAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        var hoy = DateTime.Today;
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.Id AS SedeId, s.Nombre AS SedeNombre,
                ISNULL(SUM(CASE WHEN p.Anulado = 0 AND CAST(p.FechaIngreso AS DATE) = @Hoy THEN p.Total ELSE 0 END), 0) AS VentasHoy,
                ISNULL(SUM(CASE WHEN p.Anulado = 0 AND p.FechaIngreso >= @InicioMes THEN p.Total ELSE 0 END), 0) AS VentasMes,
                ISNULL(SUM(CASE WHEN p.Anulado = 0 AND p.Total > p.MontoPagado THEN p.Total - p.MontoPagado ELSE 0 END), 0) AS SaldoPorCobrar,
                COUNT(CASE WHEN p.Anulado = 0 AND p.EstadoProceso IN ('PENDIENTE','EN_PROCESO') THEN 1 END) AS PedidosActivos,
                COUNT(CASE WHEN p.Anulado = 0 AND p.EstadoProceso = 'LISTO' THEN 1 END) AS PedidosListos
            FROM dbo.Sede s
            LEFT JOIN dbo.Pedido p ON p.SedeId = s.Id
            WHERE s.NegocioId = @NegocioId AND s.Activo = 1
            GROUP BY s.Id, s.Nombre
            ORDER BY s.Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Hoy", hoy);
        cmd.AddParam("@InicioMes", inicioMes);
        return await cmd.ReadListAsync(r => new ConsolidadoSedeDto(
            r.GetInt32(r.GetOrdinal("SedeId")),
            r.GetString(r.GetOrdinal("SedeNombre")),
            r.GetDecimal(r.GetOrdinal("VentasHoy")),
            r.GetDecimal(r.GetOrdinal("VentasMes")),
            r.GetDecimal(r.GetOrdinal("SaldoPorCobrar")),
            r.GetInt32(r.GetOrdinal("PedidosActivos")),
            r.GetInt32(r.GetOrdinal("PedidosListos"))
        ), ct);
    }
}
