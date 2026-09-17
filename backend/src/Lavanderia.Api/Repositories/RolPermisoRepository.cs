using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IRolPermisoRepository
{
    Task<List<RolPermiso>> ObtenerMatrizAsync(int negocioId, CancellationToken ct = default);
    Task<List<string>> ObtenerModulosPermitidosPorRolAsync(int rolId, int negocioId, CancellationToken ct = default);
    Task GuardarAsync(int rolId, string modulo, bool puedeAcceder, int negocioId, CancellationToken ct = default);
}

public class RolPermisoRepository : IRolPermisoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public RolPermisoRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<List<RolPermiso>> ObtenerMatrizAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT rp.Id, rp.RolId, r.Codigo AS RolCodigo, r.Nombre AS RolNombre, rp.Modulo, rp.PuedeAcceder
            FROM dbo.RolPermiso rp
            INNER JOIN dbo.Rol r ON r.Id = rp.RolId
            WHERE rp.NegocioId = @NegocioId
            ORDER BY r.Id, rp.Modulo";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => new RolPermiso
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            RolId = r.GetInt32(r.GetOrdinal("RolId")),
            RolCodigo = r.GetString(r.GetOrdinal("RolCodigo")),
            RolNombre = r.GetString(r.GetOrdinal("RolNombre")),
            Modulo = r.GetString(r.GetOrdinal("Modulo")),
            PuedeAcceder = r.GetBoolean(r.GetOrdinal("PuedeAcceder"))
        }, ct);
    }

    public async Task<List<string>> ObtenerModulosPermitidosPorRolAsync(int rolId, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Modulo FROM dbo.RolPermiso WHERE RolId = @RolId AND PuedeAcceder = 1 AND NegocioId = @NegocioId";
        cmd.AddParam("@RolId", rolId);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => r.GetString(0), ct);
    }

    public async Task GuardarAsync(int rolId, string modulo, bool puedeAcceder, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            MERGE dbo.RolPermiso AS target
            USING (SELECT @NegocioId AS NegocioId, @RolId AS RolId, @Modulo AS Modulo) AS src
              ON target.NegocioId = src.NegocioId AND target.RolId = src.RolId AND target.Modulo = src.Modulo
            WHEN MATCHED THEN UPDATE SET PuedeAcceder = @PuedeAcceder
            WHEN NOT MATCHED THEN INSERT (NegocioId, RolId, Modulo, PuedeAcceder) VALUES (@NegocioId, @RolId, @Modulo, @PuedeAcceder);";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@RolId", rolId);
        cmd.AddParam("@Modulo", modulo);
        cmd.AddParam("@PuedeAcceder", puedeAcceder);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
