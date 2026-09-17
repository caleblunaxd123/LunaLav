using ClosedXML.Excel;
using Lavanderia.Api.Services;

namespace Lavanderia.Api.Tests;

public class ExcelImportadorTests
{
    [Fact]
    public void Convierte_la_primera_hoja_a_tsv_y_limpia_saltos()
    {
        using var archivo = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Nombre";
            ws.Cell(1, 2).Value = "Precio";
            ws.Cell(2, 1).Value = "Lavado\npor kilo";
            ws.Cell(2, 2).Value = 6.5;
        });

        var resultado = ExcelImportador.ConvertirATsv(archivo);

        Assert.Contains("Nombre\tPrecio", resultado);
        Assert.Contains("Lavado por kilo\t6.5", resultado);
    }

    [Fact]
    public void Rechaza_un_archivo_que_no_es_xlsx()
    {
        using var archivo = new MemoryStream("contenido falso"u8.ToArray());

        var error = Assert.Throws<ImportacionExcelException>(() => ExcelImportador.ConvertirATsv(archivo));

        Assert.Contains("no es un Excel", error.Message);
    }

    [Fact]
    public void Rechaza_mas_columnas_de_las_permitidas()
    {
        using var archivo = CrearExcel(ws =>
        {
            for (var columna = 1; columna <= ExcelImportador.ColumnasMaximas + 1; columna++)
                ws.Cell(1, columna).Value = columna;
        });

        var error = Assert.Throws<ImportacionExcelException>(() => ExcelImportador.ConvertirATsv(archivo));

        Assert.Contains("columnas", error.Message);
    }

    private static MemoryStream CrearExcel(Action<IXLWorksheet> escribir)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Datos");
        escribir(hoja);
        var archivo = new MemoryStream();
        libro.SaveAs(archivo);
        archivo.Position = 0;
        return archivo;
    }
}
