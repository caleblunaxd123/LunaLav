using System.Text.RegularExpressions;
using Lavanderia.Api.Auth;
using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Alta y administracion de empresas (negocios/tenants) del SaaS. Solo el rol PROPIETARIO
/// (dueno de la plataforma) puede usarlo. A proposito NO hereda de TenantAwareControllerBase:
/// no opera acotado a un negocio, opera SOBRE los negocios. El aislamiento frente a los datos
/// operativos de cada tenant (pedidos, clientes, caja, etc.) es automatico: PROPIETARIO no es
/// ADMIN/COORDINADOR/TRABAJADOR, asi que los demas controllers lo rechazan (403) sin cambios.
/// </summary>
[ApiController]
[Authorize(Roles = "PROPIETARIO")]
[Route("api/negocios")]
public class NegociosController : ControllerBase
{
    // Debe reflejar core/routing/tenant-url-serializer.ts:SEGMENTOS_RESERVADOS del frontend
    // (lista chica y estatica) para que un negocio nuevo no pueda elegir un slug que colisione
    // con una ruta real de la app.
    private static readonly HashSet<string> SegmentosReservados = new(StringComparer.OrdinalIgnoreCase)
    {
        "login", "ticket", "cuadre-caja", "seleccionar-sede", "inicio", "pedidos", "registrar",
        "registro-antiguo", "clientes", "promociones", "reportes", "inventario", "ajustes",
        "facturacion", "assets", "plataforma", "seguimiento", "repartidor", "recibo-suscripcion",
        "privacidad", "terminos",
    };
    private static readonly Regex SlugValido = new("^[a-z0-9][a-z0-9-]{1,49}$", RegexOptions.IgnoreCase);
    private static readonly Regex UsuarioValido = new("^[a-z0-9._-]{3,50}$", RegexOptions.IgnoreCase);
    private static readonly Regex EmailBasicoValido = new("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.IgnoreCase);

    private readonly INegocioRepository _negocios;
    private readonly ISedeRepository _sedes;
    private readonly IUsuarioRepository _usuarios;
    private readonly IRolRepository _roles;
    private readonly IRolPermisoRepository _permisos;
    private readonly IServicioRepository _servicios;
    private readonly IConfiguracionNegocioRepository _configuracion;
    private readonly INegocioAccessValidator _accessValidator;
    private readonly IPagoSuscripcionRepository _pagos;

    public NegociosController(INegocioRepository negocios, ISedeRepository sedes,
        IUsuarioRepository usuarios, IRolRepository roles, IRolPermisoRepository permisos,
        IServicioRepository servicios, IConfiguracionNegocioRepository configuracion,
        INegocioAccessValidator accessValidator, IPagoSuscripcionRepository pagos)
    {
        _negocios = negocios;
        _sedes = sedes;
        _usuarios = usuarios;
        _roles = roles;
        _permisos = permisos;
        _servicios = servicios;
        _configuracion = configuracion;
        _accessValidator = accessValidator;
        _pagos = pagos;
    }

    [HttpGet]
    public async Task<ActionResult<List<NegocioResumenDto>>> Listar(CancellationToken ct)
        => Ok(await _negocios.ListarConConteosAsync(ct));

    [HttpPost]
    public async Task<ActionResult<NegocioResumenDto>> Crear([FromBody] CrearNegocioRequest req, CancellationToken ct)
    {
        var slug = req.Slug.Trim().ToLowerInvariant();
        if (!SlugValido.IsMatch(slug) || SegmentosReservados.Contains(slug))
            return BadRequest(new { mensaje = "El slug debe ser corto (letras, numeros y guiones) y no puede ser una palabra reservada del sistema." });
        if (await _negocios.ExisteSlugAsync(slug, ct))
            return Conflict(new { mensaje = "Ya existe una empresa con ese slug." });

        var adminUsuario = req.AdminUsuario.Trim();
        if (!UsuarioValido.IsMatch(adminUsuario))
            return BadRequest(new { mensaje = "El usuario administrador solo puede usar letras, numeros, punto, guion y guion bajo." });

        var rucEmpresa = NormalizarOpcional(req.RucEmpresa);
        if (rucEmpresa is not null && !Regex.IsMatch(rucEmpresa, "^\\d{11}$"))
            return BadRequest(new { mensaje = "El RUC debe tener 11 digitos." });

        var titularEmail = NormalizarOpcional(req.TitularEmail);
        var adminEmail = NormalizarOpcional(req.AdminEmail);
        if (!EmailValido(titularEmail) || !EmailValido(adminEmail))
            return BadRequest(new { mensaje = "El email ingresado no tiene un formato valido." });

        var rolAdmin = await _roles.BuscarPorCodigoAsync("ADMIN", ct)
            ?? throw new InvalidOperationException("Rol ADMIN no encontrado.");

        var negocioId = await _negocios.CrearAsync(new Negocio
        {
            Nombre = req.Nombre.Trim(),
            Slug = slug,
            RucEmpresa = rucEmpresa,
            TitularNombre = NormalizarOpcional(req.TitularNombre),
            TitularEmail = titularEmail,
            TitularCelular = NormalizarOpcional(req.TitularCelular),
            Activo = true
        }, ct);

        var sedeId = await _sedes.CrearAsync(new Sede
        {
            NegocioId = negocioId,
            Nombre = req.SedeNombre.Trim(),
            Activo = true
        }, ct);

        await _usuarios.CrearAsync(new Usuario
        {
            UsuarioLogin = adminUsuario,
            NombreCompleto = req.AdminNombreCompleto.Trim(),
            Email = adminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.AdminPassword),
            RolId = rolAdmin.Id,
            RolCodigo = rolAdmin.Codigo,
            NegocioId = negocioId,
            SedeId = sedeId,
            Activo = true
        }, ct);

        await SembrarPermisosDefectoAsync(negocioId, ct);

        await _configuracion.ActualizarAsync(new ConfiguracionNegocio
        {
            NombreNegocio = req.Nombre.Trim(),
            ColorPrimario = "#0b57d0",
            ColorSecundario = "#29b6f6",
            ColorAcento = "#f5a623",
            Ruc = rucEmpresa,
            Igv = 18m,
            MetaMensual = 0m,
            SolesPorPunto = 1m,
            AnchoTicketMm = 80,
            MensajePieTicket = "Gracias por su preferencia.",
            CostoDelivery = 0m,
            // Tope de descuento por defecto: sin esto un negocio nuevo nace con "0 = sin tope"
            // y cualquier empleado con módulo REGISTRAR podría dejar pedidos en S/ 0.
            // El dueño puede subirlo/bajarlo (o ponerlo en 0 a propósito) en Ajustes → Puntos y descuentos.
            MaxDescuentoPct = 30m
        }, negocioId, ct);

        // Servicio de sistema que ancla el cargo de delivery configurable (ver 022_costo_delivery.sql).
        await _servicios.CrearAsync(new Servicio
        {
            NegocioId = negocioId,
            Nombre = "Servicio a Domicilio",
            Precio = 0,
            Unidad = "Unidad",
            Activo = true,
            EsCargoDelivery = true
        }, ct);

        var creado = (await _negocios.ListarConConteosAsync(ct)).First(n => n.Id == negocioId);
        return CreatedAtAction(nameof(Listar), creado);
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoNegocioRequest req, CancellationToken ct)
    {
        var actualizado = await _negocios.CambiarEstadoAsync(id, req.Activo, ct);
        if (!actualizado) return NotFound(new { mensaje = "Empresa no encontrada." });
        _accessValidator.Invalidar(id);
        return NoContent();
    }

