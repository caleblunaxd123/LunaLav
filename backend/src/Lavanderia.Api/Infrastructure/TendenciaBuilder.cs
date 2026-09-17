using Lavanderia.Api.Dtos;

namespace Lavanderia.Api.Infrastructure;

/// <summary>Arma las series de barras rellenando los días/meses sin datos con 0 y
/// poniendo etiquetas cortas en español, a partir de los conteos crudos del repositorio.</summary>
public static class TendenciaBuilder
{
    private static readonly string[] MesesCortos =
        { "", "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

    /// <summary>Serie de los últimos <paramref name="dias"/> días hasta hoy. Etiqueta = día del mes.</summary>
    public static List<TendenciaPuntoDto> SerieDiaria(Dictionary<DateTime, int> datos, int dias)
    {
        var hoy = DateTime.Today;
        var lista = new List<TendenciaPuntoDto>(dias);
        for (var i = dias - 1; i >= 0; i--)
        {
            var d = hoy.AddDays(-i);
            datos.TryGetValue(d, out var v);
            lista.Add(new TendenciaPuntoDto(d.Day.ToString(), v));
        }
        return lista;
    }

    /// <summary>Serie de los últimos <paramref name="meses"/> meses hasta el actual. Etiqueta = mes abreviado.</summary>
    public static List<TendenciaPuntoDto> SerieMensual(Dictionary<DateTime, int> datos, int meses)
    {
        var baseMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var lista = new List<TendenciaPuntoDto>(meses);
        for (var i = meses - 1; i >= 0; i--)
        {
            var m = baseMes.AddMonths(-i);
            datos.TryGetValue(m, out var v);
            lista.Add(new TendenciaPuntoDto(MesesCortos[m.Month], v));
        }
        return lista;
    }

    /// <summary>Primer día a incluir para una serie diaria de N días (hoy inclusive).</summary>
    public static DateTime DesdeDias(int dias) => DateTime.Today.AddDays(-(dias - 1));

    /// <summary>Primer día del mes a incluir para una serie de N meses (mes actual inclusive).</summary>
    public static DateTime DesdeMeses(int meses)
    {
        var baseMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return baseMes.AddMonths(-(meses - 1));
    }
}
