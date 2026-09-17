using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IClienteRepository
{
    Task<List<Cliente>> BuscarAsync(string? texto, string? campo, int limite, int negocioId, CancellationToken ct = default);
    Task<Cliente?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task<Cliente?> BuscarPorCelularOrDniAsync(string valor, int negocioId, CancellationToken ct = default);
    Task<Cliente?> BuscarDuplicadoAsync(string? nombre, string? celular, string? dni, string? documentoFiscal, int negocioId, int? excluirClienteId = null, CancellationToken ct = default);
    Task<int> CrearAsync(Cliente c, CancellationToken ct = default);
    Task ActualizarAsync(Cliente c, int negocioId, CancellationToken ct = default);
    Task DesactivarAsync(int id, int negocioId, CancellationToken ct = default);
    Task<int> ContarPedidosAsync(int clienteId, int negocioId, CancellationToken ct = default);
    /// <summary>Clientes nuevos agrupados por mes (para las barras de tendencia), clave = primer día del mes.</summary>
    Task<Dictionary<DateTime, int>> ContarNuevosPorMesAsync(DateTime desdeMes, int negocioId, CancellationToken ct = default);
    Task<List<ClienteFrecuenteDto>> ListarFrecuentesAsync(DateTime desde, DateTime hasta, int limite, int negocioId, CancellationToken ct = default);
    Task FusionarAsync(int origenId, int destinoId, int negocioId, CancellationToken ct = default);
    Task<List<MovimientoPuntos>> ListarMovimientosPuntosAsync(int clienteId, int negocioId, CancellationToken ct = default);
    Task AgregarMovimientoPuntosAsync(MovimientoPuntos m, int negocioId, CancellationToken ct = default);
    Task RevertirPuntosPedidoAsync(int pedidoId, int negocioId, int? usuarioId, CancellationToken ct = default);
    Task<decimal> DeudaTotalAsync(int clienteId, int negocioId, CancellationToken ct = default);
    /// <summary>Clientes con al menos un pedido, ordenados por los que no compran hace mas
    /// tiempo primero — la lista base para una campana de "vuelve pronto".</summary>
    Task<List<ClienteAnaliticaDto>> ListarAnaliticaAsync(int negocioId, CancellationToken ct = default);
    Task<List<ClienteCumpleanosDto>> ListarCumpleanosProximosAsync(int negocioId, int diasAdelante, CancellationToken ct = default);
}

