using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public interface IPagoSuscripcionRepository
{
    Task<int> CrearAsync(PagoSuscripcion p, CancellationToken ct = default);
    Task<List<PagoSuscripcion>> ListarPorNegocioAsync(int negocioId, CancellationToken ct = default);
    Task<PagoSuscripcion?> ObtenerAsync(int id, int negocioId, CancellationToken ct = default);
}

public class PagoSuscripcionRepository : IPagoSuscripcionRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PagoSuscripcionRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string BaseSelect = @"
        SELECT Id, NegocioId, Fecha, Monto, Metodo, PeriodoDesde, PeriodoHasta, Nota, RegistradoPorUsuarioId, FechaCreacion
        FROM dbo.PagoSuscripcion";

    private static DateOnly? FechaOpcional(SqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : DateOnly.FromDateTime(r.GetDateTime(ord));
    }

    private static object ParamFecha(DateOnly? d) => d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : (object)DBNull.Value;

    private static PagoSuscripcion Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        Fecha = DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("Fecha"))),
        Monto = r.GetDecimal(r.GetOrdinal("Monto")),
        Metodo = r.GetString(r.GetOrdinal("Metodo")),
        PeriodoDesde = FechaOpcional(r, "PeriodoDesde"),
        PeriodoHasta = FechaOpcional(r, "PeriodoHasta"),
        Nota = r.GetNullableString("Nota"),
        RegistradoPorUsuarioId = r.IsDBNull(r.GetOrdinal("RegistradoPorUsuarioId")) ? null : r.GetInt32(r.GetOrdinal("RegistradoPorUsuarioId")),
        FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion"))
    };

    public async Task<int> CrearAsync(PagoSuscripcion p, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.PagoSuscripcion (NegocioId, Fecha, Monto, Metodo, PeriodoDesde, PeriodoHasta, Nota, RegistradoPorUsuarioId)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Fecha, @Monto, @Metodo, @PeriodoDesde, @PeriodoHasta, @Nota, @RegistradoPor);";
        cmd.AddParam("@NegocioId", p.NegocioId);
        cmd.AddParam("@Fecha", p.Fecha.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("@Monto", p.Monto);
        cmd.AddParam("@Metodo", p.Metodo);
        cmd.AddParam("@PeriodoDesde", ParamFecha(p.PeriodoDesde));
        cmd.AddParam("@PeriodoHasta", ParamFecha(p.PeriodoHasta));
        cmd.AddParam("@Nota", p.Nota);
        cmd.AddParam("@RegistradoPor", (object?)p.RegistradoPorUsuarioId ?? DBNull.Value);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<List<PagoSuscripcion>> ListarPorNegocioAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE NegocioId = @NegocioId ORDER BY Fecha DESC, Id DESC";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<PagoSuscripcion?> ObtenerAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }
}
