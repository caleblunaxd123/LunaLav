namespace Lavanderia.Api.Services;

/// <summary>
/// Guarda y recupera los archivos de las fotos de pedidos. Hoy escribe en disco local
/// (carpeta configurable, pensada para respaldarse a la nube con Google Drive para Escritorio).
/// Esta abstraccion permite cambiar a un proveedor de nube (S3/Cloudinary) sin tocar los
/// controllers: solo se registra otra implementacion en Program.cs.
/// </summary>
public interface IAlmacenamientoFotos
{
    /// <summary>Guarda los bytes y devuelve el nombre de archivo generado (con extension).</summary>
    Task<string> GuardarAsync(int negocioId, int pedidoId, int numeroTicket, byte[] datos, string extension, CancellationToken ct = default);
    /// <summary>Abre el archivo para servirlo, o null si ya no existe en disco.</summary>
    Stream? Abrir(int negocioId, int pedidoId, string nombreArchivo);
    void Eliminar(int negocioId, int pedidoId, string nombreArchivo);

    /// <summary>Guarda el logo del negocio (reemplaza el anterior) y devuelve su nombre de archivo.</summary>
    Task<string> GuardarLogoAsync(int negocioId, byte[] datos, string extension, CancellationToken ct = default);
    /// <summary>Abre el logo del negocio para servirlo, o null si no hay.</summary>
    Stream? AbrirLogo(int negocioId, string nombreArchivo);
}

public class AlmacenamientoFotosLocal : IAlmacenamientoFotos
{
    private readonly string _raiz;

    public AlmacenamientoFotosLocal(IConfiguration config, IWebHostEnvironment env)
    {
        // Carpeta configurable (Fotos:Directorio). En produccion se apunta a la carpeta que
        // Google Drive para Escritorio sincroniza. Si no se define, se usa App_Data local.
        var configurado = config["Fotos:Directorio"];
        _raiz = string.IsNullOrWhiteSpace(configurado)
            ? Path.Combine(env.ContentRootPath, "App_Data", "fotos-pedidos")
            : configurado;
        Directory.CreateDirectory(_raiz);
    }

    private string CarpetaPedido(int negocioId, int pedidoId)
        => Path.Combine(_raiz, negocioId.ToString(), pedidoId.ToString());

    public async Task<string> GuardarAsync(int negocioId, int pedidoId, int numeroTicket, byte[] datos, string extension, CancellationToken ct = default)
    {
        var carpeta = CarpetaPedido(negocioId, pedidoId);
        Directory.CreateDirectory(carpeta);
        // Prefijo con el N° de ticket para ubicar la foto manualmente sin entrar al sistema;
        // el guid mantiene el nombre unico e imposible de adivinar (varias fotos por pedido).
        var nombre = $"ticket-{numeroTicket}-{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(carpeta, nombre), datos, ct);
        return nombre;
    }

    public Stream? Abrir(int negocioId, int pedidoId, string nombreArchivo)
    {
        // Defensa contra path traversal: el nombre lo generamos nosotros (guid), pero validamos.
        if (nombreArchivo.Contains('/') || nombreArchivo.Contains('\\') || nombreArchivo.Contains(".."))
            return null;
        var ruta = Path.Combine(CarpetaPedido(negocioId, pedidoId), nombreArchivo);
        return File.Exists(ruta) ? File.OpenRead(ruta) : null;
    }

    public void Eliminar(int negocioId, int pedidoId, string nombreArchivo)
    {
        if (nombreArchivo.Contains('/') || nombreArchivo.Contains('\\') || nombreArchivo.Contains(".."))
            return;
        var ruta = Path.Combine(CarpetaPedido(negocioId, pedidoId), nombreArchivo);
        if (File.Exists(ruta)) File.Delete(ruta);
    }

    // ---- Logo del negocio ----
    // Carpeta aparte de las fotos de pedidos: el logo es un archivo por negocio, se sirve
    // publicamente (el login lo muestra antes de autenticar) y se reemplaza al resubirlo.
    private string CarpetaLogos() => Path.Combine(_raiz, "logos");

    public async Task<string> GuardarLogoAsync(int negocioId, byte[] datos, string extension, CancellationToken ct = default)
    {
        var carpeta = CarpetaLogos();
        Directory.CreateDirectory(carpeta);
        // Sufijo aleatorio: al cambiar el nombre, el navegador y los caches no sirven el logo viejo.
        var nombre = $"negocio-{negocioId}-{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(carpeta, nombre), datos, ct);

        // Se borran los logos anteriores de este negocio para no acumular archivos huerfanos.
        foreach (var viejo in Directory.EnumerateFiles(carpeta, $"negocio-{negocioId}-*"))
        {
            if (Path.GetFileName(viejo) == nombre) continue;
            try { File.Delete(viejo); } catch (IOException) { /* si esta en uso, se limpia la proxima vez */ }
        }
        return nombre;
    }

    public Stream? AbrirLogo(int negocioId, string nombreArchivo)
    {
        if (nombreArchivo.Contains('/') || nombreArchivo.Contains('\\') || nombreArchivo.Contains(".."))
            return null;
        // El nombre lleva el negocio: impide pedir el logo de otro tenant manipulando la URL.
        if (!nombreArchivo.StartsWith($"negocio-{negocioId}-", StringComparison.Ordinal)) return null;
        var ruta = Path.Combine(CarpetaLogos(), nombreArchivo);
        return File.Exists(ruta) ? File.OpenRead(ruta) : null;
    }
}
