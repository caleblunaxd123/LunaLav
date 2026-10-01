using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Tests;

public class PermisosFinosTests
{
    [Fact]
    public void Las_claves_son_unicas_y_no_chocan_con_los_modulos()
    {
        var claves = PermisosFinos.Catalogo.Select(i => i.Clave).ToList();
        Assert.Equal(claves.Count, claves.Distinct().Count());
        Assert.DoesNotContain(claves, c => Modulos.Todos.Contains(c));
    }

    [Fact]
    public void Cada_permiso_cuelga_de_un_modulo_existente_y_empieza_con_su_nombre()
    {
        foreach (var item in PermisosFinos.Catalogo)
        {
            Assert.Contains(item.Modulo, Modulos.Todos);
            Assert.StartsWith(item.Modulo + "_", item.Clave);
            Assert.False(string.IsNullOrWhiteSpace(item.Etiqueta));
        }
    }

    [Fact]
    public void Las_claves_caben_en_la_columna_Modulo_de_RolPermiso()
    {
        // dbo.RolPermiso.Modulo es NVARCHAR(40): una clave más larga fallaría al guardar el permiso.
        Assert.All(PermisosFinos.Catalogo, i => Assert.InRange(i.Clave.Length, 1, 40));
    }

    [Fact]
    public void ClavesTodas_y_ClavesDeModulo_son_coherentes()
    {
        Assert.Equal(PermisosFinos.Catalogo.Length, PermisosFinos.ClavesTodas.Count);
        var caja = PermisosFinos.ClavesDeModulo("CAJA").ToList();
        Assert.Equal(5, caja.Count);
        Assert.All(caja, c => Assert.Contains(c, PermisosFinos.ClavesTodas));
        Assert.Empty(PermisosFinos.ClavesDeModulo("AJUSTES"));
    }
}
