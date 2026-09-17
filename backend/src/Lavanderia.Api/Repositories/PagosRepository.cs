using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public interface IPagosRepository
{
    Task<ConfiguracionPagos?> ObtenerConfigAsync(int negocioId, CancellationToken ct = default);
    Task GuardarConfigAsync(ConfiguracionPagos c, CancellationToken ct = default);

    Task<SolicitudPago> CrearSolicitudAsync(int negocioId, int sedeId, int pedidoId, decimal monto, CancellationToken ct = default);
    Task<SolicitudPago?> ObtenerVigentePorPedidoAsync(int pedidoId, CancellationToken ct = default);
    Task<SolicitudPago?> ObtenerUltimaPorPedidoAsync(int pedidoId, CancellationToken ct = default);
    Task<SolicitudPago?> ObtenerPorTokenAsync(Guid token, CancellationToken ct = default);
    /// <summary>Reserva atomicamente el intento antes de llamar al proveedor. Evita que dos
    /// pestañas cobren el mismo pedido al mismo tiempo.</summary>
    Task<bool> IntentarIniciarCobroAsync(int id, CancellationToken ct = default);
    Task RestaurarPendienteAsync(int id, CancellationToken ct = default);
    Task MarcarRequiereConciliacionAsync(int id, string proveedorOperacionId, CancellationToken ct = default);
    /// <summary>Marca la solicitud como pagada solo si estaba siendo procesada.</summary>
    Task<bool> MarcarPagadoAsync(int id, string proveedorOperacionId, CancellationToken ct = default);
}