    /// <summary>KPIs del negocio-de-negocios para el tablero del propietario.</summary>
    [HttpGet("resumen")]
    public async Task<ActionResult<PlataformaResumenDto>> Resumen(CancellationToken ct)
        => Ok(await _negocios.ObtenerResumenPlataformaAsync(ct));

    /// <summary>Ficha completa de una empresa: datos, suscripción, sedes, usuarios, actividad.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<NegocioDetalleDto>> Detalle(int id, CancellationToken ct)
    {
        var n = await _negocios.ObtenerPorIdAsync(id, ct);
        if (n is null || n.Slug == "plataforma-interna")
            return NotFound(new { mensaje = "Empresa no encontrada." });

        var sedes = await _sedes.ListarPorNegocioAsync(id, ct);
        var usuarios = await _usuarios.ListarTodosAsync(id, ct);
        var pedidosMes = await _negocios.ContarPedidosMesAsync(id, ct);
        var admin = usuarios.FirstOrDefault(u => u.RolCodigo == "ADMIN");
        var ultimoAcceso = usuarios.Where(u => u.UltimoAcceso.HasValue)
            .Select(u => u.UltimoAcceso!.Value).DefaultIfEmpty().Max();

        return Ok(new NegocioDetalleDto
        {
            Id = n.Id,
            Nombre = n.Nombre,
            Slug = n.Slug,
            RucEmpresa = n.RucEmpresa,
            TitularNombre = n.TitularNombre,
            TitularEmail = n.TitularEmail,
            TitularCelular = n.TitularCelular,
            NotasInternas = n.NotasInternas,
            Activo = n.Activo,
            FechaCreacion = n.FechaCreacion,
            PlanSuscripcion = n.PlanSuscripcion,
            EstadoSuscripcion = n.EstadoSuscripcion,
            MontoMensual = n.MontoMensual,
            ProximoPago = n.ProximoPago,
            PedidosMes = pedidosMes,
            UltimoAcceso = ultimoAcceso == default ? null : ultimoAcceso,
            AdminUsuario = admin?.UsuarioLogin,
            Sedes = sedes.Select(s => new SedeResumenDto(s.Id, s.Nombre, s.Direccion, s.Activo)).ToList(),
            Usuarios = usuarios.Select(u => new UsuarioResumenDto(u.Id, u.UsuarioLogin, u.NombreCompleto, u.RolCodigo, u.Activo, u.UltimoAcceso)).ToList()
        });
    }

