using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IServicioRepository
{
    Task<List<Servicio>> ListarTodosAsync(int negocioId, CancellationToken ct = default);
    Task<List<Servicio>> ListarActivosAsync(int negocioId, CancellationToken ct = default);
    Task<Servicio?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task<bool> ExisteNombreAsync(string nombre, int negocioId, int? excluirId = null, CancellationToken ct = default);
    Task<int> CrearAsync(Servicio s, CancellationToken ct = default);
    Task ActualizarAsync(Servicio s, int negocioId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default);
    Task EliminarAsync(int id, int negocioId, CancellationToken ct = default);
    Task<int> ContarUsoAsync(int servicioId, int negocioId, CancellationToken ct = default);
    Task<Servicio?> ObtenerCargoDeliveryAsync(int negocioId, CancellationToken ct = default);
}

public class ServicioRepository : IServicioRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ServicioRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static Servicio MapConCategoria(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Precio = r.GetDecimal(r.GetOrdinal("Precio")),
        Costo = r.GetDecimal(r.GetOrdinal("Costo")),
        Unidad = r.GetString(r.GetOrdinal("Unidad")),
        CategoriaId = r.GetNullableInt("CategoriaId"),
        CategoriaNombre = r.GetNullableString("CategoriaNombre"),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        EsCargoDelivery = r.GetBoolean(r.GetOrdinal("EsCargoDelivery")),
        EnUso = r.GetBoolean(r.GetOrdinal("EnUso"))
    };

    private const string SelectConCategoria = @"
        SELECT s.Id, s.Nombre, s.Precio, s.Costo, s.Unidad, s.CategoriaId, cat.Nombre AS CategoriaNombre, s.Activo, s.EsCargoDelivery,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.PedidoItem pi WHERE pi.ServicioId = s.Id) THEN 1 ELSE 0 END AS BIT) AS EnUso
        FROM dbo.Servicio s
        LEFT JOIN dbo.Categoria cat ON cat.Id = s.CategoriaId";

    // El cargo de delivery es un servicio de sistema (ver 022_costo_delivery.sql): no debe
    // aparecer en el picker de Registrar ni en el CRUD de Ajustes > Servicios, solo se
    // administra su monto desde Ajustes > Negocio.
    public async Task<List<Servicio>> ListarActivosAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectConCategoria + " WHERE s.Activo = 1 AND s.NegocioId = @NegocioId AND s.EsCargoDelivery = 0 ORDER BY s.Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(MapConCategoria, ct);
    }

    public async Task<Servicio?> ObtenerCargoDeliveryAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectConCategoria + " WHERE s.NegocioId = @NegocioId AND s.EsCargoDelivery = 1";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(MapConCategoria, ct);
    }

    public async Task<Servicio?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectConCategoria + " WHERE s.Id = @Id AND s.NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(MapConCategoria, ct);
    }

    public async Task<bool> ExisteNombreAsync(string nombre, int negocioId, int? excluirId = null, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1
                  FROM dbo.Servicio
                 WHERE NegocioId = @NegocioId
                   AND EsCargoDelivery = 0
                   AND LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI
                   AND (@ExcluirId IS NULL OR Id <> @ExcluirId)
            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Nombre", nombre.Trim());
        cmd.AddParam("@ExcluirId", excluirId);
        return await cmd.ReadScalarAsync<bool>(ct);
    }

    public async Task<List<Servicio>> ListarTodosAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectConCategoria + " WHERE s.NegocioId = @NegocioId AND s.EsCargoDelivery = 0 ORDER BY s.Activo DESC, s.Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(MapConCategoria, ct);
    }

    public async Task<int> CrearAsync(Servicio s, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Servicio (NegocioId, Nombre, Precio, Costo, Unidad, CategoriaId, Activo, EsCargoDelivery)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Nombre, @Precio, @Costo, @Unidad, @CategoriaId, @Activo, @EsCargoDelivery)";
        cmd.AddParam("@NegocioId", s.NegocioId);
        cmd.AddParam("@Nombre", s.Nombre);
        cmd.AddParam("@Precio", s.Precio);
        cmd.AddParam("@Costo", s.Costo);
        cmd.AddParam("@Unidad", s.Unidad);
        cmd.AddParam("@CategoriaId", s.CategoriaId);
        cmd.AddParam("@Activo", s.Activo);
        cmd.AddParam("@EsCargoDelivery", s.EsCargoDelivery);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Servicio s, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Servicio
               SET Nombre = @Nombre, Precio = @Precio, Costo = @Costo, Unidad = @Unidad,
                   CategoriaId = @CategoriaId, Activo = @Activo
             WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", s.Id);
        cmd.AddParam("@Nombre", s.Nombre);
        cmd.AddParam("@Precio", s.Precio);
        cmd.AddParam("@Costo", s.Costo);
        cmd.AddParam("@Unidad", s.Unidad);
        cmd.AddParam("@CategoriaId", s.CategoriaId);
        cmd.AddParam("@Activo", s.Activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Servicio SET Activo = @Activo WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ContarUsoAsync(int servicioId, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM dbo.PedidoItem pi
            INNER JOIN dbo.Servicio s ON s.Id = pi.ServicioId
            WHERE pi.ServicioId = @Id AND s.NegocioId = @NegocioId";
        cmd.AddParam("@Id", servicioId);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task EliminarAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.Servicio WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public interface IAreaLavadoRepository
{
    Task<List<AreaLavado>> ListarActivasAsync(int sedeId, CancellationToken ct = default);
    Task<List<AreaLavado>> ListarTodasAsync(int sedeId, CancellationToken ct = default);
    Task<AreaLavado?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default);
    Task<bool> ExisteNombreAsync(string nombre, int sedeId, int? excluirId = null, CancellationToken ct = default);
    Task<bool> ExisteOrdenAsync(int orden, int sedeId, int? excluirId = null, CancellationToken ct = default);
    Task<int> CrearAsync(AreaLavado a, CancellationToken ct = default);
    Task ActualizarAsync(AreaLavado a, int sedeId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activa, int sedeId, CancellationToken ct = default);
    Task<int> ContarUsoAsync(int areaId, int sedeId, CancellationToken ct = default);
}

public class AreaLavadoRepository : IAreaLavadoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public AreaLavadoRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static AreaLavado Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Orden = r.GetInt32(r.GetOrdinal("Orden")),
        TiempoEstMinutos = r.GetInt32(r.GetOrdinal("TiempoEstMinutos")),
        Activa = r.GetBoolean(r.GetOrdinal("Activa"))
    };

    public async Task<List<AreaLavado>> ListarActivasAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Nombre, Orden, TiempoEstMinutos, Activa
            FROM dbo.AreaLavado
            WHERE Activa = 1 AND SedeId = @SedeId
            ORDER BY Orden";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<List<AreaLavado>> ListarTodasAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Nombre, Orden, TiempoEstMinutos, Activa,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Pedido p WHERE p.AreaActualId = a.Id)
                              OR EXISTS (SELECT 1 FROM dbo.PedidoHistorial ph WHERE ph.AreaId = a.Id)
                        THEN 1 ELSE 0 END AS BIT) AS EnUso
            FROM dbo.AreaLavado a WHERE a.SedeId = @SedeId ORDER BY a.Orden";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(r => { var a = Map(r); a.EnUso = r.GetBoolean(r.GetOrdinal("EnUso")); return a; }, ct);
    }

    public async Task<AreaLavado?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Nombre, Orden, TiempoEstMinutos, Activa FROM dbo.AreaLavado WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<bool> ExisteNombreAsync(string nombre, int sedeId, int? excluirId = null, CancellationToken ct = default)
        => await ExisteAsync("LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI = LTRIM(RTRIM(@Valor)) COLLATE Latin1_General_CI_AI", nombre.Trim(), sedeId, excluirId, ct);

    public async Task<bool> ExisteOrdenAsync(int orden, int sedeId, int? excluirId = null, CancellationToken ct = default)
        => await ExisteAsync("Orden = @Valor", orden, sedeId, excluirId, ct);

    private async Task<bool> ExisteAsync(string condicion, object valor, int sedeId, int? excluirId, CancellationToken ct)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.AreaLavado
                WHERE SedeId = @SedeId AND {condicion}
                  AND (@ExcluirId IS NULL OR Id <> @ExcluirId)
            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Valor", valor);
        cmd.AddParam("@ExcluirId", excluirId);
        return await cmd.ReadScalarAsync<bool>(ct);
    }

    public async Task<int> CrearAsync(AreaLavado a, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.AreaLavado (SedeId, Nombre, Orden, TiempoEstMinutos, Activa)
            OUTPUT INSERTED.Id
            VALUES (@SedeId, @Nombre, @Orden, @TiempoEstMinutos, @Activa)";
        cmd.AddParam("@SedeId", a.SedeId);
        cmd.AddParam("@Nombre", a.Nombre);
        cmd.AddParam("@Orden", a.Orden);
        cmd.AddParam("@TiempoEstMinutos", a.TiempoEstMinutos);
        cmd.AddParam("@Activa", a.Activa);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(AreaLavado a, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.AreaLavado
               SET Nombre = @Nombre, Orden = @Orden, TiempoEstMinutos = @TiempoEstMinutos, Activa = @Activa
             WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", a.Id);
        cmd.AddParam("@Nombre", a.Nombre);
        cmd.AddParam("@Orden", a.Orden);
        cmd.AddParam("@TiempoEstMinutos", a.TiempoEstMinutos);
        cmd.AddParam("@Activa", a.Activa);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activa, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.AreaLavado SET Activa = @Activa WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activa", activa);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ContarUsoAsync(int areaId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM dbo.Pedido WHERE AreaActualId = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", areaId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadScalarAsync<int>(ct);
    }
}
