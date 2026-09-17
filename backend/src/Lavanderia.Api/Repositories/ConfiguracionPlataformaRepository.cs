using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public interface IConfiguracionPlataformaRepository
{
    Task<ConfiguracionPlataforma> ObtenerAsync(CancellationToken ct = default);
    Task ActualizarAsync(ConfiguracionPlataforma c, CancellationToken ct = default);
}

public class ConfiguracionPlataformaRepository : IConfiguracionPlataformaRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ConfiguracionPlataformaRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static ConfiguracionPlataforma Map(SqlDataReader r) => new()
    {
        NombrePlataforma = r.GetString(r.GetOrdinal("NombrePlataforma")),
        YapeNombre = r.GetNullableString("YapeNombre"),
        YapeNumero = r.GetNullableString("YapeNumero"),
        ContactoSoporte = r.GetNullableString("ContactoSoporte"),
        DiasAvisoCobro = r.GetInt32(r.GetOrdinal("DiasAvisoCobro"))
    };

    public async Task<ConfiguracionPlataforma> ObtenerAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT NombrePlataforma, YapeNombre, YapeNumero, ContactoSoporte, DiasAvisoCobro
            FROM dbo.ConfiguracionPlataforma WHERE Id = 1";
        return await cmd.ReadFirstOrDefaultAsync(Map, ct) ?? new ConfiguracionPlataforma();
    }

    public async Task ActualizarAsync(ConfiguracionPlataforma c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Upsert defensivo: si la fila única no existiera (BD vieja), la crea.
        cmd.CommandText = @"
            IF EXISTS (SELECT 1 FROM dbo.ConfiguracionPlataforma WHERE Id = 1)
                UPDATE dbo.ConfiguracionPlataforma
                   SET NombrePlataforma = @Nombre, YapeNombre = @YapeNombre, YapeNumero = @YapeNumero,
                       ContactoSoporte = @Contacto, DiasAvisoCobro = @Dias
                 WHERE Id = 1;
            ELSE
                INSERT INTO dbo.ConfiguracionPlataforma (Id, NombrePlataforma, YapeNombre, YapeNumero, ContactoSoporte, DiasAvisoCobro)
                VALUES (1, @Nombre, @YapeNombre, @YapeNumero, @Contacto, @Dias);";
        cmd.AddParam("@Nombre", c.NombrePlataforma);
        cmd.AddParam("@YapeNombre", c.YapeNombre);
        cmd.AddParam("@YapeNumero", c.YapeNumero);
        cmd.AddParam("@Contacto", c.ContactoSoporte);
        cmd.AddParam("@Dias", c.DiasAvisoCobro);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