public class PagosRepository : IPagosRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PagosRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string ConfigSelect = @"
        SELECT Id, NegocioId, Proveedor, CodigoComercio, PublicKey, ApiKeyCifrada, HashKeyCifrada, Activo
        FROM dbo.ConfiguracionPagos";

    private static ConfiguracionPagos MapConfig(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        Proveedor = r.GetString(r.GetOrdinal("Proveedor")),
        CodigoComercio = r.GetNullableString("CodigoComercio"),
        PublicKey = r.GetNullableString("PublicKey"),
        ApiKeyCifrada = r.GetNullableString("ApiKeyCifrada"),
        HashKeyCifrada = r.GetNullableString("HashKeyCifrada"),
        Activo = r.GetBoolean(r.GetOrdinal("Activo"))
    };

    public async Task<ConfiguracionPagos?> ObtenerConfigAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ConfigSelect + " WHERE NegocioId = @NegocioId";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(MapConfig, ct);
    }

    public async Task GuardarConfigAsync(ConfiguracionPagos c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Upsert: el negocio puede no tener fila propia todavia (primera vez que configura).
        cmd.CommandText = @"
            MERGE dbo.ConfiguracionPagos AS target
            USING (SELECT @NegocioId AS NegocioId) AS src
                ON target.NegocioId = src.NegocioId
            WHEN MATCHED THEN UPDATE SET
                Proveedor = @Proveedor,
                CodigoComercio = @CodigoComercio,
                PublicKey = @PublicKey,
                ApiKeyCifrada = COALESCE(@ApiKeyCifrada, target.ApiKeyCifrada),
                HashKeyCifrada = COALESCE(@HashKeyCifrada, target.HashKeyCifrada),
                Activo = @Activo,
                FechaActualizacion = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (NegocioId, Proveedor, CodigoComercio, PublicKey, ApiKeyCifrada, HashKeyCifrada, Activo)
                VALUES
                (@NegocioId, @Proveedor, @CodigoComercio, @PublicKey, @ApiKeyCifrada, @HashKeyCifrada, @Activo);";
        cmd.AddParam("@NegocioId", c.NegocioId);
        cmd.AddParam("@Proveedor", c.Proveedor);
        cmd.AddParam("@CodigoComercio", c.CodigoComercio);
        cmd.AddParam("@PublicKey", c.PublicKey);
        cmd.AddParam("@ApiKeyCifrada", c.ApiKeyCifrada);
        cmd.AddParam("@HashKeyCifrada", c.HashKeyCifrada);
        cmd.AddParam("@Activo", c.Activo);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private const string SolicitudSelect = @"
        SELECT Id, NegocioId, SedeId, PedidoId, Token, Monto, Estado, ProveedorOperacionId,
               FechaCreacion, FechaExpiracion, FechaPago
        FROM dbo.SolicitudPago";

    private static SolicitudPago MapSolicitud(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        SedeId = r.GetInt32(r.GetOrdinal("SedeId")),
        PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
        Token = r.GetGuid(r.GetOrdinal("Token")),
        Monto = r.GetDecimal(r.GetOrdinal("Monto")),
        Estado = r.GetString(r.GetOrdinal("Estado")),
        ProveedorOperacionId = r.GetNullableString("ProveedorOperacionId"),
        FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion")),
        FechaExpiracion = r.GetDateTime(r.GetOrdinal("FechaExpiracion")),
        FechaPago = r.GetNullableDateTime("FechaPago")
    };

    public async Task<SolicitudPago> CrearSolicitudAsync(int negocioId, int sedeId, int pedidoId, decimal monto, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.SolicitudPago (NegocioId, SedeId, PedidoId, Token, Monto, Estado, FechaExpiracion)
            OUTPUT INSERTED.Id, INSERTED.Token, INSERTED.FechaCreacion, INSERTED.FechaExpiracion
            VALUES (@NegocioId, @SedeId, @PedidoId, NEWID(), @Monto, 'PENDIENTE', DATEADD(DAY, 30, SYSDATETIME()));";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@PedidoId", pedidoId);
        cmd.AddParam("@Monto", monto);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new SolicitudPago
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            NegocioId = negocioId,
            SedeId = sedeId,
            PedidoId = pedidoId,
            Token = reader.GetGuid(reader.GetOrdinal("Token")),
            Monto = monto,
            Estado = "PENDIENTE",
            FechaCreacion = reader.GetDateTime(reader.GetOrdinal("FechaCreacion")),
            FechaExpiracion = reader.GetDateTime(reader.GetOrdinal("FechaExpiracion"))
        };
    }

    public async Task<SolicitudPago?> ObtenerVigentePorPedidoAsync(int pedidoId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SolicitudSelect + @"
            WHERE PedidoId = @PedidoId AND Estado = 'PENDIENTE' AND FechaExpiracion > SYSDATETIME()
            ORDER BY FechaCreacion DESC";
        cmd.AddParam("@PedidoId", pedidoId);
        return await cmd.ReadFirstOrDefaultAsync(MapSolicitud, ct);
    }

    public async Task<SolicitudPago?> ObtenerUltimaPorPedidoAsync(int pedidoId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SolicitudSelect + @"
            WHERE PedidoId = @PedidoId
            ORDER BY FechaCreacion DESC";
        cmd.AddParam("@PedidoId", pedidoId);
        return await cmd.ReadFirstOrDefaultAsync(MapSolicitud, ct);
    }

    public async Task<SolicitudPago?> ObtenerPorTokenAsync(Guid token, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SolicitudSelect + " WHERE Token = @Token";
        cmd.AddParam("@Token", token);
        return await cmd.ReadFirstOrDefaultAsync(MapSolicitud, ct);
    }

    public async Task<bool> IntentarIniciarCobroAsync(int id, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.SolicitudPago
               SET Estado = 'PROCESANDO'
             WHERE Id = @Id AND Estado = 'PENDIENTE' AND FechaExpiracion > SYSDATETIME()";
        cmd.AddParam("@Id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task RestaurarPendienteAsync(int id, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.SolicitudPago
               SET Estado = CASE WHEN FechaExpiracion > SYSDATETIME() THEN 'PENDIENTE' ELSE 'EXPIRADO' END
             WHERE Id = @Id AND Estado = 'PROCESANDO'";
        cmd.AddParam("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarcarRequiereConciliacionAsync(int id, string proveedorOperacionId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.SolicitudPago
               SET Estado = 'CONCILIAR', ProveedorOperacionId = @OperacionId, FechaPago = SYSDATETIME()
             WHERE Id = @Id AND Estado = 'PROCESANDO'";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@OperacionId", proveedorOperacionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> MarcarPagadoAsync(int id, string proveedorOperacionId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.SolicitudPago
               SET Estado = 'PAGADO', ProveedorOperacionId = @OperacionId, FechaPago = SYSDATETIME()
             WHERE Id = @Id AND Estado = 'PROCESANDO'";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@OperacionId", proveedorOperacionId);
        var filas = await cmd.ExecuteNonQueryAsync(ct);
        return filas > 0;
    }
}
