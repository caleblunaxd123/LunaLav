namespace Lavanderia.Api.Services;

/// <summary>
/// Reconoce el tipo real de una imagen leyendo su firma binaria (magic bytes).
/// El header Content-Type lo envia el cliente y es trivial de falsificar, asi que no basta
/// para decidir que guardamos en disco: la carpeta de fotos se sincroniza a la nube del
/// negocio, y no queremos que un .exe o un .html entren ahi disfrazados de .jpg.
/// </summary>
public static class ImagenValidador
{
    /// <summary>Tipo real de la imagen, o null si los bytes no son JPG/PNG/WEBP.</summary>
    public static (string ContentType, string Extension)? Detectar(byte[] datos)
    {
        if (datos.Length < 12) return null;

        // JPEG: FF D8 FF
        if (datos[0] == 0xFF && datos[1] == 0xD8 && datos[2] == 0xFF)
            return ("image/jpeg", ".jpg");

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (datos[0] == 0x89 && datos[1] == 0x50 && datos[2] == 0x4E && datos[3] == 0x47 &&
            datos[4] == 0x0D && datos[5] == 0x0A && datos[6] == 0x1A && datos[7] == 0x0A)
            return ("image/png", ".png");

        // WEBP: "RIFF" .... "WEBP"
        if (datos[0] == (byte)'R' && datos[1] == (byte)'I' && datos[2] == (byte)'F' && datos[3] == (byte)'F' &&
            datos[8] == (byte)'W' && datos[9] == (byte)'E' && datos[10] == (byte)'B' && datos[11] == (byte)'P')
            return ("image/webp", ".webp");

        return null;
    }
}
