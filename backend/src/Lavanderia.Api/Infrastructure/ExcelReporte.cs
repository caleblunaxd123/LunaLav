using ClosedXML.Excel;

namespace Lavanderia.Api.Infrastructure;

/// <summary>
/// Construye un Excel (.xlsx) con estilo: portada con el negocio + título + período,
/// cabecera azul, filas alternadas (banded), bordes, autofiltro y encabezado congelado.
/// Reutilizable por cualquier export de reporte (formato genérico Columnas/Filas).
/// </summary>
public static class ExcelReporte
{
    private static readonly XLColor Azul = XLColor.FromHtml("#1e40af");
    private static readonly XLColor Banda = XLColor.FromHtml("#f1f5f9");
    private static readonly XLColor Gris = XLColor.FromHtml("#64748b");
    private static readonly XLColor BordeSuave = XLColor.FromHtml("#e2e8f0");
    private static readonly XLColor BordeExt = XLColor.FromHtml("#cbd5e1");

    // Columnas cuyo contenido es numérico/monetario → se alinean a la derecha.
    private static readonly string[] PalabrasNumericas =
        { "total", "monto", "s/", "precio", "cantidad", "ingreso", "gasto", "saldo",
          "pagado", "%", "puntos", "pedidos", "importe", "subtotal", "deuda" };

    public static byte[] Construir(string negocioNombre, string titulo, string subtitulo,
        IReadOnlyList<string> columnas, IReadOnlyList<Dictionary<string, string>> filas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(NombreHojaSeguro(titulo));
        var nCols = Math.Max(1, columnas.Count);

        // ---------- Portada ----------
        ws.Cell(1, 1).Value = negocioNombre;
        ws.Range(1, 1, 1, nCols).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 15;
        ws.Cell(1, 1).Style.Font.FontColor = Azul;

        ws.Cell(2, 1).Value = titulo;
        ws.Range(2, 1, 2, nCols).Merge();
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Style.Font.FontSize = 12;

        ws.Cell(3, 1).Value = subtitulo;
        ws.Range(3, 1, 3, nCols).Merge();
        ws.Cell(3, 1).Style.Font.Italic = true;
        ws.Cell(3, 1).Style.Font.FontColor = Gris;
        ws.Cell(3, 1).Style.Font.FontSize = 9.5;

        // ---------- Cabecera ----------
        const int filaCab = 5;
        for (int c = 0; c < columnas.Count; c++)
        {
            var cell = ws.Cell(filaCab, c + 1);
            cell.Value = columnas[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = Azul;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
        ws.Row(filaCab).Height = 20;

        // ---------- Datos ----------
        if (filas.Count == 0)
        {
            ws.Cell(filaCab + 1, 1).Value = "Sin datos para el período seleccionado.";
            ws.Range(filaCab + 1, 1, filaCab + 1, nCols).Merge();
            ws.Cell(filaCab + 1, 1).Style.Font.Italic = true;
            ws.Cell(filaCab + 1, 1).Style.Font.FontColor = Gris;
        }
        else
        {
            for (int f = 0; f < filas.Count; f++)
            {
                int r = filaCab + 1 + f;
                for (int c = 0; c < columnas.Count; c++)
                    ws.Cell(r, c + 1).Value = filas[f].TryGetValue(columnas[c], out var v) ? v : "";
                if (f % 2 == 1)
                    ws.Range(r, 1, r, nCols).Style.Fill.BackgroundColor = Banda;
            }

            // Columnas numéricas → alineadas a la derecha.
            for (int c = 0; c < columnas.Count; c++)
            {
                if (!EsColumnaNumerica(columnas[c])) continue;
                ws.Range(filaCab + 1, c + 1, filaCab + filas.Count, c + 1)
                  .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }

        // ---------- Bordes + estructura ----------
        int ultima = filaCab + Math.Max(1, filas.Count);
        var tabla = ws.Range(filaCab, 1, ultima, nCols);
        tabla.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        tabla.Style.Border.OutsideBorderColor = BordeExt;
        tabla.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        tabla.Style.Border.InsideBorderColor = BordeSuave;
        tabla.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        if (filas.Count > 0)
            ws.Range(filaCab, 1, filaCab + filas.Count, nCols).SetAutoFilter();
        ws.SheetView.FreezeRows(filaCab);

        // ---------- Ancho de columnas (con topes legibles) ----------
        ws.Columns().AdjustToContents();
        foreach (var col in ws.ColumnsUsed())
        {
            if (col.Width < 12) col.Width = 12;
            if (col.Width > 45) col.Width = 45;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static bool EsColumnaNumerica(string nombre)
    {
        var n = nombre.ToLowerInvariant();
        return PalabrasNumericas.Any(p => n.Contains(p));
    }

    private static string NombreHojaSeguro(string titulo)
    {
        var limpio = new string(titulo.Where(ch => !"\\/*?:[]".Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(limpio)) limpio = "Reporte";
        return limpio.Length > 31 ? limpio[..31] : limpio;
    }
}
