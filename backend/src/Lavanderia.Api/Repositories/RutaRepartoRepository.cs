using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

/// <summary>Datos del pedido que necesita el seguimiento en vivo del reparto, resueltos por el
/// token publico de ruta (o por id de pedido para el panel). Incluye el NegocioId (via Sede)
/// para poder cargar la marca/config del negocio sin depender de MapPedido.</summary>
public class RutaReparto
{
    public int PedidoId { get; set; }
    public int SedeId { get; set; }
    public int NegocioId { get; set; }
    public int UsuarioId { get; set; }
    public int Numero { get; set; }
    public string Modalidad { get; set; } = "";
    public string ClienteNombre { get; set; } = "";
    public string? ClienteCelular { get; set; }
    public string? DireccionEntrega { get; set; }
    public string? DistritoEntrega { get; set; }
    public string? ReferenciaEntrega { get; set; }
    public decimal? LatitudEntrega { get; set; }
    public decimal? LongitudEntrega { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public string EstadoProceso { get; set; } = "";
    public bool Anulado { get; set; }
    public DateTime? RutaIniciadaEn { get; set; }
    public DateTime? TokenRutaExpiraEn { get; set; }
    public decimal? MotorizadoLat { get; set; }
    public decimal? MotorizadoLng { get; set; }
    public DateTime? MotorizadoUbicadoEn { get; set; }
    public bool NotifRutaEnviada { get; set; }
    public bool NotifCercaEnviada { get; set; }
    public bool NotifLlegadaEnviada { get; set; }

    public decimal Saldo => Math.Max(0m, Total - MontoPagado);
}

public interface IRutaRepartoRepository
{
    Task<RutaReparto?> ObtenerPorTokenAsync(Guid token, CancellationToken ct = default);
    Task<RutaReparto?> ObtenerPorPedidoAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    /// <summary>Devuelve el token de reparto del pedido, creandolo si aun no existe.</summary>
    Task<Guid> AsegurarTokenAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task RevocarTokenAsync(int pedidoId, CancellationToken ct = default);
    Task IniciarRutaAsync(int pedidoId, CancellationToken ct = default);
    Task ActualizarUbicacionAsync(int pedidoId, decimal lat, decimal lng, CancellationToken ct = default);
    /// <summary>flag: "ruta" | "cerca" | "llegada".</summary>
    Task MarcarNotifAsync(int pedidoId, string flag, CancellationToken ct = default);
}

public class RutaRepartoRepository : IRutaRepartoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public RutaRepartoRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string BaseSelect = @"
        SELECT p.Id AS PedidoId, p.SedeId, s.NegocioId, p.UsuarioId, p.Numero, p.Modalidad,
               c.Nombre AS ClienteNombre, c.Celular AS ClienteCelular,
               p.DireccionEntrega, p.DistritoEntrega, p.ReferenciaEntrega,
               p.LatitudEntrega, p.LongitudEntrega, p.Total, p.MontoPagado,
               p.EstadoProceso, p.Anulado, p.RutaIniciadaEn, p.TokenRutaExpiraEn,
               p.MotorizadoLat, p.MotorizadoLng, p.MotorizadoUbicadoEn,
               p.NotifRutaEnviada, p.NotifCercaEnviada, p.NotifLlegadaEnviada
        FROM dbo.Pedido p
        INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
        INNER JOIN dbo.Sede s ON s.Id = p.SedeId";

