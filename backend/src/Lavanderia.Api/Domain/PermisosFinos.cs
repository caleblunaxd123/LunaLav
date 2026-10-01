namespace Lavanderia.Api.Domain;

/// <summary>
/// Permisos "finos" (sub-permisos): controlan secciones o botones concretos DENTRO de un
/// módulo, más allá del acceso al módulo completo. Se guardan en la misma tabla RolPermiso
/// (columna Modulo = la Clave del permiso fino) y viajan al token junto con los módulos, así
/// que en el frontend se consultan igual: usuario.modulosPermitidos.includes(Clave).
///
/// El Administrador siempre los tiene (bypass por rol). Para roles de trabajador, un permiso
/// fino AUSENTE = oculto; el admin lo activa por rol desde "Roles y accesos".
///
/// Para agregar un sub-permiso nuevo: añade una línea a Catalogo con su Modulo padre, una
/// Clave única (PREFIJOMODULO_LOQUEHACE) y una Etiqueta amigable. El árbol de la pantalla de
/// permisos y el flujo de guardado lo toman automáticamente.
/// </summary>
public static class PermisosFinos
{
    public record Item(string Clave, string Modulo, string Etiqueta, string? Descripcion = null);

    public static readonly Item[] Catalogo =
    {
        // ─────────── CAJA ───────────
        new("CAJA_VER_CIERRES_ANTERIORES", "CAJA", "Ver cierres de días anteriores",
            "Acceso al botón que abre el historial de cierres de días pasados."),
        new("CAJA_VER_MONTOS_DIGITALES", "CAJA", "Ver montos digitales (Yape/Plin/Transf./Tarjeta)",
            "Muestra los importes cobrados por medios digitales en las ventas del día."),
        new("CAJA_REPORTE_CUADRES", "CAJA", "Ver “Reporte de cuadres”",
            "Acceso al reporte/historial de cuadres de todos los días."),
        new("CAJA_REGISTRAR_GASTO", "CAJA", "Registrar gasto",
            "Permite registrar gastos/egresos de caja."),
        new("CAJA_VER_OTROS_TURNOS", "CAJA", "Ver toda la caja del día (otros turnos)",
            "Permite ver los movimientos de todos los colaboradores, no solo su turno."),

        // ─────────── INICIO (Dashboard) ───────────
        new("INICIO_VER_MONTOS", "INICIO", "Ver montos de ventas / ingresos",
            "Muestra los importes de ventas e ingresos en el panel de inicio."),

        // ─────────── PEDIDOS ───────────
        new("PEDIDOS_ANULAR", "PEDIDOS", "Anular pedidos",
            "Permite anular un pedido desde su detalle."),

        // ─────────── REGISTRAR ───────────
        new("REGISTRAR_APLICAR_DESCUENTO", "REGISTRAR", "Aplicar descuentos",
            "Permite aplicar descuentos al registrar un pedido."),

        // ─────────── CLIENTES ───────────
        new("CLIENTES_FUSIONAR", "CLIENTES", "Fusionar clientes",
            "Permite combinar dos clientes en uno (acción sensible)."),

        // ─────────── INVENTARIO ───────────
        new("INVENTARIO_VER_COSTOS", "INVENTARIO", "Ver costos",
            "Muestra los costos/precios de compra en inventario y sus movimientos."),

        // ─────────── REPORTES ───────────
        new("REPORTES_VER_GERENCIAL", "REPORTES", "Ver Vista gerencial",
            "Acceso a la vista gerencial (indicadores financieros del negocio)."),
        new("REPORTES_VER_CONSOLIDADO", "REPORTES", "Ver Consolidado",
            "Acceso al reporte consolidado de todas las sedes."),
    };

    /// <summary>Todas las claves finas, para validar solicitudes de guardado.</summary>
    public static readonly HashSet<string> ClavesTodas =
        Catalogo.Select(i => i.Clave).ToHashSet();

    /// <summary>Claves finas de un módulo (para sembrar al crear un rol).</summary>
    public static IEnumerable<string> ClavesDeModulo(string modulo) =>
        Catalogo.Where(i => i.Modulo == modulo).Select(i => i.Clave);
}
