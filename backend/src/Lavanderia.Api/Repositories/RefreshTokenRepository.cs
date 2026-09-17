using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public record RefreshTokenInfo(int Id, int UsuarioId, int? SedeId, bool Revocado, DateTime FechaExpiracion);

public interface IRefreshTokenRepository
{
    Task CrearAsync(int usuarioId, int? sedeId, string tokenHash, DateTime fechaExpiracion, CancellationToken ct = default);
    Task<RefreshTokenInfo?> ObtenerPorHashAsync(string tokenHash, CancellationToken ct = default);
    /// <summary>Revoca de forma atomica solo si seguia vigente (idempotente ante reintentos).</summary>
    Task<bool> RevocarAsync(string tokenHash, CancellationToken ct = default);
}

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ISqlConnectionFactory _factory;
    public RefreshTokenRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task CrearAsync(int usuarioId, int? sedeId, string tokenHash, DateTime fechaExpiracion, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.RefreshToken (UsuarioId, SedeId, TokenHash, FechaExpiracion)
            VALUES (@UsuarioId, @SedeId, @TokenHash, @FechaExpiracion);";
        cmd.AddParam("@UsuarioId", usuarioId);
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@TokenHash", tokenHash);
        cmd.AddParam("@FechaExpiracion", fechaExpiracion);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<RefreshTokenInfo?> ObtenerPorHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, UsuarioId, SedeId, Revocado, FechaExpiracion
            FROM dbo.RefreshToken
            WHERE TokenHash = @TokenHash";
        cmd.AddParam("@TokenHash", tokenHash);
        return await cmd.ReadFirstOrDefaultAsync(r =>
        {
            var sedeOrdinal = r.GetOrdinal("SedeId");
            return new RefreshTokenInfo(
                r.GetInt32(r.GetOrdinal("Id")),
                r.GetInt32(r.GetOrdinal("UsuarioId")),
                r.IsDBNull(sedeOrdinal) ? null : r.GetInt32(sedeOrdinal),
                r.GetBoolean(r.GetOrdinal("Revocado")),
                r.GetDateTime(r.GetOrdinal("FechaExpiracion"))
            );
        }, ct);
    }

    public async Task<bool> RevocarAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.RefreshToken
               SET Revocado = 1, FechaRevocado = SYSDATETIME()
             WHERE TokenHash = @TokenHash AND Revocado = 0";
        cmd.AddParam("@TokenHash", tokenHash);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