    private static RutaReparto Map(SqlDataReader r) => new()
    {
        PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
        SedeId = r.GetInt32(r.GetOrdinal("SedeId")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        UsuarioId = r.GetInt32(r.GetOrdinal("UsuarioId")),
        Numero = r.GetInt32(r.GetOrdinal("Numero")),
        Modalidad = r.GetString(r.GetOrdinal("Modalidad")),
        ClienteNombre = r.GetString(r.GetOrdinal("ClienteNombre")),
        ClienteCelular = r.GetNullableString("ClienteCelular"),
        DireccionEntrega = r.GetNullableString("DireccionEntrega"),
        DistritoEntrega = r.GetNullableString("DistritoEntrega"),
        ReferenciaEntrega = r.GetNullableString("ReferenciaEntrega"),
        LatitudEntrega = r.GetNullableDecimal("LatitudEntrega"),
        LongitudEntrega = r.GetNullableDecimal("LongitudEntrega"),
        Total = r.GetDecimal(r.GetOrdinal("Total")),
        MontoPagado = r.GetDecimal(r.GetOrdinal("MontoPagado")),
        EstadoProceso = r.GetString(r.GetOrdinal("EstadoProceso")),
        Anulado = r.GetBoolean(r.GetOrdinal("Anulado")),
        RutaIniciadaEn = r.GetNullableDateTime("RutaIniciadaEn"),
        TokenRutaExpiraEn = r.GetNullableDateTime("TokenRutaExpiraEn"),
        MotorizadoLat = r.GetNullableDecimal("MotorizadoLat"),
        MotorizadoLng = r.GetNullableDecimal("MotorizadoLng"),
        MotorizadoUbicadoEn = r.GetNullableDateTime("MotorizadoUbicadoEn"),
        NotifRutaEnviada = r.GetBoolean(r.GetOrdinal("NotifRutaEnviada")),
        NotifCercaEnviada = r.GetBoolean(r.GetOrdinal("NotifCercaEnviada")),
        NotifLlegadaEnviada = r.GetBoolean(r.GetOrdinal("NotifLlegadaEnviada"))
    };

    public async Task<RutaReparto?> ObtenerPorTokenAsync(Guid token, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE p.TokenRuta = @Token AND p.TokenRutaExpiraEn > SYSDATETIME()";
        cmd.AddParam("@Token", token);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<RutaReparto?> ObtenerPorPedidoAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE p.Id = @Id AND p.SedeId = @SedeId";
        cmd.AddParam("@Id", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Guid> AsegurarTokenAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqlTransaction)tx;
        // Pedido tiene un trigger de estados terminales; SQL Server no permite OUTPUT sin
        // INTO sobre tablas con triggers. La actualizacion y lectura permanecen atomicas.
        cmd.CommandText = @"
            UPDATE dbo.Pedido
               SET TokenRuta = CASE
                       WHEN TokenRuta IS NULL OR TokenRutaExpiraEn IS NULL OR TokenRutaExpiraEn <= SYSDATETIME() THEN @Nuevo
                       ELSE TokenRuta
                   END,
                   TokenRutaExpiraEn = CASE
                       WHEN TokenRuta IS NULL OR TokenRutaExpiraEn IS NULL OR TokenRutaExpiraEn <= SYSDATETIME() THEN DATEADD(DAY, 7, SYSDATETIME())
                       ELSE TokenRutaExpiraEn
                   END
             WHERE Id = @Id AND SedeId = @SedeId AND Anulado = 0
               AND EstadoProceso NOT IN ('ENTREGADO', 'ANULADO', 'DONADO');
            SELECT TokenRuta FROM dbo.Pedido WHERE Id = @Id AND SedeId = @SedeId;";
        cmd.AddParam("@Nuevo", Guid.NewGuid());
        cmd.AddParam("@Id", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is not Guid token)
            throw new InvalidOperationException("No se pudo generar el enlace del repartidor para este pedido.");
        await tx.CommitAsync(ct);
        return token;
    }

    public async Task RevocarTokenAsync(int pedidoId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Pedido SET TokenRuta = NULL, TokenRutaExpiraEn = NULL WHERE Id = @Id";
        cmd.AddParam("@Id", pedidoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task IniciarRutaAsync(int pedidoId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Pedido
               SET RutaIniciadaEn = COALESCE(RutaIniciadaEn, SYSDATETIME())
             WHERE Id = @Id";
        cmd.AddParam("@Id", pedidoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ActualizarUbicacionAsync(int pedidoId, decimal lat, decimal lng, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Pedido
               SET MotorizadoLat = @Lat, MotorizadoLng = @Lng, MotorizadoUbicadoEn = SYSDATETIME()
             WHERE Id = @Id";
        cmd.AddParam("@Lat", lat);
        cmd.AddParam("@Lng", lng);
        cmd.AddParam("@Id", pedidoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarcarNotifAsync(int pedidoId, string flag, CancellationToken ct = default)
    {
        var columna = flag switch
        {
            "ruta" => "NotifRutaEnviada",
            "cerca" => "NotifCercaEnviada",
            "llegada" => "NotifLlegadaEnviada",
            _ => throw new ArgumentException("Flag de notificacion invalido.", nameof(flag))
        };
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE dbo.Pedido SET {columna} = 1 WHERE Id = @Id";
        cmd.AddParam("@Id", pedidoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
