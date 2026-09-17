using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface INegocioRepository
{
    Task<int> CrearAsync(Negocio n, CancellationToken ct = default);
    Task<Negocio?> ObtenerPorIdAsync(int id, CancellationToken ct = default);
    Task<Negocio?> ObtenerPorSlugAsync(string slug, CancellationToken ct = default);
    /// <summary>Sin filtro de Activo: para resolver el negocio reservado de la plataforma (Activo=0 a proposito).</summary>
    Task<Negocio?> ObtenerPorSlugIncluyendoInactivoAsync(string slug, CancellationToken ct = default);
    Task<bool> ExisteSlugAsync(string slug, CancellationToken ct = default);
    Task<List<NegocioResumenDto>> ListarConConteosAsync(CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(int id, bool activo, CancellationToken ct = default);
    Task<PlataformaResumenDto> ObtenerResumenPlataformaAsync(CancellationToken ct = default);
    Task ActualizarDatosAsync(int id, string nombre, string? ruc, string? titularNombre, string? titularEmail, string? titularCelular, string? notas, CancellationToken ct = default);
    Task ActualizarSuscripcionAsync(int id, string plan, string estado, decimal monto, DateOnly? proximoPago, CancellationToken ct = default);
    Task<int> ContarPedidosMesAsync(int negocioId, CancellationToken ct = default);
}

public class NegocioRepository : INegocioRepository
{
    private readonly ISqlConnectionFactory _factory;
    public NegocioRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string Columnas = @"Id, Nombre, Slug, RucEmpresa, TitularNombre, TitularEmail, TitularCelular, Activo, FechaCreacion,
                                      PlanSuscripcion, EstadoSuscripcion, MontoMensual, ProximoPago, NotasInternas";

    private static DateOnly? LeerFechaOpcional(Microsoft.Data.SqlClient.SqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : DateOnly.FromDateTime(r.GetDateTime(ord));
    }

    private static Negocio Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Slug = r.GetString(r.GetOrdinal("Slug")),
        RucEmpresa = r.GetNullableString("RucEmpresa"),
        TitularNombre = r.GetNullableString("TitularNombre"),
        TitularEmail = r.GetNullableString("TitularEmail"),
        TitularCelular = r.GetNullableString("TitularCelular"),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion")),
        PlanSuscripcion = r.GetString(r.GetOrdinal("PlanSuscripcion")),
        EstadoSuscripcion = r.GetString(r.GetOrdinal("EstadoSuscripcion")),
        MontoMensual = r.GetDecimal(r.GetOrdinal("MontoMensual")),
        ProximoPago = LeerFechaOpcional(r, "ProximoPago"),
        NotasInternas = r.GetNullableString("NotasInternas")
    };

    public async Task<int> CrearAsync(Negocio n, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Negocio (Nombre, Slug, RucEmpresa, TitularNombre, TitularEmail, TitularCelular, Activo)
            OUTPUT INSERTED.Id
            VALUES (@Nombre, @Slug, @RucEmpresa, @TitularNombre, @TitularEmail, @TitularCelular, @Activo);";
        cmd.AddParam("@Nombre", n.Nombre);
        cmd.AddParam("@Slug", n.Slug);
        cmd.AddParam("@RucEmpresa", n.RucEmpresa);
        cmd.AddParam("@TitularNombre", n.TitularNombre);
        cmd.AddParam("@TitularEmail", n.TitularEmail);
        cmd.AddParam("@TitularCelular", n.TitularCelular);
        cmd.AddParam("@Activo", n.Activo);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<Negocio?> ObtenerPorIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Nombre, Slug, RucEmpresa, TitularNombre, TitularEmail, TitularCelular, Activo, FechaCreacion,
                   PlanSuscripcion, EstadoSuscripcion, MontoMensual, ProximoPago, NotasInternas
            FROM dbo.Negocio
            WHERE Id = @Id";
        cmd.AddParam("@Id", id);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Negocio?> ObtenerPorSlugAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Nombre, Slug, RucEmpresa, TitularNombre, TitularEmail, TitularCelular, Activo, FechaCreacion,
                   PlanSuscripcion, EstadoSuscripcion, MontoMensual, ProximoPago, NotasInternas
            FROM dbo.Negocio
            WHERE Slug = @Slug AND Activo = 1";
        cmd.AddParam("@Slug", slug);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Negocio?> ObtenerPorSlugIncluyendoInactivoAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Nombre, Slug, RucEmpresa, TitularNombre, TitularEmail, TitularCelular, Activo, FechaCreacion,
                   PlanSuscripcion, EstadoSuscripcion, MontoMensual, ProximoPago, NotasInternas
            FROM dbo.Negocio
            WHERE Slug = @Slug";
        cmd.AddParam("@Slug", slug);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    // A diferencia de ObtenerPorSlugAsync (que solo mira negocios activos, pensado para el
    // login), esta valida unicidad al crear un negocio nuevo: debe rechazar tambien un slug
    // ya usado por un negocio suspendido o por el negocio reservado de la plataforma.
    public async Task<bool> ExisteSlugAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM dbo.Negocio WHERE Slug = @Slug";
        cmd.AddParam("@Slug", slug);
        return await cmd.ReadScalarAsync<int>(ct) > 0;
    }

    // Excluye el negocio reservado de la plataforma (Slug = 'plataforma-interna'): no es un
    // cliente real y no debe aparecer en el panel de propietario.
    public async Task<List<NegocioResumenDto>> ListarConConteosAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Subconsultas en vez de GROUP BY: evita duplicar filas al contar sedes/usuarios/pedidos
        // por separado, y deja el conteo de pedidos del mes (via Sede) fácil de leer.
        cmd.CommandText = @"
            SELECT n.Id, n.Nombre, n.Slug, n.Activo, n.FechaCreacion,
                   n.PlanSuscripcion, n.EstadoSuscripcion, n.MontoMensual, n.ProximoPago,
                   (SELECT COUNT(*) FROM dbo.Sede s WHERE s.NegocioId = n.Id) AS CantidadSedes,
                   (SELECT COUNT(*) FROM dbo.Usuario u WHERE u.NegocioId = n.Id) AS CantidadUsuarios,
                   (SELECT MAX(u.UltimoAcceso) FROM dbo.Usuario u WHERE u.NegocioId = n.Id) AS UltimoAcceso,
                   (SELECT COUNT(*) FROM dbo.Pedido p
                      INNER JOIN dbo.Sede s ON s.Id = p.SedeId
                      WHERE s.NegocioId = n.Id AND p.Anulado = 0
                        AND YEAR(p.FechaIngreso) = YEAR(GETDATE())
                        AND MONTH(p.FechaIngreso) = MONTH(GETDATE())) AS PedidosMes
            FROM dbo.Negocio n
            WHERE n.Slug <> 'plataforma-interna'
            ORDER BY n.FechaCreacion DESC";
        return await cmd.ReadListAsync(r => new NegocioResumenDto(
            r.GetInt32(r.GetOrdinal("Id")),
            r.GetString(r.GetOrdinal("Nombre")),
            r.GetString(r.GetOrdinal("Slug")),
            r.GetBoolean(r.GetOrdinal("Activo")),
            r.GetDateTime(r.GetOrdinal("FechaCreacion")),
            r.GetInt32(r.GetOrdinal("CantidadSedes")),
            r.GetInt32(r.GetOrdinal("CantidadUsuarios")),
            r.GetString(r.GetOrdinal("PlanSuscripcion")),
            r.GetString(r.GetOrdinal("EstadoSuscripcion")),
            r.GetDecimal(r.GetOrdinal("MontoMensual")),
            LeerFechaOpcional(r, "ProximoPago"),
            r.GetNullableDateTime("UltimoAcceso"),
            r.GetInt32(r.GetOrdinal("PedidosMes"))
        ), ct);
    }

    public async Task<bool> CambiarEstadoAsync(int id, bool activo, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Negocio SET Activo = @Activo WHERE Id = @Id";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<PlataformaResumenDto> ObtenerResumenPlataformaAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Todo excluye el negocio reservado. "Ingreso recurrente" solo suma empresas activas y
        // con suscripcion vigente (no las suspendidas ni vencidas — esas no estan pagando).
        cmd.CommandText = @"
            SELECT
              (SELECT COUNT(*) FROM dbo.Negocio WHERE Slug <> 'plataforma-interna') AS TotalEmpresas,
              (SELECT COUNT(*) FROM dbo.Negocio WHERE Slug <> 'plataforma-interna' AND Activo = 1) AS EmpresasActivas,
              (SELECT COUNT(*) FROM dbo.Negocio WHERE Slug <> 'plataforma-interna' AND Activo = 0) AS EmpresasSuspendidas,
              (SELECT COUNT(*) FROM dbo.Negocio WHERE Slug <> 'plataforma-interna'
                 AND YEAR(FechaCreacion) = YEAR(GETDATE()) AND MONTH(FechaCreacion) = MONTH(GETDATE())) AS EmpresasNuevasMes,
              (SELECT ISNULL(SUM(MontoMensual), 0) FROM dbo.Negocio
                 WHERE Slug <> 'plataforma-interna' AND Activo = 1 AND EstadoSuscripcion IN ('ACTIVA','PRUEBA')) AS IngresoMensualRecurrente,
              (SELECT COUNT(*) FROM dbo.Pedido p INNER JOIN dbo.Sede s ON s.Id = p.SedeId
                 INNER JOIN dbo.Negocio n ON n.Id = s.NegocioId
                 WHERE n.Slug <> 'plataforma-interna' AND p.Anulado = 0
                   AND YEAR(p.FechaIngreso) = YEAR(GETDATE()) AND MONTH(p.FechaIngreso) = MONTH(GETDATE())) AS PedidosMesTotal,
              (SELECT COUNT(*) FROM dbo.Negocio WHERE Slug <> 'plataforma-interna' AND Activo = 1
                 AND ProximoPago IS NOT NULL AND ProximoPago BETWEEN CAST(GETDATE() AS DATE) AND DATEADD(DAY, 7, CAST(GETDATE() AS DATE))) AS EmpresasPorVencer,
              (SELECT COUNT(*) FROM dbo.Negocio WHERE Slug <> 'plataforma-interna'
                 AND (EstadoSuscripcion = 'VENCIDA' OR (ProximoPago IS NOT NULL AND ProximoPago < CAST(GETDATE() AS DATE)))) AS EmpresasVencidas,
              (SELECT ISNULL(SUM(ps.Monto), 0) FROM dbo.PagoSuscripcion ps
                 INNER JOIN dbo.Negocio n ON n.Id = ps.NegocioId
                 WHERE n.Slug <> 'plataforma-interna'
                   AND YEAR(ps.Fecha) = YEAR(GETDATE()) AND MONTH(ps.Fecha) = MONTH(GETDATE())) AS RecaudadoMes";
        return await cmd.ReadFirstOrDefaultAsync(r => new PlataformaResumenDto(
            r.GetInt32(r.GetOrdinal("TotalEmpresas")),
            r.GetInt32(r.GetOrdinal("EmpresasActivas")),
            r.GetInt32(r.GetOrdinal("EmpresasSuspendidas")),
            r.GetInt32(r.GetOrdinal("EmpresasNuevasMes")),
            r.GetDecimal(r.GetOrdinal("IngresoMensualRecurrente")),
            r.GetInt32(r.GetOrdinal("PedidosMesTotal")),
            r.GetInt32(r.GetOrdinal("EmpresasPorVencer")),
            r.GetInt32(r.GetOrdinal("EmpresasVencidas")),
            r.GetDecimal(r.GetOrdinal("RecaudadoMes"))
        ), ct) ?? new PlataformaResumenDto(0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    public async Task ActualizarDatosAsync(int id, string nombre, string? ruc, string? titularNombre, string? titularEmail, string? titularCelular, string? notas, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Negocio
               SET Nombre = @Nombre, RucEmpresa = @Ruc, TitularNombre = @TitularNombre,
                   TitularEmail = @TitularEmail, TitularCelular = @TitularCelular, NotasInternas = @Notas
             WHERE Id = @Id AND Slug <> 'plataforma-interna'";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Nombre", nombre);
        cmd.AddParam("@Ruc", ruc);
        cmd.AddParam("@TitularNombre", titularNombre);
        cmd.AddParam("@TitularEmail", titularEmail);
        cmd.AddParam("@TitularCelular", titularCelular);
        cmd.AddParam("@Notas", notas);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ActualizarSuscripcionAsync(int id, string plan, string estado, decimal monto, DateOnly? proximoPago, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Negocio
               SET PlanSuscripcion = @Plan, EstadoSuscripcion = @Estado,
                   MontoMensual = @Monto, ProximoPago = @ProximoPago
             WHERE Id = @Id AND Slug <> 'plataforma-interna'";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Plan", plan);
        cmd.AddParam("@Estado", estado);
        cmd.AddParam("@Monto", monto);
        cmd.AddParam("@ProximoPago", proximoPago.HasValue ? proximoPago.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ContarPedidosMesAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM dbo.Pedido p
            INNER JOIN dbo.Sede s ON s.Id = p.SedeId
            WHERE s.NegocioId = @NegocioId AND p.Anulado = 0
              AND YEAR(p.FechaIngreso) = YEAR(GETDATE()) AND MONTH(p.FechaIngreso) = MONTH(GETDATE())";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }
}

public interface ISedeRepository
{
    Task<Sede?> ObtenerPorIdAsync(int id, CancellationToken ct = default);
    Task<List<Sede>> ListarPorNegocioAsync(int negocioId, CancellationToken ct = default);
    Task<bool> ExisteNombreAsync(string nombre, int negocioId, int? excluirId = null, CancellationToken ct = default);
    Task<int> CrearAsync(Sede s, CancellationToken ct = default);
    Task ActualizarAsync(Sede s, int negocioId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default);
}

public class SedeRepository : ISedeRepository
{
    private readonly ISqlConnectionFactory _factory;
    public SedeRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string BaseSelect = @"
        SELECT Id, NegocioId, Nombre, Direccion, Telefono, Activo, FechaCreacion
        FROM dbo.Sede";

    private static Sede Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Direccion = r.GetNullableString("Direccion"),
        Telefono = r.GetNullableString("Telefono"),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion"))
    };

    public async Task<Sede?> ObtenerPorIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE Id = @Id";
        cmd.AddParam("@Id", id);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<List<Sede>> ListarPorNegocioAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE NegocioId = @NegocioId ORDER BY Activo DESC, Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<bool> ExisteNombreAsync(string nombre, int negocioId, int? excluirId = null, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Sede
                WHERE NegocioId = @NegocioId
                  AND UPPER(LTRIM(RTRIM(Nombre))) = UPPER(@Nombre)
                  AND (@ExcluirId IS NULL OR Id <> @ExcluirId)
            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Nombre", nombre.Trim());
        cmd.AddParam("@ExcluirId", excluirId);
        return await cmd.ReadScalarAsync<bool>(ct);
    }

    public async Task<int> CrearAsync(Sede s, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO dbo.Sede (NegocioId, Nombre, Direccion, Telefono, Activo)
                OUTPUT INSERTED.Id
                VALUES (@NegocioId, @Nombre, @Direccion, @Telefono, @Activo);";
            cmd.AddParam("@NegocioId", s.NegocioId);
            cmd.AddParam("@Nombre", s.Nombre);
            cmd.AddParam("@Direccion", s.Direccion);
            cmd.AddParam("@Telefono", s.Telefono);
            cmd.AddParam("@Activo", s.Activo);
            var sedeId = await cmd.ReadScalarAsync<int>(ct);

            // Toda sede nace con el flujo estándar de áreas: sin esto los pedidos se crean
            // pero no pueden avanzar ("No hay áreas de lavado configuradas"). El dueño puede
            // renombrarlas o desactivarlas después desde Ajustes → Áreas de lavado.
            await using var cmdAreas = conn.CreateCommand();
            cmdAreas.Transaction = tx;
            cmdAreas.CommandText = @"
                INSERT INTO dbo.AreaLavado (SedeId, Nombre, Orden, TiempoEstMinutos, Activa) VALUES
                    (@SedeId, N'Recepcion', 1, 15, 1),
                    (@SedeId, N'Lavado', 2, 60, 1),
                    (@SedeId, N'Secado', 3, 45, 1),
                    (@SedeId, N'Doblado', 4, 20, 1),
                    (@SedeId, N'Control de calidad', 5, 10, 1),
                    (@SedeId, N'Empacado', 6, 5, 1);";
            cmdAreas.AddParam("@SedeId", sedeId);
            await cmdAreas.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
            return sedeId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task ActualizarAsync(Sede s, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Sede
               SET Nombre = @Nombre, Direccion = @Direccion, Telefono = @Telefono, Activo = @Activo
             WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", s.Id);
        cmd.AddParam("@Nombre", s.Nombre);
        cmd.AddParam("@Direccion", s.Direccion);
        cmd.AddParam("@Telefono", s.Telefono);
        cmd.AddParam("@Activo", s.Activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Sede SET Activo = @Activo WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
