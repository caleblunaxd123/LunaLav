using ClosedXML.Excel;
using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Genera plantillas .xlsx con estilo (encabezados, instrucciones y filas de ejemplo) para las
/// cargas masivas. Un solo lugar define el formato de cada tipo; el frontend solo descarga.
/// </summary>
[ApiController]
[Route("api/plantillas")]
[Authorize]
public class PlantillasController : ControllerBase
{
    private record Columna(string Titulo, string Ejemplo1, string Ejemplo2, string Nota, double Ancho);
    private record Definicion(string Hoja, string Titulo, string Instruccion, Columna[] Columnas);

    private static readonly Dictionary<string, Definicion> Plantillas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["servicios"] = new(
            "Servicios", "Plantilla de servicios",
            "Completa una fila por servicio. No borres la fila de títulos. Precio en soles (ej. 6.50). Unidad: kg, prenda, pieza o und. La categoría es opcional (se crea sola si no existe).",
            new[]
            {
                new Columna("Nombre", "Lavado por kilo", "Planchado camisa", "Obligatorio", 34),
                new Columna("Precio", "6.50", "3.00", "Obligatorio · en soles", 14),
                new Columna("Unidad", "kg", "prenda", "kg / prenda / pieza / und", 16),
                new Columna("Categoria", "Ropa por kilo", "Adicionales", "Opcional", 26),
            }),
        ["clientes"] = new(
            "Clientes", "Plantilla de clientes",
            "Completa una fila por cliente. No borres la fila de títulos. El celular debe tener 9 dígitos y empezar con 9. El DNI (8 dígitos) y la dirección son opcionales.",
            new[]
            {
                new Columna("Nombre", "Juan Pérez", "María Gómez", "Obligatorio", 32),
                new Columna("Celular", "987654321", "998877665", "9 dígitos", 18),
                new Columna("DNI", "12345678", "", "Opcional · 8 dígitos", 16),
                new Columna("Direccion", "Av. Larco 123, Miraflores", "Jr. Unión 456", "Opcional", 38),
            }),
        ["insumos"] = new(
            "Insumos", "Plantilla de insumos (inventario)",
            "Completa una fila por insumo. No borres la fila de títulos. Stock actual y mínimo en números (ej. 10 o 2.5). La unidad puede ser: und, kg, litro, bolsa, etc. Clase: INSUMO (consumible), MATERIAL (herramienta) o EQUIPO (activo); si la dejas vacía se toma INSUMO.",
            new[]
            {
                new Columna("Nombre", "Detergente industrial", "Bolsas 5 kg", "Obligatorio", 34),
                new Columna("Unidad", "litro", "unidad", "und / kg / litro / bolsa…", 16),
                new Columna("StockActual", "20", "150", "Cantidad que tienes hoy", 16),
                new Columna("StockMinimo", "5", "30", "Nivel de alerta", 16),
                new Columna("Clase", "INSUMO", "EQUIPO", "INSUMO / MATERIAL / EQUIPO", 18),
            }),
    };

    [HttpGet("{tipo}")]
    public IActionResult Descargar(string tipo)
    {
        if (!Plantillas.TryGetValue(tipo, out var def))
            return NotFound(new { mensaje = "Plantilla no disponible." });

        var azul = XLColor.FromHtml("#0B57D0");
        var grisNota = XLColor.FromHtml("#64748B");

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(def.Hoja);
        var nCols = def.Columnas.Length;

        // IMPORTANTE: esta hoja se sube tal cual para la carga masiva, así que SOLO debe contener
        // el encabezado y, debajo, los datos del usuario. El título y las instrucciones van ARRIBA
        // del encabezado (el importador ancla en la fila de títulos y descarta todo lo anterior).
        // Un ejemplo va dentro del texto de instrucciones y las notas por columna van como
        // comentarios de celda: nada de eso se exporta a CSV, así no se cuela como fila de datos.

        // Fila 1: título de marca.
        var titulo = ws.Range(1, 1, 1, nCols).Merge();
        titulo.Value = def.Titulo;
        titulo.Style.Font.Bold = true;
        titulo.Style.Font.FontSize = 15;
        titulo.Style.Font.FontColor = XLColor.White;
        titulo.Style.Fill.BackgroundColor = azul;
        titulo.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titulo.Style.Alignment.Indent = 1;
        ws.Row(1).Height = 26;

        // Fila 2: instrucciones + un ejemplo en prosa (no como fila de datos, para no importarlo).
        var ejemplo = string.Join("  |  ", def.Columnas.Select(c => c.Ejemplo1));
        var instr = ws.Range(2, 1, 2, nCols).Merge();
        instr.Value = $"{def.Instruccion}\nEjemplo: {ejemplo}";
        instr.Style.Font.FontSize = 10;
        instr.Style.Font.FontColor = grisNota;
        instr.Style.Alignment.WrapText = true;
        instr.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        instr.Style.Alignment.Indent = 1;
        ws.Row(2).Height = 58;

        // Fila 4: encabezados de columna (los datos del usuario van de la fila 5 hacia abajo).
        const int filaHeader = 4;
        for (var i = 0; i < nCols; i++)
        {
            var c = ws.Cell(filaHeader, i + 1);
            c.Value = def.Columnas[i].Titulo;
            c.Style.Font.Bold = true;
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Fill.BackgroundColor = azul;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            c.Style.Border.OutsideBorderColor = XLColor.White;
            ws.Column(i + 1).Width = def.Columnas[i].Ancho;
        }
        ws.Row(filaHeader).Height = 20;
        // NOTA: no se usan comentarios de celda (generan un dibujo VML/legacy que algunas versiones
        // de Excel rechazan como "formato no válido"). Las pistas de cada columna ya están en la fila
        // de instrucciones (fila 2). Debajo del encabezado solo van los datos del usuario, para que el
        // importador (que ancla en el encabezado) no lea nada extra como fila.

        ws.SheetView.FreezeRows(filaHeader);
        ws.Range(filaHeader, 1, filaHeader, nCols).SetAutoFilter();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"plantilla-{tipo.ToLowerInvariant()}.xlsx");
    }

    [HttpPost("leer-excel")]
    [RequestSizeLimit(ExcelImportador.TamanoMaximoBytes)]
    public async Task<IActionResult> LeerExcel([FromForm] IFormFile? archivo, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Selecciona un archivo Excel .xlsx." });
        if (archivo.Length > ExcelImportador.TamanoMaximoBytes)
            return BadRequest(new { mensaje = "El archivo no puede superar los 5 MB." });
        if (!string.Equals(Path.GetExtension(archivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Solo se admiten archivos Excel .xlsx." });

        await using var memoria = new MemoryStream((int)archivo.Length);
        await archivo.CopyToAsync(memoria, ct);
        memoria.Position = 0;

        try
        {
            return Ok(new { texto = ExcelImportador.ConvertirATsv(memoria) });
        }
        catch (ImportacionExcelException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}
