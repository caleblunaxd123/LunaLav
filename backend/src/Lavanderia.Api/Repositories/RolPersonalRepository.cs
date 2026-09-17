using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IRolPersonalRepository
{
    Task<List<RolPersonal>> ListarTodosAsync(int negocioId, CancellationToken ct = default);
    Task<RolPersonal?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task<bool> ExisteNombreAsync(string nombre, int negocioId, int? excluirId = null, CancellationToken ct = default);
    Task<int> CrearAsync(RolPersonal r, CancellationToken ct = default);
    Task ActualizarAsync(RolPersonal r, int negocioId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default);
}

public class RolPersonalRepository : IRolPersonalRepository
{
    private readonly ISqlConnectionFactory _factory;
    public RolPersonalRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static RolPersonal Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Activo = r.GetBoolean(r.GetOrdinal("Activo"))
    };

    public async Task<List<RolPersonal>> ListarTodosAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Nombre, Activo FROM dbo.RolPersonal WHERE NegocioId = @NegocioId ORDER BY Activo DESC, Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<RolPersonal?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Nombre, Activo FROM dbo.RolPersonal WHERE Id = @Id AND NegocioId = @NegocioId";
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
                SELECT 1 FROM dbo.RolPersonal
                WHERE NegocioId = @NegocioId
                  AND UPPER(LTRIM(RTRIM(Nombre))) = UPPER(@Nombre)
                  AND (@ExcluirId IS NULL OR Id <> @ExcluirId)
            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Nombre", nombre.Trim());
        cmd.AddParam("@ExcluirId", excluirId);
        return await cmd.ReadScalarAsync<bool>(ct);
    }

    public async Task<int> CrearAsync(RolPersonal r, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.RolPersonal (NegocioId, Nombre, Activo)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Nombre, @Activo)";
        cmd.AddParam("@NegocioId", r.NegocioId);
        cmd.AddParam("@Nombre", r.Nombre);
        cmd.AddParam("@Activo", r.Activo);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(RolPersonal r, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.RolPersonal SET Nombre = @Nombre, Activo = @Activo WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", r.Id);
        cmd.AddParam("@Nombre", r.Nombre);
        cmd.AddParam("@Activo", r.Activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.RolPersonal SET Activo = @Activo WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
