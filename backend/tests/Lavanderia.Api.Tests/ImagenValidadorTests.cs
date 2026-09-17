using Lavanderia.Api.Services;

namespace Lavanderia.Api.Tests;

/// <summary>
/// Regresión de la auditoría: la subida de fotos aceptaba cualquier contenido si el cliente
/// declaraba un Content-Type de imagen. Ahora el tipo se decide por los bytes reales.
/// </summary>
public class ImagenValidadorTests
{
    private static byte[] Con(params byte[] cabecera)
    {
        var b = new byte[32];
        cabecera.CopyTo(b, 0);
        return b;
    }

    [Fact]
    public void Detecta_jpeg_por_su_firma()
    {
        var r = ImagenValidador.Detectar(Con(0xFF, 0xD8, 0xFF, 0xE0));
        Assert.Equal(("image/jpeg", ".jpg"), r);
    }

    [Fact]
    public void Detecta_png_por_su_firma()
    {
        var r = ImagenValidador.Detectar(Con(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A));
        Assert.Equal(("image/png", ".png"), r);
    }

    [Fact]
    public void Detecta_webp_por_su_firma()
    {
        var b = Con((byte)'R', (byte)'I', (byte)'F', (byte)'F');
        b[8] = (byte)'W'; b[9] = (byte)'E'; b[10] = (byte)'B'; b[11] = (byte)'P';
        Assert.Equal(("image/webp", ".webp"), ImagenValidador.Detectar(b));
    }

    [Fact]
    public void Rechaza_html_disfrazado_de_imagen()
    {
        var html = System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        Assert.Null(ImagenValidador.Detectar(html));
    }

    [Fact]
    public void Rechaza_ejecutable_windows()
    {
        // "MZ" es la firma de un .exe: el caso que motivó la corrección (la carpeta de
        // fotos se sincroniza a la nube del negocio).
        Assert.Null(ImagenValidador.Detectar(Con(0x4D, 0x5A, 0x90, 0x00)));
    }

    [Fact]
    public void Rechaza_svg_que_puede_llevar_script()
    {
        var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>");
        Assert.Null(ImagenValidador.Detectar(svg));
    }

    [Fact]
    public void Rechaza_archivo_vacio_o_muy_corto()
    {
        Assert.Null(ImagenValidador.Detectar(Array.Empty<byte>()));
        Assert.Null(ImagenValidador.Detectar(new byte[] { 0xFF, 0xD8, 0xFF }));
    }
}
