using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Services;

public record EtapaEmbudoDto(string Codigo, int Total);
public record NegocioMetricaDto(string Slug, DateTime FechaCreacion, string Plan, string Estado,
    int? DiasRestantesPrueba, bool PrimerLogin, int PedidosTotales, DateTime? UltimoPedidoFecha,
    string MotivoAbandono);
public record NegocioExcluidoDto(string Slug, string Motivo);
public record MetricasPlataformaDto(List<EtapaEmbudoDto> Embudo, int Excluidos,
    int RegistrosConfirmadosAutonomos, int SuscripcionesTest, int NegociosReales,
    int OnboardingCompleto, int PrimerPedido, int ActivosEnPrueba, int Pagando,
    decimal Mrr, decimal ConversionPruebaPago, List<NegocioMetricaDto> Negocios,
    List<NegocioExcluidoDto> NegociosExcluidos);

public record DatosNegocioMetrica(int Id, string Slug, bool Activo, string? TitularEmail,
    DateTime FechaCreacion, string Plan, string Estado, decimal MontoMensual, DateOnly? ProximoPago,
    bool AltaAutonoma, DateTime? UltimoAcceso, bool TieneLogo, bool TieneServicio,
    int PedidosTotales, DateTime? UltimoPedido, bool TienePago, bool TienePagoReal,
    string? CulqiEstado, string? CulqiModo);

/// <summary>Reglas puras de clasificación y cálculo; no conservan correos.</summary>
public static class MetricasPlataformaReglas
{
    public static string? MotivoExclusion(DatosNegocioMetrica n, IReadOnlyCollection<string> slugsExcluidos)
    {
        if (!n.Activo) return "inactivo";
        if (n.Slug.Equals("demo", StringComparison.OrdinalIgnoreCase)) return "demo";
        if (n.Slug.Equals("plataforma-interna", StringComparison.OrdinalIgnoreCase)) return "plataforma_interna";
        if (n.Slug.StartsWith("qa", StringComparison.OrdinalIgnoreCase)) return "qa";
        if (n.Slug.Contains("test", StringComparison.OrdinalIgnoreCase)) return "test";
        if (n.TitularEmail?.Contains("@example.", StringComparison.OrdinalIgnoreCase) == true) return "email_ejemplo";
        if (slugsExcluidos.Contains(n.Slug, StringComparer.OrdinalIgnoreCase)) return "configurado";
        return null;
    }

    public static bool Pagando(DatosNegocioMetrica n) =>
        n.Estado.Equals("ACTIVA", StringComparison.OrdinalIgnoreCase) &&
        (n.TienePagoReal || SuscripcionLive(n));

