using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;

namespace Lavanderia.Api.Services;

public sealed class ImportacionExcelException(string message) : Exception(message);

public static class ExcelImportador
{
    public const long TamanoMaximoBytes = 5 * 1024 * 1024;
    public const int FilasMaximas = 2_000;
    public const int ColumnasMaximas = 50;
    private const long TamanoDescomprimidoMaximo = 30 * 1024 * 1024;
    private const int EntradasMaximas = 250;

    public static string ConvertirATsv(Stream archivo)
    {
        if (!archivo.CanSeek)
            throw new ImportacionExcelException("No se pudo procesar el archivo Excel.");

        ValidarContenedor(archivo);
        archivo.Position = 0;

        try
        {
            using var libro = new XLWorkbook(archivo);
            var hoja = libro.Worksheets.FirstOrDefault()
                ?? throw new ImportacionExcelException("El Excel no contiene hojas.");
            var rango = hoja.RangeUsed();
            if (rango is null) return string.Empty;

            var filas = rango.RowCount();
            var columnas = rango.ColumnCount();
            if (filas > FilasMaximas)
                throw new ImportacionExcelException($"El Excel supera el limite de {FilasMaximas:N0} filas.");
            if (columnas > ColumnasMaximas)
                throw new ImportacionExcelException($"El Excel supera el limite de {ColumnasMaximas} columnas.");

            var resultado = new StringBuilder();
            for (var fila = 1; fila <= filas; fila++)
            {
                for (var columna = 1; columna <= columnas; columna++)
                {
                    if (columna > 1) resultado.Append('\t');
                    var valor = rango.Cell(fila, columna).GetFormattedString();
                    resultado.Append(LimpiarCelda(valor));
                }
                if (fila < filas) resultado.AppendLine();
            }

            return resultado.ToString();
        }
        catch (ImportacionExcelException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or NotSupportedException)
        {
            throw new ImportacionExcelException("El archivo no es un Excel .xlsx valido.");
        }
    }

    private static void ValidarContenedor(Stream archivo)
    {
        try
        {
            using var zip = new ZipArchive(archivo, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count == 0 || zip.Entries.Count > EntradasMaximas)
                throw new ImportacionExcelException("El contenido del Excel no es valido.");

            long total = 0;
            foreach (var entrada in zip.Entries)
            {
                total = checked(total + entrada.Length);
                if (entrada.Length > TamanoDescomprimidoMaximo || total > TamanoDescomprimidoMaximo)
                    throw new ImportacionExcelException("El Excel descomprimido supera el limite permitido.");
            }
        }
        catch (ImportacionExcelException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException)
        {
            throw new ImportacionExcelException("El archivo no es un Excel .xlsx valido.");
        }
    }

    private static string LimpiarCelda(string valor) => valor
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();
}
