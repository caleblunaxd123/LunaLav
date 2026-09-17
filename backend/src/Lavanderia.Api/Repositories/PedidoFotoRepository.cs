using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

/// <summary>Metadato de una foto de evidencia de un pedido. El archivo en si vive en disco
/// (ver <c>IAlmacenamientoFotos</c>); aqui solo guardamos como llegar a el y a que pedido/sede
/// pertenece.</summary>
public class PedidoFoto
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int SedeId { get; set; }
    public int NegocioId { get; set; }
    /// <summary>RECEPCION | ENTREGA | OTRO.</summary>
    public string Momento { get; set; } = "OTRO";
    public string NombreArchivo { get; set; } = "";
    public string ContentType { get; set; } = "image/jpeg";
    public int TamanoBytes { get; set; }
    public int? SubidoPorUsuarioId { get; set; }
    public DateTime FechaSubida { get; set; }
}

public interface IPedidoFotoRepository
{
    Task<List<PedidoFoto>> ListarPorPedidoAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task<int> ContarPorPedidoAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task<int> CrearAsync(PedidoFoto foto, CancellationToken ct = default);
    /// <summary>Trae una foto asegurando que pertenezca a la sede indicada (acceso del personal).</summary>
    Task<PedidoFoto?> ObtenerAsync(int id, int sedeId, CancellationToken ct = default);
    /// <summary>Trae una foto asegurando que pertenezca al pedido indicado (acceso publico por token).</summary>
    Task<PedidoFoto?> ObtenerParaPedidoAsync(int id, int pedidoId, CancellationToken ct = default);
    /// <summary>Borra la fila y devuelve el metadato borrado (para eliminar el archivo del disco).</summary>
    Task<PedidoFoto?> EliminarAsync(int id, int sedeId, CancellationToken ct = default);
}

public class PedidoFotoRepository : IPedidoFotoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PedidoFotoRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string BaseSelect = @"
        SELECT Id, PedidoId, SedeId, NegocioId, Momento, NombreArchivo, ContentType,
               TamanoBytes, SubidoPorUsuarioId, FechaSubida
        FROM dbo.PedidoFoto";

    private static PedidoFoto Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
        SedeId = r.GetInt32(r.GetOrdinal("SedeId")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        Momento = r.GetString(r.GetOrdinal("Momento")),
        NombreArchivo = r.GetString(r.GetOrdinal("NombreArchivo")),
        ContentType = r.GetString(r.GetOrdinal("ContentType")),
        TamanoBytes = r.GetInt32(r.GetOrdinal("TamanoBytes")),
        SubidoPorUsuarioId = r.GetNullableInt("SubidoPorUsuarioId"),
        FechaSubida = r.GetDateTime(r.GetOrdinal("FechaSubida"))
    };

    public async Task<List<PedidoFoto>> ListarPorPedidoAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE PedidoId = @PedidoId AND SedeId = @SedeId ORDER BY FechaSubida";
        cmd.AddParam("@PedidoId", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<int> ContarPorPedidoAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.PedidoFoto WHERE PedidoId = @PedidoId AND SedeId = @SedeId";
        cmd.AddParam("@PedidoId", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<int> CrearAsync(PedidoFoto foto, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.PedidoFoto (PedidoId, SedeId, NegocioId, Momento, NombreArchivo, ContentType, TamanoBytes, SubidoPorUsuarioId)
            OUTPUT INSERTED.Id
            VALUES (@PedidoId, @SedeId, @NegocioId, @Momento, @NombreArchivo, @ContentType, @TamanoBytes, @SubidoPor);";
        cmd.AddParam("@PedidoId", foto.PedidoId);
        cmd.AddParam("@SedeId", foto.SedeId);
        cmd.AddParam("@NegocioId", foto.NegocioId);
        cmd.AddParam("@Momento", foto.Momento);
        cmd.AddParam("@NombreArchivo", foto.NombreArchivo);
        cmd.AddParam("@ContentType", foto.ContentType);
        cmd.AddParam("@TamanoBytes", foto.TamanoBytes);
        cmd.AddParam("@SubidoPor", (object?)foto.SubidoPorUsuarioId ?? DBNull.Value);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<PedidoFoto?> ObtenerAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<PedidoFoto?> ObtenerParaPedidoAsync(int id, int pedidoId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE Id = @Id AND PedidoId = @PedidoId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@PedidoId", pedidoId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<PedidoFoto?> EliminarAsync(int id, int sedeId, CancellationToken ct = default)
    {
        var foto = await ObtenerAsync(id, sedeId, ct);
        if (foto is null) return null;

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.PedidoFoto WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
        return foto;
    }
}
