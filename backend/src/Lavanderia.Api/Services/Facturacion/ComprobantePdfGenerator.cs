using Lavanderia.Api.Domain;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lavanderia.Api.Services.Facturacion;

public class ComprobantePdfGenerator
{
    public byte[] Generar(ComprobanteElectronico c, ConfiguracionNegocio negocio)
    {
        var qr = Qr(c);
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4); page.Margin(30); page.DefaultTextStyle(x => x.FontSize(10));
            page.Header().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(c.RazonSocialEmisor ?? negocio.NombreNegocio).FontSize(15).Bold();
                    col.Item().Text($"RUC: {c.RucEmisor}");
                    if (!string.IsNullOrWhiteSpace(c.DireccionFiscalEmisor)) col.Item().Text(c.DireccionFiscalEmisor);
                    if (!string.IsNullOrWhiteSpace(negocio.Telefono)) col.Item().Text($"Telefono: {negocio.Telefono}");
                });
                row.ConstantItem(210).Border(1).Padding(10).Column(col =>
                {
                    col.Item().AlignCenter().Text(c.Tipo == "FACTURA" ? "FACTURA ELECTRONICA" : "BOLETA DE VENTA ELECTRONICA").Bold();
                    col.Item().AlignCenter().Text($"{c.Serie}-{c.Correlativo:D8}").FontSize(14).Bold();
                    col.Item().AlignCenter().Text($"RUC: {c.RucEmisor}").FontSize(9);
                });
            });
            page.Content().PaddingTop(16).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Cliente: {c.ClienteNombre}");
                    row.RelativeItem().AlignRight().Text($"{c.ClienteTipoDoc}: {c.ClienteNumDoc ?? "-"}");
                });
                col.Item().Text($"Fecha de emision: {c.FechaEmision:dd/MM/yyyy HH:mm}");
                col.Item().PaddingTop(14).Table(table =>
                {
                    table.ColumnsDefinition(x => { x.RelativeColumn(1); x.RelativeColumn(4); x.RelativeColumn(2); x.RelativeColumn(2); });
                    table.Header(h =>
                    {
                        foreach (var text in new[] { "Cant.", "Descripcion", "P. Unit.", "Importe" })
                            h.Cell().PaddingVertical(5).BorderBottom(1).Text(text).SemiBold();
                    });
                    foreach (var item in c.Detalles)
                    {
                        table.Cell().PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Text(item.Cantidad.ToString("0.###"));
                        table.Cell().PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Text(item.Descripcion);
                        table.Cell().PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).AlignRight().Text($"S/ {item.PrecioUnitarioIgv:0.00}");
                        table.Cell().PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).AlignRight().Text($"S/ {item.Total:0.00}");
                    }
                });
                col.Item().PaddingTop(15).Row(row =>
                {
                    row.ConstantItem(110).Height(100).Image(qr);
                    row.RelativeItem().AlignRight().Column(total =>
                    {
                        FilaTotal(total, "Op. gravada", c.OpGravada);
                        FilaTotal(total, $"IGV ({c.IgvPorcentaje:0.##}%)", c.Igv);
                        if (c.Descuento > 0) FilaTotal(total, "Descuento", -c.Descuento);
                        if (c.Recargo > 0) FilaTotal(total, "Recargo", c.Recargo);
                        if (c.Redondeo != 0) FilaTotal(total, "Redondeo", c.Redondeo);
                        total.Item().PaddingTop(4).BorderTop(1).Row(r => { r.RelativeItem().Text("TOTAL").Bold(); r.ConstantItem(100).AlignRight().Text($"S/ {c.Total:0.00}").Bold(); });
                    });
                });
                if (c.EsSimulado) col.Item().PaddingTop(12).Background(Colors.Orange.Lighten4).Padding(8).Text("DOCUMENTO SIMULADO - SIN VALIDEZ TRIBUTARIA").Bold();
            });
            page.Footer().AlignCenter().Text(c.Estado == "ACEPTADO" ? "Representacion impresa. Aceptado por SUNAT." : $"Representacion impresa. Estado: {c.Estado}.").FontSize(8);
        })).GeneratePdf();
    }

    private static void FilaTotal(ColumnDescriptor col, string label, decimal value) =>
        col.Item().Row(r => { r.RelativeItem().Text(label); r.ConstantItem(100).AlignRight().Text($"S/ {value:0.00}"); });

    private static byte[] Qr(ComprobanteElectronico c)
    {
        var contenido = string.Join("|", c.RucEmisor, c.Tipo == "FACTURA" ? "01" : "03", c.Serie,
            c.Correlativo, c.Igv.ToString("0.00"), c.Total.ToString("0.00"), c.FechaEmision.ToString("yyyy-MM-dd"),
            c.ClienteTipoDoc switch { "RUC" => "6", "DNI" => "1", _ => "-" }, c.ClienteNumDoc ?? "", c.HashCpe ?? "");
        using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(10);
    }
}