    public static bool SuscripcionLive(DatosNegocioMetrica n) =>
        string.Equals(n.CulqiEstado, "ACTIVA", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(n.CulqiModo, "LIVE", StringComparison.OrdinalIgnoreCase);

    public static bool PruebaActiva(DatosNegocioMetrica n, DateTime ahora) =>
        n.Estado.Equals("PRUEBA", StringComparison.OrdinalIgnoreCase) &&
        n.ProximoPago is DateOnly fin && fin >= DateOnly.FromDateTime(ahora) &&
        (n.UltimoAcceso >= ahora.AddDays(-7) || n.UltimoPedido >= ahora.AddDays(-7));

    public static string MotivoAbandono(DatosNegocioMetrica n, DateTime ahora)
    {
        if (Pagando(n)) return "convertido";
        if (n.Estado.Equals("PRUEBA", StringComparison.OrdinalIgnoreCase) &&
            n.ProximoPago is DateOnly fin && fin < DateOnly.FromDateTime(ahora))
            return "prueba_vencida_sin_pago";
        if (n.UltimoAcceso is null) return "no_login";
        if (n.PedidosTotales == 0) return "sin_pedidos";
        if (n.UltimoAcceso < ahora.AddDays(-7) && n.UltimoPedido < ahora.AddDays(-7)) return "inactivo_7d";
        return "activo";
    }

    public static decimal Conversion(int pagando, int elegibles) => elegibles == 0 ? 0 :
        Math.Round(100m * pagando / elegibles, 2);

    public static decimal Mrr(IEnumerable<DatosNegocioMetrica> negocios) =>
        negocios.Where(Pagando).Sum(n => n.MontoMensual);
}

public class MetricasPlataformaService
{
    private readonly ISqlConnectionFactory _db;
    private readonly IConfiguration _config;
    public MetricasPlataformaService(ISqlConnectionFactory db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<MetricasPlataformaDto> ObtenerAsync(CancellationToken ct = default)
    {
        var ahora = DateTime.Now; // Las fechas operativas y ProximoPago se guardan en hora local del servidor.
        var excluidos = _config.GetSection("Metricas:SlugsExcluidos").Get<string[]>() ?? [];
        await using var conn = _db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, n.Slug, n.Activo, n.TitularEmail, n.FechaCreacion,
                   n.PlanSuscripcion, n.EstadoSuscripcion, n.MontoMensual, n.ProximoPago,
                   CASE WHEN n.NotasInternas LIKE N'Alta autónoma%' THEN 1 ELSE 0 END AS AltaAutonoma,
                   (SELECT MAX(u.UltimoAcceso) FROM dbo.Usuario u WHERE u.NegocioId = n.Id) AS UltimoAcceso,
                   CASE WHEN EXISTS (SELECT 1 FROM dbo.ConfiguracionNegocio c WHERE c.NegocioId = n.Id
                       AND NULLIF(LTRIM(RTRIM(c.LogoUrl)), '') IS NOT NULL) THEN 1 ELSE 0 END AS TieneLogo,
                   CASE WHEN EXISTS (SELECT 1 FROM dbo.Servicio v WHERE v.NegocioId = n.Id
                       AND v.Activo = 1 AND v.EsCargoDelivery = 0) THEN 1 ELSE 0 END AS TieneServicio,
                   (SELECT COUNT(*) FROM dbo.Pedido p JOIN dbo.Sede s ON s.Id = p.SedeId
                       WHERE s.NegocioId = n.Id) AS PedidosTotales,
                   (SELECT MAX(p.FechaIngreso) FROM dbo.Pedido p JOIN dbo.Sede s ON s.Id = p.SedeId
                       WHERE s.NegocioId = n.Id) AS UltimoPedido,
                   CASE WHEN EXISTS (SELECT 1 FROM dbo.PagoSuscripcion ps WHERE ps.NegocioId = n.Id)
                       THEN 1 ELSE 0 END AS TienePago,
                   CASE WHEN EXISTS (SELECT 1 FROM dbo.PagoSuscripcion ps WHERE ps.NegocioId = n.Id
                       AND (ps.Metodo <> 'TARJETA' OR ISNULL(sc.Modo, 'LIVE') <> 'TEST'))
                       THEN 1 ELSE 0 END AS TienePagoReal,
                   sc.Estado AS CulqiEstado, sc.Modo AS CulqiModo
            FROM dbo.Negocio n LEFT JOIN dbo.SuscripcionCulqi sc ON sc.NegocioId = n.Id
            ORDER BY n.FechaCreacion DESC
            """;
        var todos = await cmd.ReadListAsync(r => new DatosNegocioMetrica(
            r.GetInt32(0), r.GetString(1), r.GetBoolean(2), r.GetNullableString("TitularEmail"),
            r.GetDateTime(4), r.GetString(5), r.GetString(6), r.GetDecimal(7),
            r.IsDBNull(8) ? null : DateOnly.FromDateTime(r.GetDateTime(8)), r.GetInt32(9) == 1,
            r.GetNullableDateTime("UltimoAcceso"), r.GetInt32(11) == 1, r.GetInt32(12) == 1,
            r.GetInt32(13), r.GetNullableDateTime("UltimoPedido"), r.GetInt32(15) == 1,
            r.GetInt32(16) == 1,
            r.GetNullableString("CulqiEstado"), r.GetNullableString("CulqiModo")), ct);
        var reales = todos.Where(n => MetricasPlataformaReglas.MotivoExclusion(n, excluidos) is null).ToList();
        var descartados = todos.Except(reales).Select(n => new NegocioExcluidoDto(n.Slug,
            MetricasPlataformaReglas.MotivoExclusion(n, excluidos)!)).ToList();

        // Registros sin Negocio aún se cuentan como candidatos. No se materializa ni se devuelve Email.
        await using var registros = conn.CreateCommand();
        registros.CommandText = """
            SELECT COUNT(DISTINCT LOWER(LTRIM(RTRIM(rv.Email))))
            FROM dbo.RegistroVerificacion rv
            WHERE rv.Email NOT LIKE '%@example.%'
              AND NOT EXISTS (SELECT 1 FROM dbo.Negocio n WHERE LOWER(LTRIM(RTRIM(n.TitularEmail))) = LOWER(LTRIM(RTRIM(rv.Email)))
                  AND (n.Activo = 0 OR n.Slug IN ('demo', 'plataforma-interna') OR n.Slug LIKE 'qa%'
                    OR n.Slug LIKE '%test%' OR n.TitularEmail LIKE '%@example.%'
                    OR n.Slug IN (SELECT value FROM OPENJSON(@slugs))))
            """;
        registros.AddParam("@slugs", System.Text.Json.JsonSerializer.Serialize(excluidos));
        var inicios = await registros.ReadScalarAsync<int>(ct);
        var pagando = reales.Count(MetricasPlataformaReglas.Pagando);
        // Denominador: reales con prueba vencida o ya convertidos; excluye pruebas aún abiertas.
        var elegibles = reales.Count(n => MetricasPlataformaReglas.Pagando(n) ||
            n.ProximoPago is DateOnly fin && fin < DateOnly.FromDateTime(ahora));
        var onboarding = reales.Count(n => n.TieneLogo || n.TieneServicio);
        var pedidos = reales.Count(n => n.PedidosTotales > 0);
        var pruebas = reales.Count(n => MetricasPlataformaReglas.PruebaActiva(n, ahora));
        var embudo = new List<EtapaEmbudoDto>
        {
            new("inicio_registro", inicios), new("registro_confirmado", reales.Count),
            new("primer_login", reales.Count(n => n.UltimoAcceso.HasValue)),
            new("onboarding_completo", onboarding), new("primer_pedido", pedidos),
            new("activos_en_prueba", pruebas), new("suscripcion_activada", reales.Count(MetricasPlataformaReglas.SuscripcionLive)),
            new("primer_pago", reales.Count(n => n.TienePago))
        };
        return new(embudo, descartados.Count, reales.Count(n => n.AltaAutonoma),
            reales.Count(n => n.CulqiEstado == "ACTIVA" && n.CulqiModo == "TEST"),
            reales.Count, onboarding, pedidos, pruebas, pagando, MetricasPlataformaReglas.Mrr(reales),
            MetricasPlataformaReglas.Conversion(pagando, elegibles),
            reales.Select(n => new NegocioMetricaDto(n.Slug, n.FechaCreacion, n.Plan, n.Estado,
                n.Estado == "PRUEBA" && n.ProximoPago is DateOnly fin
                    ? fin.DayNumber - DateOnly.FromDateTime(ahora).DayNumber : null,
                n.UltimoAcceso.HasValue, n.PedidosTotales, n.UltimoPedido,
                MetricasPlataformaReglas.MotivoAbandono(n, ahora))).ToList(), descartados);
    }
}
