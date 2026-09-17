using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IMotorizadoRepository
{
    Task<List<Motorizado>> ListarActivosAsync(int sedeId, CancellationToken ct = default);
    Task<List<Motorizado>> ListarTodosAsync(int sedeId, CancellationToken ct = default);
    Task<Motorizado?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default);
    Task<bool> ExisteCelularAsync(string celular, int sedeId, int? excluirId = null, CancellationToken ct = default);
    Task<int> CrearAsync(Motorizado m, CancellationToken ct = default);
    Task ActualizarAsync(Motorizado m, int sedeId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activo, int sedeId, CancellationToken ct = default);
}

public class MotorizadoRepository : IMotorizadoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public MotorizadoRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string BaseSelect = "SELECT Id, SedeId, Nombre, Celular, Activo FROM dbo.Motorizado";

    private static Motorizado Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        SedeId = r.GetInt32(r.GetOrdinal("SedeId")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Celular = r.GetNullableString("Celular"),
        Activo = r.GetBoolean(r.GetOrdinal("Activo"))
    };

    public async Task<List<Motorizado>> ListarActivosAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE Activo = 1 AND SedeId = @SedeId ORDER BY Nombre";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<List<Motorizado>> ListarTodosAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE SedeId = @SedeId ORDER BY Activo DESC, Nombre";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<Motorizado?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<bool> ExisteCelularAsync(string celular, int sedeId, int? excluirId = null, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Motorizado
                WHERE SedeId = @SedeId AND Celular = @Celular
                  AND (@ExcluirId IS NULL OR Id <> @ExcluirId)
            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Celular", celular);
        cmd.AddParam("@ExcluirId", excluirId);
        return await cmd.ReadScalarAsync<bool>(ct);
    }

    public async Task<int> CrearAsync(Motorizado m, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Motorizado (SedeId, Nombre, Celular, Activo)
            OUTPUT INSERTED.Id
            VALUES (@SedeId, @Nombre, @Celular, @Activo)";
        cmd.AddParam("@SedeId", m.SedeId);
        cmd.AddParam("@Nombre", m.Nombre);
        cmd.AddParam("@Celular", m.Celular);
        cmd.AddParam("@Activo", m.Activo);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Motorizado m, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Motorizado
               SET Nombre = @Nombre, Celular = @Celular, Activo = @Activo
             WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", m.Id);
        cmd.AddParam("@Nombre", m.Nombre);
        cmd.AddParam("@Celular", m.Celular);
        cmd.AddParam("@Activo", m.Activo);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Motorizado SET Activo = @Activo WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