    /// <summary>Editar datos de una empresa (nombre, RUC, titular, notas internas).</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Editar(int id, [FromBody] EditarNegocioRequest req, CancellationToken ct)
    {
        var n = await _negocios.ObtenerPorIdAsync(id, ct);
        if (n is null || n.Slug == "plataforma-interna")
            return NotFound(new { mensaje = "Empresa no encontrada." });

        var ruc = NormalizarOpcional(req.RucEmpresa);
        if (ruc is not null && !Regex.IsMatch(ruc, "^\\d{11}$"))
            return BadRequest(new { mensaje = "El RUC debe tener 11 digitos." });
        var titularEmail = NormalizarOpcional(req.TitularEmail);
        if (!EmailValido(titularEmail))
            return BadRequest(new { mensaje = "El email del titular no tiene un formato valido." });

        await _negocios.ActualizarDatosAsync(id, req.Nombre.Trim(), ruc,
            NormalizarOpcional(req.TitularNombre), titularEmail, NormalizarOpcional(req.TitularCelular), NormalizarOpcional(req.NotasInternas), ct);
        return NoContent();
    }

    /// <summary>Cambiar plan, estado, monto y próximo pago de la suscripción.</summary>
    [HttpPut("{id:int}/suscripcion")]
    public async Task<IActionResult> CambiarSuscripcion(int id, [FromBody] CambiarSuscripcionRequest req, CancellationToken ct)
    {
        var n = await _negocios.ObtenerPorIdAsync(id, ct);
        if (n is null || n.Slug == "plataforma-interna")
            return NotFound(new { mensaje = "Empresa no encontrada." });

        var planes = new[] { "BASICO", "PRO", "PREMIUM" };
        var estados = new[] { "PRUEBA", "ACTIVA", "VENCIDA", "SUSPENDIDA" };
        var plan = req.PlanSuscripcion.Trim().ToUpperInvariant();
        var estado = req.EstadoSuscripcion.Trim().ToUpperInvariant();
        if (!planes.Contains(plan)) return BadRequest(new { mensaje = "Plan inválido." });
        if (!estados.Contains(estado)) return BadRequest(new { mensaje = "Estado de suscripción inválido." });

        await _negocios.ActualizarSuscripcionAsync(id, plan, estado, req.MontoMensual, req.ProximoPago, ct);
        _accessValidator.Invalidar(id);
        return NoContent();
    }

