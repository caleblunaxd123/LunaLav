using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace Lavanderia.Api.Services.Facturacion;

/// <summary>
/// Firma digitalmente el XML UBL del comprobante con el certificado .pfx del negocio,
/// insertando el &lt;ds:Signature&gt; dentro de ext:ExtensionContent tal como exige SUNAT.
/// </summary>
public static class XmlSigner
{
    public static byte[] Firmar(byte[] xmlSinFirmar, byte[] certificadoPfx, string certificadoPassword)
    {
        var xmlDoc = new XmlDocument { PreserveWhitespace = true };
        using (var ms = new MemoryStream(xmlSinFirmar))
            xmlDoc.Load(ms);

        using var certificado = X509CertificateLoader.LoadPkcs12(
            certificadoPfx, certificadoPassword,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

        var signedXml = new SignedXml(xmlDoc)
        {
            SigningKey = certificado.GetRSAPrivateKey()
        };

        var reference = new Reference { Uri = "" };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        reference.DigestMethod = SignedXml.XmlDsigSHA256Url;
        signedXml.SignedInfo!.SignatureMethod = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
        signedXml.SignedInfo.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificado));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        var signatureElement = signedXml.GetXml();

        var extensionContent = xmlDoc.GetElementsByTagName("ext:ExtensionContent")
            .Cast<XmlNode>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No se encontro ext:ExtensionContent en el XML a firmar.");
        extensionContent.AppendChild(xmlDoc.ImportNode(signatureElement, true));

        using var salida = new MemoryStream();
        xmlDoc.Save(salida);
        return salida.ToArray();
    }

    public static string? ObtenerDigest(byte[]? xml)
    {
        if (xml is null || xml.Length == 0) return null;
        try
        {
            var doc = new XmlDocument();
            using var ms = new MemoryStream(xml);
            doc.Load(ms);
            return doc.GetElementsByTagName("DigestValue", SignedXml.XmlDsigNamespaceUrl)
                .Cast<XmlNode>().FirstOrDefault()?.InnerText;
        }
        catch { return null; }
    }
}
