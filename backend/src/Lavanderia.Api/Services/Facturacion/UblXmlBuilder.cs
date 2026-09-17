using System.Globalization;
using System.Xml.Linq;
using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Services.Facturacion;

public static class UblXmlBuilder
{
    private static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cn = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    private static readonly XNamespace Dn = "urn:oasis:names:specification:ubl:schema:xsd:DebitNote-2";
    private static readonly XNamespace Da = "urn:oasis:names:specification:ubl:schema:xsd:DespatchAdvice-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";

    public static XDocument Construir(ComprobanteElectronico c, ConfiguracionFacturacion config, List<PedidoItem> items)
    {
        var codigoTipo = c.Tipo == "FACTURA" ? "01" : "03";
        var fecha = c.FechaEmision == default ? DateTime.Now : c.FechaEmision;
        var lineas = AsignarLineas(c, items);
        var baseAjustes = lineas.Sum(x => x.ValorVenta);
        var descuentoNeto = SinIgv(c.Descuento, c.IgvPorcentaje);
        var recargoNeto = SinIgv(c.Recargo, c.IgvPorcentaje);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Inv + "Invoice",
                new XAttribute(XNamespace.Xmlns + "cac", Cac),
                new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
                new XAttribute(XNamespace.Xmlns + "ext", Ext),
                new XElement(Ext + "UBLExtensions",
                    new XElement(Ext + "UBLExtension", new XElement(Ext + "ExtensionContent"))),
                new XElement(Cbc + "UBLVersionID", "2.1"),
                new XElement(Cbc + "CustomizationID", new XAttribute("schemeAgencyName", "PE:SUNAT"), "2.0"),
                new XElement(Cbc + "ProfileID",
                    new XAttribute("schemeName", "SUNAT:Identificador de Tipo de Operacion"),
                    new XAttribute("schemeAgencyName", "PE:SUNAT"),
                    new XAttribute("schemeURI", "urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo17"), "0101"),
                new XElement(Cbc + "ID", $"{c.Serie}-{c.Correlativo:D8}"),
                new XElement(Cbc + "IssueDate", fecha.ToString("yyyy-MM-dd")),
                new XElement(Cbc + "IssueTime", fecha.ToString("HH:mm:ss")),
                new XElement(Cbc + "InvoiceTypeCode",
                    new XAttribute("listID", "0101"),   // Tipo de operación (catálogo 51): 0101 = Venta interna. SUNAT lo exige (error 3205).
                    codigoTipo),                        // _text = tipo de documento (03 boleta / 01 factura). Sin listSchemeURI: evita la observación 4261.
                new XElement(Cbc + "Note", new XAttribute("languageLocaleID", "1000"), MontoEnLetras.Convertir(c.Total)),
                new XElement(Cbc + "DocumentCurrencyCode", new XAttribute("listID", "ISO 4217 Alpha"), "PEN"),
                ConstruirFirma(config),
                ConstruirEmisor(config),
                ConstruirCliente(c),
                new XElement(Cac + "PaymentTerms",
                    new XElement(Cbc + "ID", "FormaPago"), new XElement(Cbc + "PaymentMeansID", "Contado")),
                ConstruirAjuste(c.Descuento > 0, false, descuentoNeto, baseAjustes, "02", "Descuento global"),
                ConstruirAjuste(c.Recargo > 0, true, recargoNeto, baseAjustes, "50", "Recargo por servicio urgente"),
                ConstruirImpuesto(c),
                new XElement(Cac + "LegalMonetaryTotal",
                    new XElement(Cbc + "LineExtensionAmount", Moneda(), Dec(lineas.Sum(x => x.ValorVenta))),
                    c.Descuento > 0 ? new XElement(Cbc + "AllowanceTotalAmount", Moneda(), Dec(descuentoNeto)) : null,
                    c.Recargo > 0 ? new XElement(Cbc + "ChargeTotalAmount", Moneda(), Dec(recargoNeto)) : null,
                    new XElement(Cbc + "TaxInclusiveAmount", Moneda(), Dec(c.Total - c.Redondeo)),
                    c.Redondeo != 0 ? new XElement(Cbc + "PayableRoundingAmount", Moneda(), Dec(c.Redondeo)) : null,
                    new XElement(Cbc + "PayableAmount", Moneda(), Dec(c.Total))),
                lineas.Select((linea, indice) => ConstruirLinea(indice + 1, linea, c.IgvPorcentaje))));
    }

    /// <summary>
    /// Nota de Crédito (SUNAT 07): documento &lt;CreditNote&gt; que referencia a la boleta/factura
    /// original (BillingReference), indica el motivo (DiscrepancyResponse, catálogo 09) y repite las
    /// líneas como CreditNoteLine. Reutiliza los mismos sub-bloques que la factura para que el
    /// contenido tributario sea idéntico. Se emite por el total del comprobante referenciado.
    /// </summary>
    public static XDocument ConstruirNotaCredito(ComprobanteElectronico c, ConfiguracionFacturacion config, List<PedidoItem> items)
    {
        var fecha = c.FechaEmision == default ? DateTime.Now : c.FechaEmision;
        var lineas = AsignarLineas(c, items);
        var baseAjustes = lineas.Sum(x => x.ValorVenta);
        var descuentoNeto = SinIgv(c.Descuento, c.IgvPorcentaje);
        var recargoNeto = SinIgv(c.Recargo, c.IgvPorcentaje);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Cn + "CreditNote",
                new XAttribute(XNamespace.Xmlns + "cac", Cac),
                new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
                new XAttribute(XNamespace.Xmlns + "ext", Ext),
                new XElement(Ext + "UBLExtensions",
                    new XElement(Ext + "UBLExtension", new XElement(Ext + "ExtensionContent"))),
                new XElement(Cbc + "UBLVersionID", "2.1"),
                new XElement(Cbc + "CustomizationID", "2.0"),
                new XElement(Cbc + "ID", $"{c.Serie}-{c.Correlativo:D8}"),
                new XElement(Cbc + "IssueDate", fecha.ToString("yyyy-MM-dd")),
                new XElement(Cbc + "IssueTime", fecha.ToString("HH:mm:ss")),
                new XElement(Cbc + "Note", new XAttribute("languageLocaleID", "1000"), MontoEnLetras.Convertir(c.Total)),
                new XElement(Cbc + "DocumentCurrencyCode", new XAttribute("listID", "ISO 4217 Alpha"), "PEN"),
                new XElement(Cac + "DiscrepancyResponse",
                    new XElement(Cbc + "ReferenceID", c.DocRefSerieNumero),
                    new XElement(Cbc + "ResponseCode", c.MotivoNotaCodigo),
                    new XElement(Cbc + "Description", c.MotivoNotaDescripcion)),
                new XElement(Cac + "BillingReference",
                    new XElement(Cac + "InvoiceDocumentReference",
                        new XElement(Cbc + "ID", c.DocRefSerieNumero),
                        new XElement(Cbc + "DocumentTypeCode", c.DocRefTipo))),
                ConstruirFirma(config),
                ConstruirEmisor(config),
                ConstruirCliente(c),
                ConstruirAjuste(c.Descuento > 0, false, descuentoNeto, baseAjustes, "02", "Descuento global"),
                ConstruirAjuste(c.Recargo > 0, true, recargoNeto, baseAjustes, "50", "Recargo por servicio urgente"),
                ConstruirImpuesto(c),
                new XElement(Cac + "LegalMonetaryTotal",
                    new XElement(Cbc + "LineExtensionAmount", Moneda(), Dec(lineas.Sum(x => x.ValorVenta))),
                    c.Descuento > 0 ? new XElement(Cbc + "AllowanceTotalAmount", Moneda(), Dec(descuentoNeto)) : null,
                    c.Recargo > 0 ? new XElement(Cbc + "ChargeTotalAmount", Moneda(), Dec(recargoNeto)) : null,
                    new XElement(Cbc + "TaxInclusiveAmount", Moneda(), Dec(c.Total - c.Redondeo)),
                    c.Redondeo != 0 ? new XElement(Cbc + "PayableRoundingAmount", Moneda(), Dec(c.Redondeo)) : null,
                    new XElement(Cbc + "PayableAmount", Moneda(), Dec(c.Total))),
                lineas.Select((linea, indice) => ConstruirLinea(indice + 1, linea, c.IgvPorcentaje, "CreditNoteLine", "CreditedQuantity"))));
    }

    /// <summary>
    /// Nota de Débito (SUNAT 08): documento &lt;DebitNote&gt; que referencia a la boleta/factura
    /// original (BillingReference), indica el motivo (DiscrepancyResponse, catálogo 10) y repite las
    /// líneas como DebitNoteLine. Idéntica estructura tributaria que la Nota de Crédito; cambia el
    /// tipo de nota, el root UBL y el catálogo del motivo. Se emite por el monto adicional a cobrar.
    /// </summary>
    public static XDocument ConstruirNotaDebito(ComprobanteElectronico c, ConfiguracionFacturacion config, List<PedidoItem> items)
    {
        var fecha = c.FechaEmision == default ? DateTime.Now : c.FechaEmision;
        var lineas = AsignarLineas(c, items);
        var baseAjustes = lineas.Sum(x => x.ValorVenta);
        var descuentoNeto = SinIgv(c.Descuento, c.IgvPorcentaje);
        var recargoNeto = SinIgv(c.Recargo, c.IgvPorcentaje);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Dn + "DebitNote",
                new XAttribute(XNamespace.Xmlns + "cac", Cac),
                new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
                new XAttribute(XNamespace.Xmlns + "ext", Ext),
                new XElement(Ext + "UBLExtensions",
                    new XElement(Ext + "UBLExtension", new XElement(Ext + "ExtensionContent"))),
                new XElement(Cbc + "UBLVersionID", "2.1"),
                new XElement(Cbc + "CustomizationID", "2.0"),
                new XElement(Cbc + "ID", $"{c.Serie}-{c.Correlativo:D8}"),
                new XElement(Cbc + "IssueDate", fecha.ToString("yyyy-MM-dd")),
                new XElement(Cbc + "IssueTime", fecha.ToString("HH:mm:ss")),
                new XElement(Cbc + "Note", new XAttribute("languageLocaleID", "1000"), MontoEnLetras.Convertir(c.Total)),
                new XElement(Cbc + "DocumentCurrencyCode", new XAttribute("listID", "ISO 4217 Alpha"), "PEN"),
                new XElement(Cac + "DiscrepancyResponse",
                    new XElement(Cbc + "ReferenceID", c.DocRefSerieNumero),
                    new XElement(Cbc + "ResponseCode", c.MotivoNotaCodigo),
                    new XElement(Cbc + "Description", c.MotivoNotaDescripcion)),
                new XElement(Cac + "BillingReference",
                    new XElement(Cac + "InvoiceDocumentReference",
                        new XElement(Cbc + "ID", c.DocRefSerieNumero),
                        new XElement(Cbc + "DocumentTypeCode", c.DocRefTipo))),
                ConstruirFirma(config),
                ConstruirEmisor(config),
                ConstruirCliente(c),
                ConstruirAjuste(c.Descuento > 0, false, descuentoNeto, baseAjustes, "02", "Descuento global"),
                ConstruirAjuste(c.Recargo > 0, true, recargoNeto, baseAjustes, "50", "Recargo por servicio urgente"),
                ConstruirImpuesto(c),
                new XElement(Cac + "RequestedMonetaryTotal",
                    new XElement(Cbc + "LineExtensionAmount", Moneda(), Dec(lineas.Sum(x => x.ValorVenta))),
                    c.Descuento > 0 ? new XElement(Cbc + "AllowanceTotalAmount", Moneda(), Dec(descuentoNeto)) : null,
                    c.Recargo > 0 ? new XElement(Cbc + "ChargeTotalAmount", Moneda(), Dec(recargoNeto)) : null,
                    new XElement(Cbc + "TaxInclusiveAmount", Moneda(), Dec(c.Total - c.Redondeo)),
                    c.Redondeo != 0 ? new XElement(Cbc + "PayableRoundingAmount", Moneda(), Dec(c.Redondeo)) : null,
                    new XElement(Cbc + "PayableAmount", Moneda(), Dec(c.Total))),
                lineas.Select((linea, indice) => ConstruirLinea(indice + 1, linea, c.IgvPorcentaje, "DebitNoteLine", "DebitedQuantity"))));
    }

    /// <summary>
    /// Guía de Remisión Remitente (GRE, SUNAT 09): documento &lt;DespatchAdvice&gt; sin IGV ni totales.
    /// Describe el traslado: remitente (emisor), destinatario (cliente), motivo (catálogo 20), peso,
    /// bultos, direcciones de partida/llegada y el transporte (público: transportista; privado:
    /// vehículo + conductor). Las líneas son los bienes transportados (DespatchLine).
    /// </summary>
    public static XDocument ConstruirGuiaRemision(ComprobanteElectronico c, ConfiguracionFacturacion config, List<ComprobanteElectronicoDetalle> items)
    {
        var g = c.Guia ?? throw new InvalidOperationException("La guía no tiene datos de traslado.");
        var fecha = c.FechaEmision == default ? DateTime.Now : c.FechaEmision;
        var esPublico = g.ModalidadTransporte == "01";

        // Etapa de transporte: público lleva transportista; privado lleva vehículo + conductor.
        XElement etapa = new(Cac + "ShipmentStage",
            new XElement(Cbc + "TransportModeCode", g.ModalidadTransporte),
            new XElement(Cac + "TransitPeriod",
                new XElement(Cbc + "StartDate", g.FechaInicioTraslado.ToString("yyyy-MM-dd"))),
            esPublico
                ? new XElement(Cac + "CarrierParty",
                    new XElement(Cac + "PartyIdentification",
                        new XElement(Cbc + "ID", new XAttribute("schemeID", "6"), g.TransportistaNumDoc)),
                    new XElement(Cac + "PartyLegalEntity",
                        new XElement(Cbc + "RegistrationName", g.TransportistaRazonSocial)))
                : null,
            !esPublico
                ? new XElement(Cac + "TransportMeans",
                    new XElement(Cac + "RoadTransport",
                        new XElement(Cbc + "LicensePlateID", g.VehiculoPlaca)))
                : null,
            !esPublico
                ? new XElement(Cac + "DriverPerson",
                    new XElement(Cbc + "ID", new XAttribute("schemeID", g.ConductorTipoDoc ?? "1"), g.ConductorNumDoc),
                    new XElement(Cbc + "FirstName", g.ConductorNombres),
                    new XElement(Cbc + "FamilyName", g.ConductorNombres),
                    new XElement(Cbc + "JobTitle", "Principal"),
                    new XElement(Cac + "IdentityDocumentReference",
                        new XElement(Cbc + "ID", g.ConductorLicencia)))
                : null);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Da + "DespatchAdvice",
                new XAttribute(XNamespace.Xmlns + "cac", Cac),
                new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
                new XAttribute(XNamespace.Xmlns + "ext", Ext),
                new XElement(Ext + "UBLExtensions",
                    new XElement(Ext + "UBLExtension", new XElement(Ext + "ExtensionContent"))),
                new XElement(Cbc + "UBLVersionID", "2.1"),
                new XElement(Cbc + "CustomizationID", "2.0"),
                new XElement(Cbc + "ID", $"{c.Serie}-{c.Correlativo:D8}"),
                new XElement(Cbc + "IssueDate", fecha.ToString("yyyy-MM-dd")),
                new XElement(Cbc + "IssueTime", fecha.ToString("HH:mm:ss")),
                new XElement(Cbc + "DespatchAdviceTypeCode",
                    new XAttribute("listAgencyName", "PE:SUNAT"),
                    new XAttribute("listName", "Tipo de Documento"),
                    new XAttribute("listURI", "urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo01"), "09"),
                ConstruirFirma(config),
                new XElement(Cac + "DespatchSupplierParty",
                    new XElement(Cac + "Party",
                        new XElement(Cac + "PartyIdentification",
                            new XElement(Cbc + "ID", new XAttribute("schemeID", "6"), config.RucEmisor)),
                        new XElement(Cac + "PartyLegalEntity",
                            new XElement(Cbc + "RegistrationName", config.RazonSocial)))),
                new XElement(Cac + "DeliveryCustomerParty",
                    new XElement(Cac + "Party",
                        new XElement(Cac + "PartyIdentification",
                            new XElement(Cbc + "ID", new XAttribute("schemeID", c.ClienteTipoDoc), c.ClienteNumDoc ?? "-")),
                        new XElement(Cac + "PartyLegalEntity",
                            new XElement(Cbc + "RegistrationName", c.ClienteNombre)))),
                new XElement(Cac + "Shipment",
                    new XElement(Cbc + "ID", "SUNAT_Envio"),
                    new XElement(Cbc + "HandlingCode", g.MotivoTrasladoCodigo),
                    string.IsNullOrWhiteSpace(g.MotivoTrasladoDescripcion) ? null
                        : new XElement(Cbc + "HandlingInstructions", g.MotivoTrasladoDescripcion),
                    new XElement(Cbc + "GrossWeightMeasure", new XAttribute("unitCode", g.UnidadPeso), Dec3(g.PesoBrutoTotal)),
                    g.NumeroBultos.HasValue ? new XElement(Cbc + "TotalTransportHandlingUnitQuantity", g.NumeroBultos.Value) : null,
                    etapa,
                    new XElement(Cac + "Delivery",
                        new XElement(Cac + "DeliveryAddress",
                            new XElement(Cbc + "ID",
                                new XAttribute("schemeAgencyName", "PE:INEI"),
                                new XAttribute("schemeName", "Ubigeos"), g.LlegadaUbigeo),
                            new XElement(Cac + "AddressLine", new XElement(Cbc + "Line", g.LlegadaDireccion)))),
                    new XElement(Cac + "OriginAddress",
                        new XElement(Cbc + "ID",
                            new XAttribute("schemeAgencyName", "PE:INEI"),
                            new XAttribute("schemeName", "Ubigeos"), g.PartidaUbigeo),
                        new XElement(Cac + "AddressLine", new XElement(Cbc + "Line", g.PartidaDireccion)))),
                items.Select((it, i) => new XElement(Cac + "DespatchLine",
                    new XElement(Cbc + "ID", i + 1),
                    new XElement(Cbc + "DeliveredQuantity", new XAttribute("unitCode", string.IsNullOrWhiteSpace(it.UnidadMedida) ? "NIU" : it.UnidadMedida), Dec3(it.Cantidad)),
                    new XElement(Cac + "OrderLineReference", new XElement(Cbc + "LineID", i + 1)),
                    new XElement(Cac + "Item", new XElement(Cbc + "Name", it.Descripcion))))));
    }

    private static XElement ConstruirFirma(ConfiguracionFacturacion config) =>
        new(Cac + "Signature",
            new XElement(Cbc + "ID", "SignatureSP"),
            new XElement(Cac + "SignatoryParty",
                new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", config.RucEmisor)),
                new XElement(Cac + "PartyName", new XElement(Cbc + "Name", config.RazonSocial))),
            new XElement(Cac + "DigitalSignatureAttachment",
                new XElement(Cac + "ExternalReference", new XElement(Cbc + "URI", "#SignatureSP"))));

    private static XElement ConstruirEmisor(ConfiguracionFacturacion config) =>
        new(Cac + "AccountingSupplierParty",
            new XElement(Cac + "Party",
                new XElement(Cac + "PartyIdentification", Documento(config.RucEmisor ?? "", "6")),
                new XElement(Cac + "PartyLegalEntity",
                    new XElement(Cbc + "RegistrationName", config.RazonSocial),
                    new XElement(Cac + "RegistrationAddress",
                        new XElement(Cbc + "ID", config.Ubigeo ?? "150110"),
                        new XElement(Cbc + "AddressTypeCode", config.CodigoEstablecimiento),
                        new XElement(Cac + "AddressLine", new XElement(Cbc + "Line", config.DireccionFiscal ?? "-")),
                        new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", "PE"))))));

    private static XElement ConstruirCliente(ComprobanteElectronico c) =>
        new(Cac + "AccountingCustomerParty",
            new XElement(Cac + "Party",
                new XElement(Cac + "PartyIdentification", Documento(c.ClienteNumDoc ?? "-", SchemeId(c.ClienteTipoDoc))),
                new XElement(Cac + "PartyLegalEntity", new XElement(Cbc + "RegistrationName", c.ClienteNombre))));

    private static XElement Documento(string numero, string schemeId) =>
        new(Cbc + "ID", new XAttribute("schemeID", schemeId),
            new XAttribute("schemeName", "Documento de Identidad"),
            new XAttribute("schemeAgencyName", "PE:SUNAT"),
            new XAttribute("schemeURI", "urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo06"), numero);

    private static XElement ConstruirImpuesto(ComprobanteElectronico c) =>
        new(Cac + "TaxTotal",
            new XElement(Cbc + "TaxAmount", Moneda(), Dec(c.Igv)),
            new XElement(Cac + "TaxSubtotal",
                new XElement(Cbc + "TaxableAmount", Moneda(), Dec(c.OpGravada)),
                new XElement(Cbc + "TaxAmount", Moneda(), Dec(c.Igv)),
                new XElement(Cac + "TaxCategory", new XElement(Cac + "TaxScheme",
                    new XElement(Cbc + "ID", "1000"), new XElement(Cbc + "Name", "IGV"),
                    new XElement(Cbc + "TaxTypeCode", "VAT")))));

    private static XElement? ConstruirAjuste(bool incluir, bool cargo, decimal monto, decimal baseAjuste, string codigo, string motivo)
    {
        if (!incluir) return null;
        return new XElement(Cac + "AllowanceCharge",
            new XElement(Cbc + "ChargeIndicator", cargo ? "true" : "false"),
            new XElement(Cbc + "AllowanceChargeReasonCode", codigo),
            new XElement(Cbc + "AllowanceChargeReason", motivo),
            baseAjuste > 0 ? new XElement(Cbc + "MultiplierFactorNumeric", Dec6(monto / baseAjuste)) : null,
            new XElement(Cbc + "Amount", Moneda(), Dec(monto)),
            new XElement(Cbc + "BaseAmount", Moneda(), Dec(baseAjuste)));
    }

    private record Linea(string Descripcion, string Unidad, decimal Cantidad, decimal PrecioUnitarioIgv,
        decimal ValorVenta, decimal Igv, decimal Total);

    private static List<Linea> AsignarLineas(ComprobanteElectronico c, List<PedidoItem> items)
    {
        if (c.Detalles.Count > 0)
            return c.Detalles.OrderBy(x => x.NumeroLinea).Select(x =>
                new Linea(x.Descripcion, x.UnidadMedida, x.Cantidad, x.PrecioUnitarioIgv, x.ValorVenta, x.Igv, x.Total)).ToList();
        if (items.Count == 0)
            return [new("Servicio de lavanderia", "ZZ", 1, c.Total, c.OpGravada, c.Igv, c.Total)];
        return items.Select(x =>
        {
            var valor = SinIgv(x.Total, c.IgvPorcentaje);
            return new Linea(x.ServicioNombre ?? "Servicio", "ZZ", x.Cantidad, x.PrecioUnit, valor, x.Total - valor, x.Total);
        }).ToList();
    }

    private static XElement ConstruirLinea(int numero, Linea l, decimal porcentaje,
        string nodoLinea = "InvoiceLine", string nodoCantidad = "InvoicedQuantity")
    {
        var precioSinIgv = l.Cantidad == 0 ? 0 : Math.Round(l.ValorVenta / l.Cantidad, 4, MidpointRounding.AwayFromZero);
        return new XElement(Cac + nodoLinea,
            new XElement(Cbc + "ID", numero),
            new XElement(Cbc + nodoCantidad, new XAttribute("unitCode", l.Unidad), Dec3(l.Cantidad)),
            new XElement(Cbc + "LineExtensionAmount", Moneda(), Dec(l.ValorVenta)),
            new XElement(Cac + "PricingReference", new XElement(Cac + "AlternativeConditionPrice",
                new XElement(Cbc + "PriceAmount", Moneda(), Dec4(l.PrecioUnitarioIgv)),
                new XElement(Cbc + "PriceTypeCode", "01"))),
            new XElement(Cac + "TaxTotal", new XElement(Cbc + "TaxAmount", Moneda(), Dec(l.Igv)),
                new XElement(Cac + "TaxSubtotal",
                    new XElement(Cbc + "TaxableAmount", Moneda(), Dec(l.ValorVenta)),
                    new XElement(Cbc + "TaxAmount", Moneda(), Dec(l.Igv)),
                    new XElement(Cac + "TaxCategory", new XElement(Cbc + "Percent", Dec(porcentaje)),
                        new XElement(Cbc + "TaxExemptionReasonCode", "10"),
                        new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "1000"),
                            new XElement(Cbc + "Name", "IGV"), new XElement(Cbc + "TaxTypeCode", "VAT"))))),
            new XElement(Cac + "Item", new XElement(Cbc + "Description", l.Descripcion)),
            new XElement(Cac + "Price", new XElement(Cbc + "PriceAmount", Moneda(), Dec4(precioSinIgv))));
    }

    private static string SchemeId(string tipo) => tipo switch { "RUC" => "6", "DNI" => "1", _ => "0" };
    private static XAttribute Moneda() => new("currencyID", "PEN");
    private static decimal SinIgv(decimal monto, decimal porcentaje) => Math.Round(monto / (1 + porcentaje / 100m), 2, MidpointRounding.AwayFromZero);
    private static string Dec(decimal valor) => valor.ToString("F2", CultureInfo.InvariantCulture);
    private static string Dec3(decimal valor) => valor.ToString("F3", CultureInfo.InvariantCulture);
    private static string Dec4(decimal valor) => valor.ToString("F4", CultureInfo.InvariantCulture);
    private static string Dec6(decimal valor) => valor.ToString("F6", CultureInfo.InvariantCulture);
}