public class ClienteRepository : IClienteRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ClienteRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string BaseSelect = @"
        SELECT Id, NegocioId, Nombre, Celular, Dni, DocumentoFiscal, Direccion, Puntos, Activo, FechaCreacion, FechaNacimiento
        FROM dbo.Cliente";

    private static Cliente Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Celular = r.GetNullableString("Celular"),
        Dni = r.GetNullableString("Dni"),
        DocumentoFiscal = r.GetNullableString("DocumentoFiscal"),
        Direccion = r.GetNullableString("Direccion"),
        Puntos = r.GetInt32(r.GetOrdinal("Puntos")),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion")),
        FechaNacimiento = r.IsDBNull(r.GetOrdinal("FechaNacimiento")) ? null : DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("FechaNacimiento")))
    };

    public async Task<List<Cliente>> BuscarAsync(string? texto, string? campo, int limite, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        var whereExtra = "";
        if (!string.IsNullOrWhiteSpace(texto))
        {
            whereExtra = campo?.ToLowerInvariant() switch
            {
                "celular" => " AND Celular LIKE @Texto",
                "dni" => " AND Dni LIKE @Texto",
                _ => " AND Nombre LIKE @Texto"
            };
            cmd.AddParam("@Texto", $"%{texto}%");
        }
        cmd.CommandText = $"SELECT TOP (@Limite) * FROM ({BaseSelect}) t WHERE Activo = 1 AND NegocioId = @NegocioId {whereExtra} ORDER BY Nombre";
        cmd.AddParam("@Limite", limite);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<Cliente?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Cliente?> BuscarPorCelularOrDniAsync(string valor, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE (Celular = @Valor OR Dni = @Valor) AND NegocioId = @NegocioId";
        cmd.AddParam("@Valor", valor);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Cliente?> BuscarDuplicadoAsync(string? nombre, string? celular, string? dni, string? documentoFiscal, int negocioId, int? excluirClienteId = null, CancellationToken ct = default)
    {
        // Un cliente es "el mismo" cuando coincide su identificador de persona (DNI o documento
        // fiscal) o cuando repite Nombre + Celular. OJO: un celular compartido por personas con
        // nombres distintos (familias con un solo número) NO es duplicado, por eso el celular
        // solo cuenta junto al nombre.
        var condiciones = new List<string>();
        if (!string.IsNullOrWhiteSpace(dni)) condiciones.Add("Dni = @Dni");
        if (!string.IsNullOrWhiteSpace(documentoFiscal)) condiciones.Add("DocumentoFiscal = @DocumentoFiscal");
        if (!string.IsNullOrWhiteSpace(celular) && !string.IsNullOrWhiteSpace(nombre))
            condiciones.Add("(Celular = @Celular AND LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI)");
        if (condiciones.Count == 0) return null;

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + $@"
            WHERE NegocioId = @NegocioId
              AND Activo = 1
              AND ({string.Join(" OR ", condiciones)})
              AND (@ExcluirId IS NULL OR Id <> @ExcluirId)";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Nombre", string.IsNullOrWhiteSpace(nombre) ? null : nombre);
        cmd.AddParam("@Celular", string.IsNullOrWhiteSpace(celular) ? null : celular);
        cmd.AddParam("@Dni", string.IsNullOrWhiteSpace(dni) ? null : dni);
        cmd.AddParam("@DocumentoFiscal", string.IsNullOrWhiteSpace(documentoFiscal) ? null : documentoFiscal);
        cmd.AddParam("@ExcluirId", excluirClienteId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<int> CrearAsync(Cliente c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Cliente (NegocioId, Nombre, Celular, Dni, DocumentoFiscal, Direccion, Puntos, FechaNacimiento)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Nombre, @Celular, @Dni, @DocumentoFiscal, @Direccion, @Puntos, @FechaNacimiento);";
        cmd.AddParam("@NegocioId", c.NegocioId);
        cmd.AddParam("@Nombre", c.Nombre);
        cmd.AddParam("@Celular", c.Celular);
        cmd.AddParam("@Dni", c.Dni);
        cmd.AddParam("@DocumentoFiscal", c.DocumentoFiscal);
        cmd.AddParam("@Direccion", c.Direccion);
        cmd.AddParam("@Puntos", c.Puntos);
        cmd.AddParam("@FechaNacimiento", c.FechaNacimiento?.ToDateTime(TimeOnly.MinValue));
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Cliente c, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Cliente
               SET Nombre = @Nombre,
                   Celular = @Celular,
                   Dni = @Dni,
                   DocumentoFiscal = @DocumentoFiscal,
                   Direccion = @Direccion,
                   Puntos = @Puntos,
                   FechaNacimiento = @FechaNacimiento
             WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", c.Id);
        cmd.AddParam("@Nombre", c.Nombre);
        cmd.AddParam("@Celular", c.Celular);
        cmd.AddParam("@Dni", c.Dni);
        cmd.AddParam("@DocumentoFiscal", c.DocumentoFiscal);
        cmd.AddParam("@Direccion", c.Direccion);
        cmd.AddParam("@Puntos", c.Puntos);
        cmd.AddParam("@FechaNacimiento", c.FechaNacimiento?.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DesactivarAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Cliente SET Activo = 0 WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ContarPedidosAsync(int clienteId, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            WHERE p.ClienteId = @Id AND c.NegocioId = @NegocioId";
        cmd.AddParam("@Id", clienteId);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<Dictionary<DateTime, int>> ContarNuevosPorMesAsync(DateTime desdeMes, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DATEFROMPARTS(YEAR(FechaCreacion), MONTH(FechaCreacion), 1) AS Mes, COUNT(1) AS Cant
            FROM dbo.Cliente
            WHERE NegocioId = @NegocioId AND FechaCreacion >= @Desde
            GROUP BY DATEFROMPARTS(YEAR(FechaCreacion), MONTH(FechaCreacion), 1)";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Desde", desdeMes.Date);
        var dict = new Dictionary<DateTime, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dict[reader.GetDateTime(0)] = reader.GetInt32(1);
        return dict;
    }

    public async Task<List<ClienteFrecuenteDto>> ListarFrecuentesAsync(DateTime desde, DateTime hasta, int limite, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP (@Limite) c.Id AS ClienteId, c.Nombre, c.Celular, COUNT(p.Id) AS Visitas
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            WHERE p.FechaIngreso >= @Desde AND p.FechaIngreso < @Hasta AND p.Anulado = 0 AND c.NegocioId = @NegocioId
            GROUP BY c.Id, c.Nombre, c.Celular
            ORDER BY COUNT(p.Id) DESC, c.Nombre";
        cmd.AddParam("@Desde", desde);
        cmd.AddParam("@Hasta", hasta);
        cmd.AddParam("@Limite", limite);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => new ClienteFrecuenteDto(
            r.GetInt32(r.GetOrdinal("ClienteId")),
            r.GetString(r.GetOrdinal("Nombre")),
            r.GetNullableString("Celular"),
            r.GetInt32(r.GetOrdinal("Visitas"))
        ), ct);
    }

    public async Task FusionarAsync(int origenId, int destinoId, int negocioId, CancellationToken ct = default)
    {
        if (origenId == destinoId) throw new InvalidOperationException("Elige dos clientes distintos para fusionar.");

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var cmdPuntos = conn.CreateCommand())
            {
                cmdPuntos.Transaction = tx;
                cmdPuntos.CommandText = @"
                    UPDATE dbo.Cliente
                    SET Puntos = Puntos + (
                        SELECT Puntos FROM dbo.Cliente WHERE Id = @Origen AND NegocioId = @NegocioId
                    )
                    WHERE Id = @Destino AND NegocioId = @NegocioId";
                cmdPuntos.AddParam("@Origen", origenId);
                cmdPuntos.AddParam("@Destino", destinoId);
                cmdPuntos.AddParam("@NegocioId", negocioId);
                await cmdPuntos.ExecuteNonQueryAsync(ct);
            }

            await using (var cmdPedidos = conn.CreateCommand())
            {
                cmdPedidos.Transaction = tx;
                cmdPedidos.CommandText = @"
                    UPDATE p
                    SET ClienteId = @Destino
                    FROM dbo.Pedido p
                    INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
                    WHERE p.ClienteId = @Origen AND c.NegocioId = @NegocioId";
                cmdPedidos.AddParam("@Destino", destinoId);
                cmdPedidos.AddParam("@Origen", origenId);
                cmdPedidos.AddParam("@NegocioId", negocioId);
                await cmdPedidos.ExecuteNonQueryAsync(ct);
            }

            await using (var cmdMovPuntos = conn.CreateCommand())
            {
                cmdMovPuntos.Transaction = tx;
                cmdMovPuntos.CommandText = @"
                    UPDATE dbo.MovimientoPuntos
                    SET ClienteId = @Destino
                    WHERE ClienteId = @Origen AND NegocioId = @NegocioId";
                cmdMovPuntos.AddParam("@Destino", destinoId);
                cmdMovPuntos.AddParam("@Origen", origenId);
                cmdMovPuntos.AddParam("@NegocioId", negocioId);
                await cmdMovPuntos.ExecuteNonQueryAsync(ct);
            }

            await using (var cmdDesactivar = conn.CreateCommand())
            {
                cmdDesactivar.Transaction = tx;
                cmdDesactivar.CommandText = "UPDATE dbo.Cliente SET Activo = 0, Puntos = 0 WHERE Id = @Origen AND NegocioId = @NegocioId";
                cmdDesactivar.AddParam("@Origen", origenId);
                cmdDesactivar.AddParam("@NegocioId", negocioId);
                await cmdDesactivar.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<MovimientoPuntos>> ListarMovimientosPuntosAsync(int clienteId, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT m.Id, m.ClienteId, m.Fecha, m.Motivo, m.Puntos, m.Tipo, m.UsuarioId, m.PedidoId, u.NombreCompleto AS UsuarioNombre
            FROM dbo.MovimientoPuntos m
            INNER JOIN dbo.Cliente c ON c.Id = m.ClienteId
            LEFT JOIN dbo.Usuario u ON u.Id = m.UsuarioId
            WHERE m.ClienteId = @ClienteId AND c.NegocioId = @NegocioId
            ORDER BY m.Fecha DESC";
        cmd.AddParam("@ClienteId", clienteId);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => new MovimientoPuntos
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            ClienteId = r.GetInt32(r.GetOrdinal("ClienteId")),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            Motivo = r.GetString(r.GetOrdinal("Motivo")),
            Puntos = r.GetInt32(r.GetOrdinal("Puntos")),
            Tipo = r.GetString(r.GetOrdinal("Tipo")),
            UsuarioId = r.IsDBNull(r.GetOrdinal("UsuarioId")) ? null : r.GetInt32(r.GetOrdinal("UsuarioId")),
            UsuarioNombre = r.GetNullableString("UsuarioNombre"),
            PedidoId = r.GetNullableInt("PedidoId")
        }, ct);
    }

    public async Task AgregarMovimientoPuntosAsync(MovimientoPuntos m, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var cmdIns = conn.CreateCommand())
            {
                cmdIns.Transaction = tx;
                cmdIns.CommandText = @"
                    INSERT INTO dbo.MovimientoPuntos (NegocioId, ClienteId, Fecha, Motivo, Puntos, Tipo, UsuarioId, PedidoId)
                    SELECT @NegocioId, @ClienteId, SYSDATETIME(), @Motivo, @Puntos, @Tipo, @UsuarioId, @PedidoId
                    WHERE EXISTS (
                        SELECT 1 FROM dbo.Cliente WHERE Id = @ClienteId AND NegocioId = @NegocioId
                    )";
                cmdIns.AddParam("@NegocioId", negocioId);
                cmdIns.AddParam("@ClienteId", m.ClienteId);
                cmdIns.AddParam("@Motivo", m.Motivo);
                cmdIns.AddParam("@Puntos", m.Puntos);
                cmdIns.AddParam("@Tipo", m.Tipo);
                cmdIns.AddParam("@UsuarioId", m.UsuarioId);
                cmdIns.AddParam("@PedidoId", (object?)m.PedidoId ?? DBNull.Value);
                var filas = await cmdIns.ExecuteNonQueryAsync(ct);
                if (filas == 0) throw new InvalidOperationException("El cliente no pertenece a este negocio.");
            }

            await using (var cmdUpd = conn.CreateCommand())
            {
                cmdUpd.Transaction = tx;
                var signo = m.Tipo == "RESTA" ? -1 : 1;
                cmdUpd.CommandText = "UPDATE dbo.Cliente SET Puntos = Puntos + @Delta WHERE Id = @ClienteId AND NegocioId = @NegocioId";
                cmdUpd.AddParam("@Delta", signo * m.Puntos);
                cmdUpd.AddParam("@ClienteId", m.ClienteId);
                cmdUpd.AddParam("@NegocioId", negocioId);
                await cmdUpd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Deshace los puntos (otorgados y/o canjeados) de un pedido que se anula, dejando
    /// el saldo del cliente como si el pedido nunca hubiera existido. Idempotente: si ya no queda
    /// nada neto que revertir, no hace cambios.</summary>
    public async Task RevertirPuntosPedidoAsync(int pedidoId, int negocioId, int? usuarioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int clienteId = 0, neto = 0;
            await using (var cmdSum = conn.CreateCommand())
            {
                cmdSum.Transaction = tx;
                cmdSum.CommandText = @"
                    SELECT TOP 1 ClienteId,
                           SUM(CASE WHEN Tipo = 'SUMA' THEN Puntos ELSE -Puntos END) AS Neto
                    FROM dbo.MovimientoPuntos
                    WHERE PedidoId = @PedidoId AND NegocioId = @NegocioId
                    GROUP BY ClienteId";
                cmdSum.AddParam("@PedidoId", pedidoId);
                cmdSum.AddParam("@NegocioId", negocioId);
                await using var r = await cmdSum.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    clienteId = r.GetInt32(0);
                    neto = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                }
            }

            if (clienteId == 0 || neto == 0) { await tx.RollbackAsync(ct); return; }

            await using (var cmdIns = conn.CreateCommand())
            {
                cmdIns.Transaction = tx;
                cmdIns.CommandText = @"
                    INSERT INTO dbo.MovimientoPuntos (NegocioId, ClienteId, Fecha, Motivo, Puntos, Tipo, UsuarioId, PedidoId)
                    VALUES (@NegocioId, @ClienteId, SYSDATETIME(), @Motivo, @Puntos, @Tipo, @UsuarioId, @PedidoId)";
                cmdIns.AddParam("@NegocioId", negocioId);
                cmdIns.AddParam("@ClienteId", clienteId);
                cmdIns.AddParam("@Motivo", "Reversa de puntos por anulación del pedido");
                cmdIns.AddParam("@Puntos", Math.Abs(neto));
                cmdIns.AddParam("@Tipo", neto > 0 ? "RESTA" : "SUMA");
                cmdIns.AddParam("@UsuarioId", (object?)usuarioId ?? DBNull.Value);
                cmdIns.AddParam("@PedidoId", pedidoId);
                await cmdIns.ExecuteNonQueryAsync(ct);
            }

            await using (var cmdUpd = conn.CreateCommand())
            {
                cmdUpd.Transaction = tx;
                // Clamp a 0: si el cliente ya gastó los puntos que ahora se revierten, no lo dejamos negativo.
                cmdUpd.CommandText = @"
                    UPDATE dbo.Cliente
                       SET Puntos = CASE WHEN Puntos - @Neto < 0 THEN 0 ELSE Puntos - @Neto END
                     WHERE Id = @ClienteId AND NegocioId = @NegocioId";
                cmdUpd.AddParam("@Neto", neto);
                cmdUpd.AddParam("@ClienteId", clienteId);
                cmdUpd.AddParam("@NegocioId", negocioId);
                await cmdUpd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    /// <summary>Saldo total que el cliente debe: suma de (Total - MontoPagado) de todos sus
    /// pedidos no anulados con saldo pendiente, a través de todas las sedes del negocio.</summary>
    public async Task<decimal> DeudaTotalAsync(int clienteId, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ISNULL(SUM(p.Total - p.MontoPagado), 0)
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            WHERE p.ClienteId = @ClienteId AND c.NegocioId = @NegocioId
              AND p.Anulado = 0 AND p.Total > p.MontoPagado";
        cmd.AddParam("@ClienteId", clienteId);
        cmd.AddParam("@NegocioId", negocioId);
        var val = await cmd.ExecuteScalarAsync(ct);
        return val is decimal d ? d : 0m;
    }

    public async Task<List<ClienteAnaliticaDto>> ListarAnaliticaAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.Id AS ClienteId, c.Nombre, c.Celular,
                   COUNT(p.Id) AS TotalPedidos,
                   AVG(p.Total) AS TicketPromedio,
                   MAX(p.FechaIngreso) AS UltimaCompra,
                   ISNULL(SUM(CASE WHEN p.Total > p.MontoPagado THEN p.Total - p.MontoPagado ELSE 0 END), 0) AS DeudaTotal
            FROM dbo.Cliente c
            INNER JOIN dbo.Pedido p ON p.ClienteId = c.Id AND p.Anulado = 0
            WHERE c.NegocioId = @NegocioId AND c.Activo = 1
            GROUP BY c.Id, c.Nombre, c.Celular
            ORDER BY MAX(p.FechaIngreso) ASC";
        cmd.AddParam("@NegocioId", negocioId);
        var hoy = DateTime.Today;
        return await cmd.ReadListAsync(r =>
        {
            var ultimaCompra = r.GetDateTime(r.GetOrdinal("UltimaCompra"));
            return new ClienteAnaliticaDto(
                r.GetInt32(r.GetOrdinal("ClienteId")),
                r.GetString(r.GetOrdinal("Nombre")),
                r.GetNullableString("Celular"),
                r.GetInt32(r.GetOrdinal("TotalPedidos")),
                r.GetDecimal(r.GetOrdinal("TicketPromedio")),
                ultimaCompra,
                (int)(hoy - ultimaCompra.Date).TotalDays,
                r.GetDecimal(r.GetOrdinal("DeudaTotal"))
            );
        }, ct);
    }

    public async Task<List<ClienteCumpleanosDto>> ListarCumpleanosProximosAsync(int negocioId, int diasAdelante, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // DATEADD (no DATEFROMPARTS) para que un cumpleanos 29-feb no rompa la consulta en anios
        // no bisiestos: SQL Server lo recorta a 28-feb en vez de lanzar error de fecha invalida.
        cmd.CommandText = @"
            ;WITH ProximoCumple AS (
                SELECT Id, Nombre, Celular, FechaNacimiento,
                       DATEADD(YEAR,
                           DATEDIFF(YEAR, FechaNacimiento, GETDATE())
                               + CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, FechaNacimiento, GETDATE()), FechaNacimiento) < CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END,
                           FechaNacimiento) AS ProximaFecha
                FROM dbo.Cliente
                WHERE NegocioId = @NegocioId AND Activo = 1 AND FechaNacimiento IS NOT NULL
            )
            SELECT Id AS ClienteId, Nombre, Celular, FechaNacimiento,
                   DATEDIFF(DAY, CAST(GETDATE() AS DATE), ProximaFecha) AS DiasParaCumpleanos
            FROM ProximoCumple
            WHERE DATEDIFF(DAY, CAST(GETDATE() AS DATE), ProximaFecha) <= @DiasAdelante
            ORDER BY ProximaFecha";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@DiasAdelante", diasAdelante);
        return await cmd.ReadListAsync(r => new ClienteCumpleanosDto(
            r.GetInt32(r.GetOrdinal("ClienteId")),
            r.GetString(r.GetOrdinal("Nombre")),
            r.GetNullableString("Celular"),
            DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("FechaNacimiento"))),
            r.GetInt32(r.GetOrdinal("DiasParaCumpleanos"))
        ), ct);
    }
}
