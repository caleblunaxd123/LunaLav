using System.Globalization;
using System.Text;

namespace Lavanderia.Api.Domain;

/// <summary>Reglas puras de inventario (testeables sin base de datos).</summary>
public static class InventarioReglas
{
    /// <summary>Clases de inventario válidas: equipos de trabajo, materiales/herramientas, insumos consumibles.</summary>
    public static readonly string[] ClasesValidas = { "EQUIPO", "MATERIAL", "INSUMO" };

    /// <summary>
    /// Normaliza la clase de un insumo. Acepta cualquier caso/espacios; si viene vacía o
    /// no es una clase válida, devuelve INSUMO (consumible) como valor seguro por defecto.
    /// </summary>
    public static string NormalizarClase(string? clase)
    {
        var c = (clase ?? "").Trim().ToUpperInvariant();
        return ClasesValidas.Contains(c) ? c : "INSUMO";
    }

    /// <summary>Clave tolerante para impedir duplicados que solo cambian tildes, espacios o guiones.</summary>
    public static string CanonicalNombre(string? nombre)
    {
        var descompuesto = (nombre ?? "").Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var ch in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }
}
