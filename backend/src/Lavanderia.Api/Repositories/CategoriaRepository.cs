using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface ICategoriaRepository
{
    Task<List<Categoria>> ListarTodasAsync(int negocioId, CancellationToken ct = default);
    Task<Categoria?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task<bool> ExisteNombreAsync(string nombre, int negocioId, int? excluirId = null, CancellationToken ct = default);
    Task<int> CrearAsync(Categoria c, CancellationToken ct = default);
    Task ActualizarAsync(Categoria c, int negocioId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activa, int negocioId, CancellationToken ct = default);
    Task<int> ContarUsoAsync(int categoriaId, int negocioId, CancellationToken ct = default);
}

public class CategoriaRepository : ICategoriaRepository
{
    private readonly ISqlConnectionFactory _factory;
    public CategoriaRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static Categoria Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Activa = r.GetBoolean(r.GetOrdinal("Activa"))
    };

    public async Task<List<Categoria>> ListarTodasAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.Id, c.Nombre, c.Activa,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Servicio s WHERE s.CategoriaId = c.Id) THEN 1 ELSE 0 END AS BIT) AS EnUso
            FROM dbo.Categoria c WHERE c.NegocioId = @NegocioId ORDER BY c.Activa DESC, c.Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => { var c = Map(r); c.EnUso = r.GetBoolean(r.GetOrdinal("EnUso")); return c; }, ct);
    }

    public async Task<Categoria?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Nombre, Activa FROM dbo.Categoria WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<bool> ExisteNombreAsync(string nombre, int negocioId, int? excluirId = null, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Categoria
                WHERE NegocioId = @NegocioId
                  AND UPPER(LTRIM(RTRIM(Nombre))) = UPPER(@Nombre)
                  AND (@ExcluirId IS NULL OR Id <> @ExcluirId)
            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Nombre", nombre.Trim());
        cmd.AddParam("@ExcluirId", excluirId);
        return await cmd.ReadScalarAsync<bool>(ct);
    }

    public async Task<int> CrearAsync(Categoria c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Categoria (NegocioId, Nombre, Activa)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Nombre, @Activa)";
        cmd.AddParam("@NegocioId", c.NegocioId);
        cmd.AddParam("@Nombre", c.Nombre);
        cmd.AddParam("@Activa", c.Activa);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Categoria c, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Categoria SET Nombre = @Nombre, Activa = @Activa WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", c.Id);
        cmd.AddParam("@Nombre", c.Nombre);
        cmd.AddParam("@Activa", c.Activa);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activa, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Categoria SET Activa = @Activa WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activa", activa);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ContarUsoAsync(int categoriaId, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM dbo.Servicio WHERE CategoriaId = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", categoriaId);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }
}
