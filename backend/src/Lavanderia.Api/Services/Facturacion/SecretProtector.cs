using Microsoft.AspNetCore.DataProtection;

namespace Lavanderia.Api.Services.Facturacion;

/// <summary>Cifra/descifra la clave SOL y la contraseña del certificado antes de guardarlas o usarlas.</summary>
public class SecretProtector
{
    private readonly IDataProtector _protector;
    public SecretProtector(IDataProtectionProvider provider) => _protector = provider.CreateProtector("Facturacion.Secrets.v1");

    public string Proteger(string valor) => _protector.Protect(valor);
    public string Desproteger(string valorCifrado) => _protector.Unprotect(valorCifrado);
}
