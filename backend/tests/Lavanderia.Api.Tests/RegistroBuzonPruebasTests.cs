using Lavanderia.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Lavanderia.Api.Tests;

public class RegistroBuzonPruebasTests
{
    private sealed class Entorno(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "Lavanderia.Api";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(string? buzon) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Registro:BuzonPruebas"] = buzon })
        .Build();

    [Fact]
    public void Production_nuncaUsaElBuzon_aunqueEsteConfigurado()
        => Assert.Null(RegistroVerificacionService.BuzonPruebas(new Entorno(Environments.Production), Config(@"C:\qa\buzon")));

    [Theory]
    [InlineData("QA")]
    [InlineData("Development")]
    public void FueraDeProduction_usaElBuzonConfigurado(string entorno)
        => Assert.Equal(@"C:\qa\buzon", RegistroVerificacionService.BuzonPruebas(new Entorno(entorno), Config(@"C:\qa\buzon")));

    [Fact]
    public void SinConfiguracion_noHayBuzon()
        => Assert.Null(RegistroVerificacionService.BuzonPruebas(new Entorno("QA"), Config(null)));
}
