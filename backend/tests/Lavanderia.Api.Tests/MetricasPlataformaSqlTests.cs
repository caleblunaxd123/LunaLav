using System.Text.Json;
using Lavanderia.Api.Infrastructure;
using Lavanderia.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace Lavanderia.Api.Tests;

public sealed class QaSqlFactAttribute : FactAttribute
{
    public QaSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LUNALAV_QA_SQL")))
            Skip = "Definir LUNALAV_QA_SQL para ejecutar contra la base aislada LunaLavQA.";
    }
}

public class MetricasPlataformaSqlTests
{
    private readonly ITestOutputHelper _output;
    public MetricasPlataformaSqlTests(ITestOutputHelper output) => _output = output;

    [QaSqlFact]
    public async Task ConsultasYRespuestaCumplenInvariantesEnLunaLavQa()
    {
        var connectionString = Environment.GetEnvironmentVariable("LUNALAV_QA_SQL")!;
        var target = new SqlConnectionStringBuilder(connectionString);
        Assert.Contains(target.DataSource, new[] { @".\SQLEXPRESS", @"lpc:.\SQLEXPRESS" },
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("LunaLavQA", target.InitialCatalog, ignoreCase: true);
        Assert.True(target.IntegratedSecurity);
        Assert.True(target.TrustServerCertificate);
        Assert.True(string.IsNullOrEmpty(target.AttachDBFilename));
        Assert.True(string.IsNullOrEmpty(target.UserID));
        Assert.True(string.IsNullOrEmpty(target.Password));

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Sql"] = connectionString })
            .Build();
        var resultado = await new MetricasPlataformaService(new SqlConnectionFactory(config), config).ObtenerAsync();
        var etapas = resultado.Embudo.ToDictionary(e => e.Codigo, e => e.Total);

        Assert.Equal(new[] { "inicio_registro", "registro_confirmado", "primer_login", "onboarding_completo",
            "primer_pedido", "activos_en_prueba", "suscripcion_activada", "primer_pago" },
            resultado.Embudo.Select(e => e.Codigo));
        Assert.All(resultado.Embudo, e => Assert.True(e.Total >= 0));
        Assert.Equal(resultado.NegociosReales, etapas["registro_confirmado"]);
        Assert.Equal(resultado.OnboardingCompleto, etapas["onboarding_completo"]);
        Assert.Equal(resultado.PrimerPedido, etapas["primer_pedido"]);
        Assert.Equal(resultado.ActivosEnPrueba, etapas["activos_en_prueba"]);
        Assert.Equal(resultado.NegociosReales, resultado.Negocios.Count);
        Assert.Equal(resultado.Excluidos, resultado.NegociosExcluidos.Count);
        Assert.InRange(resultado.RegistrosConfirmadosAutonomos, 0, resultado.NegociosReales);
        Assert.InRange(resultado.Pagando, 0, resultado.NegociosReales);
        Assert.InRange(resultado.ConversionPruebaPago, 0m, 100m);
        Assert.True(resultado.Mrr >= 0);
        foreach (var codigo in new[] { "primer_login", "onboarding_completo", "primer_pedido",
            "activos_en_prueba", "suscripcion_activada", "primer_pago" })
            Assert.InRange(etapas[codigo], 0, etapas["registro_confirmado"]);
        Assert.True(etapas["activos_en_prueba"] <= etapas["primer_login"] + etapas["primer_pedido"]);

        Assert.Contains(resultado.NegociosExcluidos, n => n.Slug.Equals("demo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resultado.NegociosExcluidos, n => n.Slug.Equals("plataforma-interna", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resultado.NegociosExcluidos, n => n.Slug.StartsWith("qa", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resultado.Negocios, n => n.Slug.Equals("demo", StringComparison.OrdinalIgnoreCase)
            || n.Slug.Equals("plataforma-interna", StringComparison.OrdinalIgnoreCase)
            || n.Slug.StartsWith("qa", StringComparison.OrdinalIgnoreCase));

        foreach (var dto in new[] { typeof(MetricasPlataformaDto), typeof(EtapaEmbudoDto),
            typeof(NegocioMetricaDto), typeof(NegocioExcluidoDto) })
            Assert.DoesNotContain(dto.GetProperties(), p => EsCampoPersonal(p.Name));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(resultado));
        AssertNoPersonalFields(json.RootElement);

        _output.WriteLine("METRICAS_QA_CONTEOS " + JsonSerializer.Serialize(new
        {
            etapas,
            resultado.Excluidos,
            resultado.RegistrosConfirmadosAutonomos,
            resultado.SuscripcionesTest,
            resultado.NegociosReales,
            resultado.OnboardingCompleto,
            resultado.PrimerPedido,
            resultado.ActivosEnPrueba,
            resultado.Pagando
        }));
    }

    private static bool EsCampoPersonal(string nombre) =>
        new[] { "email", "correo", "phone", "telefono", "celular" }
            .Any(fragmento => nombre.Contains(fragmento, StringComparison.OrdinalIgnoreCase));

    private static void AssertNoPersonalFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) AssertNoPersonalFields(item);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.False(EsCampoPersonal(property.Name), $"Campo personal en respuesta: {property.Name}");
                AssertNoPersonalFields(property.Value);
            }
        }
    }
}
