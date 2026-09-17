using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public interface IReporteRepository
{
    Task<ReporteResultDto> OrdenesPendientesAsync(int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> GastosAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> GeneralAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> ServiciosAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> CuadresCajaAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
    Task<CuadresDiariosReporteDto> CuadresDiariosAsync(int anio, int mes, int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> OrdenesMensualAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> AlmacenAsync(int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> AnuladosAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> RegistroEntregasAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> PagosAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
    Task<ReporteResultDto> DescuentoDirectoAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
}

public class ReporteRepository : IReporteRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ReporteRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static async Task<ReporteResultDto> Ejecutar(
        SqlCommand cmd, List<string> columnas, Func<SqlDataReader, Dictionary<string, string>> mapFila, CancellationToken ct)
    {
        var result = new ReporteResultDto { Columnas = columnas };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Filas.Add(mapFila(reader));
        return result;
    }

    private static string Fecha(SqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? "" : r.GetDateTime(r.GetOrdinal(col)).ToString("dd/MM/yyyy HH:mm");

    private static string Soles(SqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? "S/ 0.00" : $"S/ {r.GetDecimal(r.GetOrdinal(col)):F2}";

    private static string Texto(SqlDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? "—" : r.GetValue(r.GetOrdinal(col)).ToString() ?? "—";

    public async Task<ReporteResultDto> OrdenesPendientesAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id AS PedidoId, p.Numero, c.Nombre AS Cliente, c.Celular, p.FechaIngreso, a.Nombre AS AreaActual,
                   p.EstadoProceso, p.Total, p.MontoPagado,
                   DATEDIFF(DAY, p.FechaIngreso, SYSDATETIME()) AS DiasEnProceso
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN dbo.AreaLavado a ON a.Id = p.AreaActualId
            WHERE p.EstadoProceso IN ('PENDIENTE','EN_PROCESO') AND p.Anulado = 0 AND p.SedeId = @SedeId
            ORDER BY p.FechaIngreso";
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "N°", "Cliente", "Celular", "Ingreso", "Área actual", "Estado", "Total", "Pagado", "Días en proceso" };
        var res = await Ejecutar(cmd, columnas, r => new Dictionary<string, string>
        {
            ["_id"] = Texto(r, "PedidoId"),
            ["N°"] = "#" + Texto(r, "Numero"),
            ["Cliente"] = Texto(r, "Cliente"),
            ["Celular"] = Texto(r, "Celular"),
            ["Ingreso"] = Fecha(r, "FechaIngreso"),
            ["Área actual"] = Texto(r, "AreaActual"),
            ["Estado"] = Texto(r, "EstadoProceso"),
            ["Total"] = Soles(r, "Total"),
            ["Pagado"] = Soles(r, "MontoPagado"),
            ["Días en proceso"] = Texto(r, "DiasEnProceso"),
        }, ct);
        res.Accion = "reenviar-almacen";
        return res;
    }

    public async Task<ReporteResultDto> GastosAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ISNULL(tg.Nombre, 'Otros') AS Tipo, COUNT(1) AS Cantidad, SUM(m.Monto) AS Total
            FROM dbo.MovimientoCaja m
            LEFT JOIN dbo.TipoGasto tg ON tg.Id = m.TipoGastoId
            WHERE m.Tipo = 'GASTO' AND CAST(m.Fecha AS DATE) BETWEEN @Desde AND @Hasta AND m.SedeId = @SedeId
            GROUP BY tg.Nombre
            ORDER BY SUM(m.Monto) DESC";
        cmd.AddParam("@Desde", desde.Date);
        cmd.AddParam("@Hasta", hasta.Date);
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "Tipo de gasto", "Cantidad de movimientos", "Monto total" };
        return await Ejecutar(cmd, columnas, r => new Dictionary<string, string>
        {
            ["Tipo de gasto"] = Texto(r, "Tipo"),
            ["Cantidad de movimientos"] = Texto(r, "Cantidad"),
            ["Monto total"] = Soles(r, "Total"),
        }, ct);
    }

    public async Task<ReporteResultDto> GeneralAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CAST(m.Fecha AS DATE) AS Dia,
                   SUM(CASE WHEN m.Tipo = 'INGRESO' THEN m.Monto ELSE 0 END) AS Ingresos,
                   SUM(CASE WHEN m.Tipo = 'GASTO' THEN m.Monto ELSE 0 END) AS Gastos
            FROM dbo.MovimientoCaja m
            WHERE CAST(m.Fecha AS DATE) BETWEEN @Desde AND @Hasta AND m.SedeId = @SedeId
            GROUP BY CAST(m.Fecha AS DATE)
            ORDER BY CAST(m.Fecha AS DATE) DESC";
        cmd.AddParam("@Desde", desde.Date);
        cmd.AddParam("@Hasta", hasta.Date);
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "Día", "Ingresos", "Gastos", "Utilidad neta" };
        return await Ejecutar(cmd, columnas, r =>
        {
            var ingresos = r.GetDecimal(r.GetOrdinal("Ingresos"));
            var gastos = r.GetDecimal(r.GetOrdinal("Gastos"));
            return new Dictionary<string, string>
            {
                ["Día"] = r.GetDateTime(r.GetOrdinal("Dia")).ToString("dd/MM/yyyy"),
                ["Ingresos"] = $"S/ {ingresos:F2}",
                ["Gastos"] = $"S/ {gastos:F2}",
                ["Utilidad neta"] = $"S/ {(ingresos - gastos):F2}",
            };
        }, ct);
    }

    public async Task<ReporteResultDto> ServiciosAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.Nombre, SUM(i.Cantidad) AS CantidadVendida, SUM(i.Total) AS IngresoTotal,
                   CASE WHEN SUM(i.Cantidad) > 0 THEN SUM(i.Total) / SUM(i.Cantidad) ELSE 0 END AS PrecioPromedio,
                   s.Costo AS CostoUnit,
                   s.Costo * SUM(i.Cantidad) AS CostoTotal,
                   SUM(i.Total) - s.Costo * SUM(i.Cantidad) AS Margen,
                   CASE WHEN SUM(i.Total) > 0 THEN (SUM(i.Total) - s.Costo * SUM(i.Cantidad)) / SUM(i.Total) * 100 ELSE 0 END AS MargenPct
            FROM dbo.PedidoItem i
            INNER JOIN dbo.Servicio s ON s.Id = i.ServicioId
            INNER JOIN dbo.Pedido p ON p.Id = i.PedidoId
            WHERE p.Anulado = 0 AND CAST(p.FechaIngreso AS DATE) BETWEEN @Desde AND @Hasta AND p.SedeId = @SedeId
            GROUP BY s.Nombre, s.Costo
            ORDER BY SUM(i.Total) DESC";
        cmd.AddParam("@Desde", desde.Date);
        cmd.AddParam("@Hasta", hasta.Date);
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "Servicio", "Cantidad vendida", "Precio promedio", "Ingreso generado", "Costo unit.", "Costo total", "Margen", "Margen %" };
        return await Ejecutar(cmd, columnas, r => new Dictionary<string, string>
        {
            ["Servicio"] = Texto(r, "Nombre"),
            ["Cantidad vendida"] = r.GetDecimal(r.GetOrdinal("CantidadVendida")).ToString("0.##"),
            ["Precio promedio"] = Soles(r, "PrecioPromedio"),
            ["Ingreso generado"] = Soles(r, "IngresoTotal"),
            ["Costo unit."] = Soles(r, "CostoUnit"),
            ["Costo total"] = Soles(r, "CostoTotal"),
            ["Margen"] = Soles(r, "Margen"),
            ["Margen %"] = r.GetDecimal(r.GetOrdinal("MargenPct")).ToString("0.#") + "%",
        }, ct);
    }

    public async Task<ReporteResultDto> CuadresCajaAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.Fecha, u.NombreCompleto AS Responsable, c.TotalContado, c.CajaFinal, c.Diferencia
            FROM dbo.CuadreCaja c
            INNER JOIN dbo.Usuario u ON u.Id = c.UsuarioId
            WHERE c.Fecha BETWEEN @Desde AND @Hasta AND c.SedeId = @SedeId
            ORDER BY c.Fecha DESC";
        cmd.AddParam("@Desde", desde.Date);
        cmd.AddParam("@Hasta", hasta.Date);
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "Fecha", "Responsable", "Total contado", "Caja final", "Diferencia", "Estado" };
        return await Ejecutar(cmd, columnas, r =>
        {
            var dif = r.GetDecimal(r.GetOrdinal("Diferencia"));
            var estado = Math.Abs(dif) < 0.01m ? "CUADRA" : (dif > 0 ? "SOBRA" : "FALTA");
            return new Dictionary<string, string>
            {
                ["Fecha"] = r.GetDateTime(r.GetOrdinal("Fecha")).ToString("dd/MM/yyyy"),
                ["Responsable"] = Texto(r, "Responsable"),
                ["Total contado"] = Soles(r, "TotalContado"),
                ["Caja final"] = Soles(r, "CajaFinal"),
                ["Diferencia"] = $"S/ {dif:F2}",
                ["Estado"] = estado,
            };
        }, ct);
    }

    public async Task<CuadresDiariosReporteDto> CuadresDiariosAsync(int anio, int mes, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        // 1) Cuadres guardados del mes (pueden ser varios por día, uno por colaborador).
        var cuadresPorDia = new Dictionary<int, List<CuadreDiarioFilaDto>>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT c.Id, c.Fecha, c.UsuarioId, u.NombreCompleto AS Usuario, c.CajaInicial, c.PedidosPagadosEfect,
                       c.Gastos, c.TotalContado, c.Corte, c.CajaFinal, c.Diferencia, c.Nota,
                       c.IngresosDigital, c.IngresosTarjeta
                FROM dbo.CuadreCaja c
                INNER JOIN dbo.Usuario u ON u.Id = c.UsuarioId
                WHERE c.SedeId = @SedeId AND YEAR(c.Fecha) = @Anio AND MONTH(c.Fecha) = @Mes
                ORDER BY c.Fecha, c.FechaCreacion";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Anio", anio);
            cmd.AddParam("@Mes", mes);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var dia = r.GetDateTime(r.GetOrdinal("Fecha")).Day;
                var dif = r.GetDecimal(r.GetOrdinal("Diferencia"));
                var estado = Math.Abs(dif) < 0.01m ? "CUADRA" : (dif > 0 ? "SOBRA" : "FALTA");
                var fila = new CuadreDiarioFilaDto(
                    r.GetInt32(r.GetOrdinal("Id")),
                    r.GetInt32(r.GetOrdinal("UsuarioId")),
                    r.GetString(r.GetOrdinal("Usuario")),
                    r.GetDecimal(r.GetOrdinal("CajaInicial")),
                    r.GetDecimal(r.GetOrdinal("PedidosPagadosEfect")),
                    r.GetDecimal(r.GetOrdinal("Gastos")),
                    r.GetDecimal(r.GetOrdinal("TotalContado")),
                    r.GetDecimal(r.GetOrdinal("Corte")),
                    r.GetDecimal(r.GetOrdinal("CajaFinal")),
                    estado,
                    Math.Abs(dif),
                    r.IsDBNull(r.GetOrdinal("Nota")) ? null : r.GetString(r.GetOrdinal("Nota")),
                    r.GetDecimal(r.GetOrdinal("IngresosDigital")),
                    r.GetDecimal(r.GetOrdinal("IngresosTarjeta")));
                if (!cuadresPorDia.TryGetValue(dia, out var lista)) { lista = new(); cuadresPorDia[dia] = lista; }
                lista.Add(fila);
            }
        }

        // 2) Totales de movimientos por día (para detectar dinero no cuadrado en días sin cuadre).
        var movPorDia = new Dictionary<int, (decimal Ing, decimal Egr)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DAY(m.Fecha) AS Dia,
                       SUM(CASE WHEN m.Tipo = 'INGRESO' THEN m.Monto ELSE 0 END) AS Ingresos,
                       SUM(CASE WHEN m.Tipo = 'GASTO' THEN m.Monto ELSE 0 END) AS Egresos
                FROM dbo.MovimientoCaja m
                WHERE m.SedeId = @SedeId AND YEAR(m.Fecha) = @Anio AND MONTH(m.Fecha) = @Mes
                GROUP BY DAY(m.Fecha)";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Anio", anio);
            cmd.AddParam("@Mes", mes);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                movPorDia[r.GetInt32(r.GetOrdinal("Dia"))] =
                    (r.GetDecimal(r.GetOrdinal("Ingresos")), r.GetDecimal(r.GetOrdinal("Egresos")));
        }

        // 2b) Ingresos por día y por MÉTODO de pago (cuántas operaciones y por cuánto).
        var formasPorDia = new Dictionary<int, List<FormaPagoDiaDto>>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DAY(m.Fecha) AS Dia, m.MetodoPago, COUNT(*) AS Cant, SUM(m.Monto) AS Monto
                FROM dbo.MovimientoCaja m
                WHERE m.SedeId = @SedeId AND YEAR(m.Fecha) = @Anio AND MONTH(m.Fecha) = @Mes AND m.Tipo = 'INGRESO'
                GROUP BY DAY(m.Fecha), m.MetodoPago";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Anio", anio);
            cmd.AddParam("@Mes", mes);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var dia = r.GetInt32(r.GetOrdinal("Dia"));
                var forma = new FormaPagoDiaDto(
                    r.GetString(r.GetOrdinal("MetodoPago")),
                    r.GetInt32(r.GetOrdinal("Cant")),
                    r.GetDecimal(r.GetOrdinal("Monto")));
                if (!formasPorDia.TryGetValue(dia, out var lista)) { lista = new(); formasPorDia[dia] = lista; }
                lista.Add(forma);
            }
        }

        // 3) Armar la lista día por día (hasta hoy si es el mes en curso).
        var hoy = DateTime.Today;
        int ultimoDia = DateTime.DaysInMonth(anio, mes);
        if (anio == hoy.Year && mes == hoy.Month) ultimoDia = Math.Min(ultimoDia, hoy.Day);

        var dias = new List<CuadreDiarioDiaDto>();
        for (int d = 1; d <= ultimoDia; d++)
        {
            var fecha = new DateOnly(anio, mes, d);
            var formas = formasPorDia.TryGetValue(d, out var f) ? f : new();
            if (cuadresPorDia.TryGetValue(d, out var cuadres) && cuadres.Count > 0)
            {
                dias.Add(new CuadreDiarioDiaDto(fecha, cuadres, false, 0, 0, formas));
            }
            else
            {
                var (ing, egr) = movPorDia.TryGetValue(d, out var m) ? m : (0m, 0m);
                dias.Add(new CuadreDiarioDiaDto(fecha, new(), true, ing, egr, formas));
            }
        }
        return new CuadresDiariosReporteDto(anio, mes, dias);
    }

    public async Task<ReporteResultDto> OrdenesMensualAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT FORMAT(p.FechaIngreso, 'yyyy-MM') AS Mes, COUNT(1) AS Cantidad,
                   SUM(p.Total) AS TotalFacturado, SUM(p.MontoPagado) AS TotalPagado
            FROM dbo.Pedido p
            WHERE p.Anulado = 0 AND CAST(p.FechaIngreso AS DATE) BETWEEN @Desde AND @Hasta AND p.SedeId = @SedeId
            GROUP BY FORMAT(p.FechaIngreso, 'yyyy-MM')
            ORDER BY FORMAT(p.FechaIngreso, 'yyyy-MM') DESC";
        cmd.AddParam("@Desde", desde.Date);
        cmd.AddParam("@Hasta", hasta.Date);
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "Mes", "N° de pedidos", "Total facturado", "Total pagado" };
        return await Ejecutar(cmd, columnas, r => new Dictionary<string, string>
        {
            ["Mes"] = Texto(r, "Mes"),
            ["N° de pedidos"] = Texto(r, "Cantidad"),
            ["Total facturado"] = Soles(r, "TotalFacturado"),
            ["Total pagado"] = Soles(r, "TotalPagado"),
        }, ct);
    }

    public async Task<ReporteResultDto> AlmacenAsync(int sedeId, CancellationToken ct = default)
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
            SELECT p.Id AS PedidoId, p.Numero, c.Nombre AS Cliente, c.Celular, ul.FechaListo, p.Total, p.MontoPagado,
                   DATEDIFF(DAY, ul.FechaListo, SYSDATETIME()) AS DiasEnCustodia
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            INNER JOIN UltimoListo ul ON ul.PedidoId = p.Id
            WHERE p.EstadoProceso = 'LISTO' AND p.Anulado = 0 AND p.SedeId = @SedeId
            ORDER BY ul.FechaListo";
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "N°", "Cliente", "Celular", "Listo desde", "Total", "Pagado", "Días en custodia" };
        var res = await Ejecutar(cmd, columnas, r => new Dictionary<string, string>
        {
            ["_id"] = Texto(r, "PedidoId"),
            ["N°"] = "#" + Texto(r, "Numero"),
            ["Cliente"] = Texto(r, "Cliente"),
            ["Celular"] = Texto(r, "Celular"),
            ["Listo desde"] = Fecha(r, "FechaListo"),
            ["Total"] = Soles(r, "Total"),
            ["Pagado"] = Soles(r, "MontoPagado"),
            ["Días en custodia"] = Texto(r, "DiasEnCustodia"),
        }, ct);
        res.Accion = "donar";
        return res;
    }

    public async Task<ReporteResultDto> AnuladosAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            ;WITH UltimaAnulacion AS (
                SELECT PedidoId, MAX(Fecha) AS Fecha
                FROM dbo.PedidoHistorial
                WHERE EstadoProceso = 'ANULADO'
                GROUP BY PedidoId
            )
            SELECT p.Numero, c.Nombre AS Cliente, ua.Fecha, u.NombreCompleto AS Responsable, p.Total, p.MotivoAnulacion
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN UltimaAnulacion ua ON ua.PedidoId = p.Id
            LEFT JOIN dbo.PedidoHistorial ph ON ph.PedidoId = p.Id AND ph.Fecha = ua.Fecha AND ph.EstadoProceso = 'ANULADO'
            LEFT JOIN dbo.Usuario u ON u.Id = ph.UsuarioId
            WHERE p.Anulado = 1 AND CAST(ISNULL(ua.Fecha, p.FechaIngreso) AS DATE) BETWEEN @Desde AND @Hasta AND p.SedeId = @SedeId
            ORDER BY ua.Fecha DESC";
        cmd.AddParam("@Desde", desde.Date);
        cmd.AddParam("@Hasta", hasta.Date);
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "N°", "Cliente", "Fecha anulación", "Responsable", "Total", "Motivo" };
        return await Ejecutar(cmd, columnas, r => new Dictionary<string, string>
        {
            ["N°"] = "#" + Texto(r, "Numero"),
            ["Cliente"] = Texto(r, "Cliente"),
            ["Fecha anulación"] = Fecha(r, "Fecha"),
            ["Responsable"] = Texto(r, "Responsable"),
            ["Total"] = Soles(r, "Total"),
            ["Motivo"] = Texto(r, "MotivoAnulacion"),
        }, ct);
    }

    public async Task<ReporteResultDto> RegistroEntregasAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Numero, c.Nombre AS Cliente, ph.EstadoProceso, ph.Fecha, u.NombreCompleto AS Responsable
            FROM dbo.PedidoHistorial ph
            INNER JOIN dbo.Pedido p ON p.Id = ph.PedidoId
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN dbo.Usuario u ON u.Id = ph.UsuarioId
            WHERE ph.EstadoProceso IN ('PENDIENTE','ENTREGADO') AND CAST(ph.Fecha AS DATE) BETWEEN @Desde AND @Hasta AND p.SedeId = @SedeId
            ORDER BY ph.Fecha DESC";
        cmd.AddParam("@Desde", desde.Date);
        cmd.AddParam("@Hasta", hasta.Date);
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "N°", "Cliente", "Evento", "Fecha", "Responsable" };
        return await Ejecutar(cmd, columnas, r => new Dictionary<string, string>
        {
            ["N°"] = "#" + Texto(r, "Numero"),
            ["Cliente"] = Texto(r, "Cliente"),
            ["Evento"] = Texto(r, "EstadoProceso") == "PENDIENTE" ? "Registro" : "Entrega",
            ["Fecha"] = Fecha(r, "Fecha"),
            ["Responsable"] = Texto(r, "Responsable"),
        }, ct);
    }

    public async Task<ReporteResultDto> PagosAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT m.Fecha, p.Numero, u.NombreCompleto AS Responsable, m.MetodoPago, m.Monto, m.Descripcion
            FROM dbo.MovimientoCaja m
            LEFT JOIN dbo.Pedido p ON p.Id = m.PedidoId
            LEFT JOIN dbo.Usuario u ON u.Id = m.UsuarioId
            WHERE m.Tipo = 'INGRESO' AND CAST(m.Fecha AS DATE) BETWEEN @Desde AND @Hasta AND m.SedeId = @SedeId
            ORDER BY m.Fecha DESC";
        cmd.AddParam("@Desde", desde.Date);
        cmd.AddParam("@Hasta", hasta.Date);
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "Fecha", "Pedido", "Responsable", "Método de pago", "Monto", "Descripción" };
        return await Ejecutar(cmd, columnas, r => new Dictionary<string, string>
        {
            ["Fecha"] = Fecha(r, "Fecha"),
            ["Pedido"] = r.IsDBNull(r.GetOrdinal("Numero")) ? "—" : "#" + Texto(r, "Numero"),
            ["Responsable"] = Texto(r, "Responsable"),
            ["Método de pago"] = Texto(r, "MetodoPago"),
            ["Monto"] = Soles(r, "Monto"),
            ["Descripción"] = Texto(r, "Descripcion"),
        }, ct);
    }

    public async Task<ReporteResultDto> DescuentoDirectoAsync(DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Numero, c.Nombre AS Cliente, u.NombreCompleto AS Responsable,
                   p.Subtotal, p.Descuento, p.FechaIngreso
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN dbo.Usuario u ON u.Id = p.UsuarioId
            WHERE p.Descuento > 0 AND CAST(p.FechaIngreso AS DATE) BETWEEN @Desde AND @Hasta AND p.SedeId = @SedeId
            ORDER BY p.FechaIngreso DESC";
        cmd.AddParam("@Desde", desde.Date);
        cmd.AddParam("@Hasta", hasta.Date);
        cmd.AddParam("@SedeId", sedeId);
        var columnas = new List<string> { "N°", "Cliente", "Responsable", "Subtotal", "Descuento", "Fecha" };
        return await Ejecutar(cmd, columnas, r => new Dictionary<string, string>
        {
            ["N°"] = "#" + Texto(r, "Numero"),
            ["Cliente"] = Texto(r, "Cliente"),
            ["Responsable"] = Texto(r, "Responsable"),
            ["Subtotal"] = Soles(r, "Subtotal"),
            ["Descuento"] = Soles(r, "Descuento"),
            ["Fecha"] = Fecha(r, "FechaIngreso"),
        }, ct);
    }
}