    /// <summary>Restablece la contraseña del administrador de la empresa (soporte del propietario).
    /// Devuelve el nombre de usuario para que el propietario se lo comunique al cliente.</summary>
    [HttpPost("{id:int}/reset-password-admin")]
    public async Task<IActionResult> ResetPasswordAdmin(int id, [FromBody] ResetPasswordAdminRequest req, CancellationToken ct)
    {
        var n = await _negocios.ObtenerPorIdAsync(id, ct);
        if (n is null || n.Slug == "plataforma-interna")
            return NotFound(new { mensaje = "Empresa no encontrada." });

        if (!Regex.IsMatch(req.NuevaPassword, "^(?=.*[A-Za-z])(?=.*\\d).{8,}$"))
            return BadRequest(new { mensaje = "La contraseña debe tener al menos 8 caracteres, con letras y números." });

        var usuarios = await _usuarios.ListarTodosAsync(id, ct);
        var admin = usuarios.FirstOrDefault(u => u.RolCodigo == "ADMIN" && u.Activo);
        if (admin is null)
            return BadRequest(new { mensaje = "Esta empresa no tiene un administrador activo." });

        await _usuarios.ActualizarPasswordAsync(admin.Id, BCrypt.Net.BCrypt.HashPassword(req.NuevaPassword), id, ct);
        return Ok(new { usuario = admin.UsuarioLogin });
    }

    /// <summary>Restablece la contraseña de CUALQUIER usuario de la empresa (soporte del propietario):
    /// útil cuando un trabajador o coordinador pierde su acceso, no solo el administrador. El usuario
    /// se valida contra la empresa (ListarTodos ya está acotado por negocioId), evitando tocar usuarios
    /// de otra empresa. Devuelve el nombre de usuario para comunicárselo al cliente.</summary>
    [HttpPost("{id:int}/usuarios/{usuarioId:int}/reset-password")]
    public async Task<IActionResult> ResetPasswordUsuario(int id, int usuarioId, [FromBody] ResetPasswordAdminRequest req, CancellationToken ct)
    {
        var n = await _negocios.ObtenerPorIdAsync(id, ct);
        if (n is null || n.Slug == "plataforma-interna")
            return NotFound(new { mensaje = "Empresa no encontrada." });

        if (!Regex.IsMatch(req.NuevaPassword, "^(?=.*[A-Za-z])(?=.*\\d).{8,}$"))
            return BadRequest(new { mensaje = "La contraseña debe tener al menos 8 caracteres, con letras y números." });

        var usuarios = await _usuarios.ListarTodosAsync(id, ct);
        var usuario = usuarios.FirstOrDefault(u => u.Id == usuarioId);
        if (usuario is null)
            return NotFound(new { mensaje = "El usuario no existe en esta empresa." });
        if (!usuario.Activo)
            return BadRequest(new { mensaje = "Este usuario está inactivo; reactívalo antes de restablecer su clave." });

        await _usuarios.ActualizarPasswordAsync(usuario.Id, BCrypt.Net.BCrypt.HashPassword(req.NuevaPassword), id, ct);
        return Ok(new { usuario = usuario.UsuarioLogin });
    }

    // ---------- Cobranza: pagos de la suscripción ----------
    private static readonly string[] MetodosPago = { "YAPE", "PLIN", "TRANSFERENCIA", "EFECTIVO", "OTRO" };

    /// <summary>Registra un pago de la suscripción: deja el pago en el historial y avanza el
    /// próximo pago la cantidad de meses cubiertos, dejando la suscripción ACTIVA.</summary>
    [HttpPost("{id:int}/pagos")]
    public async Task<ActionResult<PagoSuscripcionDto>> RegistrarPago(int id, [FromBody] RegistrarPagoSuscripcionRequest req, CancellationToken ct)
    {
        var n = await _negocios.ObtenerPorIdAsync(id, ct);
        if (n is null || n.Slug == "plataforma-interna")
            return NotFound(new { mensaje = "Empresa no encontrada." });

        var metodo = (req.Metodo ?? "").Trim().ToUpperInvariant();
        if (!MetodosPago.Contains(metodo)) return BadRequest(new { mensaje = "Método de pago inválido." });
        if (req.Monto <= 0) return BadRequest(new { mensaje = "El monto debe ser mayor a S/ 0." });
        var meses = Math.Clamp(req.Meses, 1, 24);

        // El período cubierto arranca donde termina la cobertura vigente (si paga adelantado)
        // o desde hoy (si ya venció o nunca pagó).
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var desde = (n.ProximoPago.HasValue && n.ProximoPago.Value > hoy) ? n.ProximoPago.Value : hoy;
        var hasta = desde.AddMonths(meses);

        var pago = new PagoSuscripcion
        {
            NegocioId = id,
            Fecha = hoy,
            Monto = req.Monto,
            Metodo = metodo,
            PeriodoDesde = desde,
            PeriodoHasta = hasta,
            Nota = NormalizarOpcional(req.Nota)
        };
        pago.Id = await _pagos.CrearAsync(pago, ct);

        // Deja la suscripción al día: mismo plan/monto, estado ACTIVA y próximo pago = fin del período.
        await _negocios.ActualizarSuscripcionAsync(id, n.PlanSuscripcion, "ACTIVA", n.MontoMensual, hasta, ct);
        _accessValidator.Invalidar(id);

        return Ok(ToPagoDto(pago));
    }

