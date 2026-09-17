using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IPromocionRepository
{
    Task<List<Promocion>> ListarTodasAsync(int negocioId, CancellationToken ct = default);
    Task<Promocion?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task<int> CrearAsync(Promocion p, CancellationToken ct = default);
    Task ActualizarAsync(Promocion p, int negocioId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activa, int negocioId, CancellationToken ct = default);
    Task EliminarAsync(int id, int negocioId, CancellationToken ct = default);
    Task<Promocion?> BuscarPorCodigoAsync(string codigo, int negocioId, CancellationToken ct = default);
    Task<bool> ConsumirPorCodigoAsync(string codigo, int negocioId, int? clienteId, CancellationToken ct = default);
    Task<string> GenerarCodigoUnicoAsync(string prefijo, int negocioId, CancellationToken ct = default);
}

public class PromocionRepository : IPromocionRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PromocionRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static Promocion Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Tipo = r.GetString(r.GetOrdinal("Tipo")),
        Descripcion = r.GetString(r.GetOrdinal("Descripcion")),
        DescuentoPct = r.GetNullableDecimal("DescuentoPct"),
        DescuentoMonto = r.GetNullableDecimal("DescuentoMonto"),
        ServicioId = r.GetNullableInt("ServicioId"),
        ServicioNombre = r.GetNullableString("ServicioNombre"),
        CantidadMinima = r.GetDecimal(r.GetOrdinal("CantidadMinima")),
        FechaInicio = r.IsDBNull(r.GetOrdinal("FechaInicio")) ? null : DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("FechaInicio"))),
        FechaFin = r.IsDBNull(r.GetOrdinal("FechaFin")) ? null : DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("FechaFin"))),
        Activa = r.GetBoolean(r.GetOrdinal("Activa")),
        Codigo = r.GetNullableString("Codigo"),
        ClienteId = r.GetNullableInt("ClienteId"),
        ClienteNombre = r.GetNullableString("ClienteNombre"),
        Origen = r.GetNullableString("Origen"),
        MaxUsos = r.GetNullableInt("MaxUsos"),
        Usos = r.GetInt32(r.GetOrdinal("Usos"))
    };

    public async Task<List<Promocion>> ListarTodasAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.Tipo, p.Descripcion, p.DescuentoPct, p.DescuentoMonto,
                   p.ServicioId, s.Nombre AS ServicioNombre, p.CantidadMinima,
                   p.FechaInicio, p.FechaFin, p.Activa, p.Codigo,
                   p.ClienteId, cl.Nombre AS ClienteNombre, p.Origen, p.MaxUsos, p.Usos
            FROM dbo.Promocion p
            LEFT JOIN dbo.Servicio s ON s.Id = p.ServicioId
            LEFT JOIN dbo.Cliente cl ON cl.Id = p.ClienteId
            WHERE p.NegocioId = @NegocioId
            ORDER BY p.Activa DESC, p.Id DESC";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<Promocion?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.Tipo, p.Descripcion, p.DescuentoPct, p.DescuentoMonto,
                   p.ServicioId, s.Nombre AS ServicioNombre, p.CantidadMinima,
                   p.FechaInicio, p.FechaFin, p.Activa, p.Codigo,
                   p.ClienteId, cl.Nombre AS ClienteNombre, p.Origen, p.MaxUsos, p.Usos
            FROM dbo.Promocion p
            LEFT JOIN dbo.Servicio s ON s.Id = p.ServicioId
            LEFT JOIN dbo.Cliente cl ON cl.Id = p.ClienteId
            WHERE p.Id = @Id AND p.NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<int> CrearAsync(Promocion p, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Promocion
                (NegocioId, Tipo, Descripcion, DescuentoPct, DescuentoMonto, ServicioId, CantidadMinima, FechaInicio, FechaFin, Activa, Codigo, ClienteId, Origen, MaxUsos, Usos)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Tipo, @Descripcion, @DescuentoPct, @DescuentoMonto, @ServicioId, @CantidadMinima, @FechaInicio, @FechaFin, @Activa, @Codigo, @ClienteId, @Origen, @MaxUsos, @Usos)";
        cmd.AddParam("@NegocioId", p.NegocioId);
        AddParams(cmd, p);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Promocion p, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Promocion
               SET Tipo = @Tipo, Descripcion = @Descripcion, DescuentoPct = @DescuentoPct,
                   DescuentoMonto = @DescuentoMonto, ServicioId = @ServicioId, CantidadMinima = @CantidadMinima,
                   FechaInicio = @FechaInicio, FechaFin = @FechaFin, Activa = @Activa, Codigo = @Codigo,
                   MaxUsos = @MaxUsos
             WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", p.Id);
        cmd.AddParam("@NegocioId", negocioId);
        AddParams(cmd, p);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activa, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Promocion SET Activa = @Activa WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activa", activa);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Antes borraba el registro (DELETE). Se cambia a soft-delete (mismo efecto visible: la
    // promocion deja de listarse/usarse) porque un borrado fisico pierde para siempre el
    // historico de que promociones existieron/se aplicaron. El nombre/contrato del metodo no
    // cambia para no tocar el controller ni el frontend.
    public async Task EliminarAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Promocion SET Activa = 0 WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParams(Microsoft.Data.SqlClient.SqlCommand cmd, Promocion p)
    {
        cmd.AddParam("@Tipo", p.Tipo);
        cmd.AddParam("@Descripcion", p.Descripcion);
        cmd.AddParam("@DescuentoPct", p.DescuentoPct);
        cmd.AddParam("@DescuentoMonto", p.DescuentoMonto);
        cmd.AddParam("@ServicioId", p.ServicioId);
        cmd.AddParam("@CantidadMinima", p.CantidadMinima);
        cmd.AddParam("@FechaInicio", p.FechaInicio.HasValue ? p.FechaInicio.Value.ToDateTime(TimeOnly.MinValue) : (object?)null);
        cmd.AddParam("@FechaFin", p.FechaFin.HasValue ? p.FechaFin.Value.ToDateTime(TimeOnly.MinValue) : (object?)null);
        cmd.AddParam("@Activa", p.Activa);
        cmd.AddParam("@Codigo", string.IsNullOrWhiteSpace(p.Codigo) ? null : p.Codigo.Trim().ToUpperInvariant());
        cmd.AddParam("@ClienteId", p.ClienteId);
        cmd.AddParam("@Origen", string.IsNullOrWhiteSpace(p.Origen) ? null : p.Origen.Trim().ToUpperInvariant());
        cmd.AddParam("@MaxUsos", p.MaxUsos);
        cmd.AddParam("@Usos", p.Usos);
    }

    /// <summary>
    /// Registra un canje del código de forma atómica: incrementa Usos SOLO si el código sigue
    /// vigente (activo y sin agotar) y —si es personal— corresponde al cliente. Desactiva el
    /// código al alcanzar el tope. Devuelve true si se consumió. Un código de marketing sin tope
    /// (MaxUsos NULL) también incrementa su contador pero nunca se agota.
    /// </summary>
    public async Task<bool> ConsumirPorCodigoAsync(string codigo, int negocioId, int? clienteId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Promocion
               SET Usos = Usos + 1,
                   Activa = CASE WHEN MaxUsos IS NOT NULL AND Usos + 1 >= MaxUsos THEN 0 ELSE Activa END
             WHERE Codigo = @Codigo AND NegocioId = @NegocioId AND Activa = 1
               AND (MaxUsos IS NULL OR Usos < MaxUsos)
               AND (ClienteId IS NULL OR ClienteId = @ClienteId)";
        cmd.AddParam("@Codigo", codigo.Trim().ToUpperInvariant());
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@ClienteId", clienteId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>Genera un código único para el negocio (prefijo por origen + nombre + sufijo aleatorio).</summary>
    public async Task<string> GenerarCodigoUnicoAsync(string prefijo, int negocioId, CancellationToken ct = default)
    {
        const string abc = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sin O/0/I/1 para evitar confusiones al dictarlo
        for (var intento = 0; intento < 20; intento++)
        {
            var sufijo = new string(Enumerable.Range(0, 4)
                .Select(_ => abc[System.Security.Cryptography.RandomNumberGenerator.GetInt32(abc.Length)]).ToArray());
            var codigo = $"{prefijo}-{sufijo}";
            if (await BuscarPorCodigoAsync(codigo, negocioId, ct) is null)
                return codigo;
        }
        // Fallback prácticamente imposible: agrega más entropía
        return $"{prefijo}-{Guid.NewGuid():N}"[..Math.Min(29, prefijo.Length + 33)].ToUpperInvariant();
    }

    public async Task<Promocion?> BuscarPorCodigoAsync(string codigo, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.Tipo, p.Descripcion, p.DescuentoPct, p.DescuentoMonto,
                   p.ServicioId, s.Nombre AS ServicioNombre, p.CantidadMinima,
                   p.FechaInicio, p.FechaFin, p.Activa, p.Codigo,
                   p.ClienteId, cl.Nombre AS ClienteNombre, p.Origen, p.MaxUsos, p.Usos
            FROM dbo.Promocion p
            LEFT JOIN dbo.Servicio s ON s.Id = p.ServicioId
            LEFT JOIN dbo.Cliente cl ON cl.Id = p.ClienteId
            WHERE p.Codigo = @Codigo AND p.NegocioId = @NegocioId";
        cmd.AddParam("@Codigo", codigo.Trim().ToUpperInvariant());
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }
}
