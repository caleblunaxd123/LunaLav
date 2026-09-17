using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IEmpleadoRepository
{
    Task<List<Empleado>> ListarTodosAsync(int sedeId, CancellationToken ct = default);
    Task<Empleado?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default);
    Task<bool> ExisteDniAsync(string dni, int sedeId, int? excluirId = null, CancellationToken ct = default);
    Task<bool> ExisteCelularAsync(string celular, int sedeId, int? excluirId = null, CancellationToken ct = default);
    Task<int> CrearAsync(Empleado e, CancellationToken ct = default);
    Task ActualizarAsync(Empleado e, int sedeId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activo, int sedeId, CancellationToken ct = default);
}

public class EmpleadoRepository : IEmpleadoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public EmpleadoRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static Empleado Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Dni = r.GetNullableString("Dni"),
        Celular = r.GetNullableString("Celular"),
        Cargo = r.GetNullableString("Cargo"),
        FechaIngreso = r.IsDBNull(r.GetOrdinal("FechaIngreso")) ? null : DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("FechaIngreso"))),
        Activo = r.GetBoolean(r.GetOrdinal("Activo"))
    };

    private const string Select = "SELECT Id, Nombre, Dni, Celular, Cargo, FechaIngreso, Activo FROM dbo.Empleado";

    public async Task<List<Empleado>> ListarTodosAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE SedeId = @SedeId ORDER BY Activo DESC, Nombre";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<Empleado?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public Task<bool> ExisteDniAsync(string dni, int sedeId, int? excluirId = null, CancellationToken ct = default)
        => ExisteDatoAsync("Dni", dni, sedeId, excluirId, ct);

    public Task<bool> ExisteCelularAsync(string celular, int sedeId, int? excluirId = null, CancellationToken ct = default)
        => ExisteDatoAsync("Celular", celular, sedeId, excluirId, ct);

    private async Task<bool> ExisteDatoAsync(string columna, string valor, int sedeId, int? excluirId, CancellationToken ct)
    {
        if (columna is not ("Dni" or "Celular"))
            throw new ArgumentOutOfRangeException(nameof(columna));
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Empleado
                WHERE SedeId = @SedeId AND {columna} = @Valor
                  AND (@ExcluirId IS NULL OR Id <> @ExcluirId)
            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Valor", valor);
        cmd.AddParam("@ExcluirId", excluirId);
        return await cmd.ReadScalarAsync<bool>(ct);
    }

    public async Task<int> CrearAsync(Empleado e, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Empleado (SedeId, Nombre, Dni, Celular, Cargo, FechaIngreso, Activo)
            OUTPUT INSERTED.Id
            VALUES (@SedeId, @Nombre, @Dni, @Celular, @Cargo, @FechaIngreso, @Activo)";
        cmd.AddParam("@SedeId", e.SedeId);
        cmd.AddParam("@Nombre", e.Nombre);
        cmd.AddParam("@Dni", e.Dni);
        cmd.AddParam("@Celular", e.Celular);
        cmd.AddParam("@Cargo", e.Cargo);
        cmd.AddParam("@FechaIngreso", e.FechaIngreso?.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("@Activo", e.Activo);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Empleado e, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Empleado
            SET Nombre = @Nombre, Dni = @Dni, Celular = @Celular, Cargo = @Cargo,
                FechaIngreso = @FechaIngreso, Activo = @Activo
            WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", e.Id);
        cmd.AddParam("@Nombre", e.Nombre);
        cmd.AddParam("@Dni", e.Dni);
        cmd.AddParam("@Celular", e.Celular);
        cmd.AddParam("@Cargo", e.Cargo);
        cmd.AddParam("@FechaIngreso", e.FechaIngreso?.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("@Activo", e.Activo);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Empleado SET Activo = @Activo WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