    /// <summary>Historial de pagos de una empresa (más recientes primero).</summary>
    [HttpGet("{id:int}/pagos")]
    public async Task<ActionResult<List<PagoSuscripcionDto>>> ListarPagos(int id, CancellationToken ct)
    {
        var n = await _negocios.ObtenerPorIdAsync(id, ct);
        if (n is null || n.Slug == "plataforma-interna")
            return NotFound(new { mensaje = "Empresa no encontrada." });
        var pagos = await _pagos.ListarPorNegocioAsync(id, ct);
        return Ok(pagos.Select(ToPagoDto).ToList());
    }

    /// <summary>Un pago puntual (para generar su recibo imprimible).</summary>
    [HttpGet("{id:int}/pagos/{pagoId:int}")]
    public async Task<ActionResult<PagoSuscripcionDto>> ObtenerPago(int id, int pagoId, CancellationToken ct)
    {
        var pago = await _pagos.ObtenerAsync(pagoId, id, ct);
        if (pago is null) return NotFound(new { mensaje = "Pago no encontrado." });
        return Ok(ToPagoDto(pago));
    }

    private static PagoSuscripcionDto ToPagoDto(PagoSuscripcion p) =>
        new(p.Id, p.Fecha, p.Monto, p.Metodo, p.PeriodoDesde, p.PeriodoHasta, p.Nota, p.FechaCreacion);

    // Mismos defaults del producto (ver 021_propietario_plataforma.sql).
    // ADMIN no necesita filas: AuthController.ObtenerModulosAsync le da Modulos.Todos directo.
    private async Task SembrarPermisosDefectoAsync(int negocioId, CancellationToken ct)
    {
        // Cada negocio nace con dos roles de ejemplo PROPIOS (editables/eliminables por el dueño).
        // ADMIN es de sistema y no necesita filas (AuthController le da Modulos.Todos directo).
        var rolCoordinadorId = await _roles.CrearAsync(negocioId, "COORDINADOR", "Coordinador", ct);
        var rolTrabajadorId = await _roles.CrearAsync(negocioId, "TRABAJADOR", "Trabajador", ct);

        var coordinador = new (string Modulo, bool Puede)[]
        {
            ("INICIO", true), ("PEDIDOS", true), ("REGISTRAR", true), ("CAJA", true),
            ("CLIENTES", true), ("PROMOCIONES", false), ("REPORTES", true),
            ("INVENTARIO", true), ("AJUSTES", false)
        };
        var trabajador = new (string Modulo, bool Puede)[]
        {
            ("INICIO", true), ("PEDIDOS", true), ("REGISTRAR", true), ("CAJA", false),
            ("CLIENTES", false), ("PROMOCIONES", false), ("REPORTES", false),
            ("INVENTARIO", false), ("AJUSTES", false)
        };

        foreach (var (modulo, puede) in coordinador)
            await _permisos.GuardarAsync(rolCoordinadorId, modulo, puede, negocioId, ct);
        foreach (var (modulo, puede) in trabajador)
            await _permisos.GuardarAsync(rolTrabajadorId, modulo, puede, negocioId, ct);
    }

    private static string? NormalizarOpcional(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrWhiteSpace(limpio) ? null : limpio;
    }

    private static bool EmailValido(string? valor) => valor is null || EmailBasicoValido.IsMatch(valor);
}
