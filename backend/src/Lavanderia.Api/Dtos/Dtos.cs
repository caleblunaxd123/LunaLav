using System.ComponentModel.DataAnnotations;

namespace Lavanderia.Api.Dtos;

// ---------- Auth ----------
public record LoginRequest(
    [Required, StringLength(60, MinimumLength = 3)] string Usuario,
    [Required, StringLength(200, MinimumLength = 4)] string Password,
    string? EmpresaSlug = null);

public record LoginResponse(string AccessToken, DateTime Expira, string RefreshToken, UsuarioDto Usuario);

public record UsuarioDto(
    int Id, string Usuario, string NombreCompleto, string Rol, List<string> ModulosPermitidos,
    int NegocioId, int? SedeId, string? SedeNombre);

public record SeleccionarSedeRequest([Required] int SedeId, string? RefreshToken = null);

public record RefreshTokenRequest([Required] string RefreshToken);

// ---------- Sedes ----------
public class SedeDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [StringLength(200)] public string? Direccion { get; set; }
    [StringLength(30)] public string? Telefono { get; set; }
    public bool Activo { get; set; } = true;
}

// ---------- Usuarios (administración) ----------
public class UsuarioAdminDto
{
    public int Id { get; set; }
    [Required, StringLength(60, MinimumLength = 3)] public string Usuario { get; set; } = "";
    [Required, StringLength(120, MinimumLength = 2)] public string NombreCompleto { get; set; } = "";
    [EmailAddress, StringLength(120)] public string? Email { get; set; }
    [StringLength(200)] public string? Password { get; set; }
    [Required] public int RolId { get; set; }
    public int? SedeId { get; set; }
    public string? SedeNombre { get; set; }
    public string? RolCodigo { get; set; }
    public string? RolNombre { get; set; }
    public bool Activo { get; set; } = true;
}

public record RolDto(int Id, string Codigo, string Nombre);

// Gestión de roles de acceso propios del negocio (CRUD flexible).
public record RolAccesoDto(int Id, string Nombre, bool EsSistema, bool EnUso);
public record CrearRolRequest(string Nombre);
public record RenombrarRolRequest(string Nombre);
public record CambiarEstadoUsuarioRequest(bool Activo);

// ---------- Permisos ----------
public record PermisoItemDto(int RolId, string Modulo, bool PuedeAcceder);

public class ActualizarPermisosRequest
{
    [Required, MinLength(1)] public List<PermisoItemDto> Permisos { get; set; } = new();
}

// ---------- Configuracion Negocio ----------
public class ConfiguracionNegocioDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)]
    public string NombreNegocio { get; set; } = "";
    // Acepta una URL absoluta (logo alojado fuera) o la ruta relativa que devuelve
    // POST /api/configuracion/logo cuando el usuario lo sube desde su equipo.
    [StringLength(500)] public string? LogoUrl { get; set; }
    public string ColorPrimario { get; set; } = "#0b57d0";
    public string ColorSecundario { get; set; } = "#29b6f6";
    public string ColorAcento { get; set; } = "#f5a623";
    [StringLength(200)] public string? Direccion { get; set; }
    [StringLength(30)] public string? Telefono { get; set; }
    [StringLength(20)] public string? Ruc { get; set; }
    [StringLength(120)] public string? HorarioAtencion { get; set; }
    [Range(0, 100)] public decimal Igv { get; set; } = 18m;
    public decimal MetaMensual { get; set; }
    [Range(0.01, 100000)] public decimal SolesPorPunto { get; set; } = 1m;
    [Range(50, 120)] public int AnchoTicketMm { get; set; } = 80;
    [StringLength(300)] public string? MensajePieTicket { get; set; }
    [StringLength(6000)] public string? CondicionesServicio { get; set; }
    [StringLength(500)] public string? NotasProduccion { get; set; }
    [Range(0, 1000)] public decimal CostoDelivery { get; set; }
    [Range(0, 100)] public decimal ValorPuntoCanje { get; set; }   // S/ que vale 1 punto al canjear (0 = off)
    [Range(0, 100)] public decimal MaxDescuentoPct { get; set; }    // tope de descuento manual (0 = sin tope)
    // Cobro por Yape/Plin del negocio. YapeQrUrl acepta la ruta que devuelve POST /configuracion/yape-qr.
    [StringLength(30)] public string? YapeNumero { get; set; }
    [StringLength(120)] public string? YapeTitular { get; set; }
    [StringLength(500)] public string? YapeQrUrl { get; set; }
    // Solo lectura: id del servicio de sistema al que Registrar debe apuntar al agregar
    // automaticamente el cargo de delivery al carrito (ver 022_costo_delivery.sql). Se ignora
    // si viene en el body de un PUT.
    public int? ServicioDeliveryId { get; set; }
}

// ---------- Cliente ----------
public class ClienteDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)]
    public string Nombre { get; set; } = "";
    [StringLength(25)]
    [RegularExpression(@"^\+?\d{4,20}$", ErrorMessage = "Celular inválido. Ingresa solo números (para el extranjero, empieza con + y el código de país).")]
    public string? Celular { get; set; }
    [StringLength(8, MinimumLength = 8)]
    [RegularExpression(@"^\d{8}$", ErrorMessage = "El DNI debe tener 8 digitos.")]
    public string? Dni { get; set; }
    [StringLength(11, MinimumLength = 11)]
    [RegularExpression(@"^\d{11}$", ErrorMessage = "El RUC debe tener 11 digitos.")]
    public string? DocumentoFiscal { get; set; }
    [StringLength(200)] public string? Direccion { get; set; }
    public int Puntos { get; set; }
    public DateTime? FechaCreacion { get; set; }
    public DateOnly? FechaNacimiento { get; set; }
}

public record ClienteFrecuenteDto(int ClienteId, string Nombre, string? Celular, int Visitas);
public record FusionarClientesRequest([Required] int OrigenId, [Required] int DestinoId);

// ---------- CRM ----------
public record ClienteAnaliticaDto(int ClienteId, string Nombre, string? Celular, int TotalPedidos, decimal TicketPromedio, DateTime UltimaCompra, int DiasSinComprar, decimal DeudaTotal);
public record ClienteCumpleanosDto(int ClienteId, string Nombre, string? Celular, DateOnly FechaNacimiento, int DiasParaCumpleanos);

public class MovimientoPuntosDto
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public DateTime Fecha { get; set; }
    public string Motivo { get; set; } = "";
    public int Puntos { get; set; }
    public string Tipo { get; set; } = "SUMA";
    public string? UsuarioNombre { get; set; }
}

public class CrearMovimientoPuntosRequest
{
    [Required, StringLength(200, MinimumLength = 2)] public string Motivo { get; set; } = "";
    [Range(1, 100000)] public int Puntos { get; set; }
    [Required] public string Tipo { get; set; } = "SUMA";
}

// ---------- Paginación ----------
public class PagedResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int TamanoPagina { get; set; }
}

// ---------- Pedido ----------
public class PedidoItemDto
{
    public int Id { get; set; }
    [Range(1, int.MaxValue)] public int ServicioId { get; set; }
    public string? ServicioNombre { get; set; }
    public string? ServicioUnidad { get; set; }
    [Range(0.01, 10000)] public decimal Cantidad { get; set; }
    public decimal PrecioUnit { get; set; }
    public decimal Total { get; set; }
    [StringLength(200)] public string? Descripcion { get; set; }
    /// <summary>Cantidad ya entregada de este ítem (entregas parciales). Pendiente = Cantidad - CantidadEntregada.</summary>
    public decimal CantidadEntregada { get; set; }
}

public class CrearPedidoRequest
{
    public int? ClienteId { get; set; }
    public ClienteDto? ClienteNuevo { get; set; }
    [Required] public string Modalidad { get; set; } = "Tienda";
    [StringLength(250)] public string? DireccionEntrega { get; set; }
    [StringLength(100)] public string? DistritoEntrega { get; set; }
    [StringLength(250)] public string? ReferenciaEntrega { get; set; }
    [Range(-90d, 90d)] public decimal? LatitudEntrega { get; set; }
    [Range(-180d, 180d)] public decimal? LongitudEntrega { get; set; }
    [Required, MinLength(1)] public List<PedidoItemDto> Items { get; set; } = new();
    [Range(0, 100)] public decimal DescuentoPct { get; set; }
    [Range(0, 100000)] public int? PuntosACanjear { get; set; }
    /// <summary>Código de promoción aplicado (para marcar su consumo si es de un solo uso).</summary>
    [StringLength(30)] public string? CodigoPromocion { get; set; }
    public bool EsUrgente { get; set; }
    [Range(0, 100)] public decimal RecargoUrgentePct { get; set; } = 20m;
    // Tarifa de domicilio acordada para este pedido. Si no llega, se usa la tarifa
    // configurada por el negocio como valor por defecto.
    [Range(0, 10000)] public decimal? CostoDelivery { get; set; }
    [Range(0, 1000000)] public decimal MontoPagado { get; set; }
    [Required, StringLength(30)] public string MetodoPagoInicial { get; set; } = "EFECTIVO";
    public DateTime? FechaEntregaEst { get; set; }
    [StringLength(500)] public string? Observaciones { get; set; }
    public int? AreaInicialId { get; set; }
    public DateTime? FechaIngreso { get; set; }
    [StringLength(30)] public string? CodigoAntiguo { get; set; }
}

public class PedidoDto
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public int ClienteId { get; set; }
    public string? ClienteNombre { get; set; }
    public string? ClienteCelular { get; set; }
    public string? ClienteDni { get; set; }
    public int ClientePuntos { get; set; }
    public string? UsuarioNombre { get; set; }
    public DateTime FechaIngreso { get; set; }
    public DateTime? FechaEntregaEst { get; set; }
    public string Modalidad { get; set; } = "";
    public string? DireccionEntrega { get; set; }
    public string? DistritoEntrega { get; set; }
    public string? ReferenciaEntrega { get; set; }
    public decimal? LatitudEntrega { get; set; }
    public decimal? LongitudEntrega { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public bool EsUrgente { get; set; }
    public decimal RecargoUrgente { get; set; }
    public decimal Redondeo { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public string EstadoPago { get; set; } = "";
    public string EstadoProceso { get; set; } = "";
    public int? AreaActualId { get; set; }
    public string? AreaActualNombre { get; set; }
    public string? Observaciones { get; set; }
    public bool Anulado { get; set; }
    public string? MotivoAnulacion { get; set; }
    public string? CodigoAntiguo { get; set; }
    public int? MotorizadoId { get; set; }
    public string? MotorizadoNombre { get; set; }
    public string? MotorizadoCelular { get; set; }
    public List<PedidoItemDto> Items { get; set; } = new();
}

public record AvanzarAreaRequest(int? NuevaAreaId, string NuevoEstado, string? Nota);

public class PedidoHistorialDto
{
    public int Id { get; set; }
    public int? AreaId { get; set; }
    public string? AreaNombre { get; set; }
    public string EstadoProceso { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string ActorTipo { get; set; } = "USUARIO";
    public string? ActorDescripcion { get; set; }
    public string? Nota { get; set; }
    public bool NotificadoWsp { get; set; }
}

public class DashboardDto
{
    public Dictionary<string, int> PedidosPorEstado { get; set; } = new();
    public List<AreaConteoDto> PedidosPorArea { get; set; } = new();
    public decimal VentasDelDia { get; set; }
    public decimal? CobradoDelDia { get; set; }
    public decimal? SaldoPorCobrar { get; set; }
    public decimal? CajaEsperadaHoy { get; set; }
    public int PedidosEntregadosHoy { get; set; }
    public int PedidosEntregadosTiendaHoy { get; set; }
    public int PedidosEntregadosDomicilioHoy { get; set; }
    public int PedidosEntregadosSemana { get; set; }
    public int PedidosEntregadosMes { get; set; }
    public int TotalPendientes { get; set; }
    public int TotalListos { get; set; }
    public int TotalEnProceso { get; set; }
    public int PedidosDelMes { get; set; }
    public decimal MetaMensual { get; set; }
    public int? InsumosBajoStock { get; set; }
    public int? ComprobantesPendientes { get; set; }
    public int? ComprobantesRechazados { get; set; }
    public List<SlaAreaDto> SlaPorArea { get; set; } = new();
    public int TotalPedidosEstancados { get; set; }
    public List<PedidoEstancadoDto> PedidosEstancados { get; set; } = new();
    public int TotalPedidosAbandonados { get; set; }
    public List<PedidoAbandonadoDto> PedidosAbandonados { get; set; } = new();
    public DateTime ActualizadoEn { get; set; }

    // --- Bloques estilo panel (dashboard visual): comparativos, actividad y distribución ---
    public int OrdenesHoy { get; set; }
    public int OrdenesAyer { get; set; }
    public decimal VentasAyer { get; set; }
    public int TotalClientes { get; set; }
    public int ClientesNuevosMes { get; set; }
    public int ClientesNuevosMesAnterior { get; set; }
    /// <summary>Ventas por día de la semana actual (lunes→domingo), rellenas con 0.</summary>
    public List<PuntoTendenciaDto> VentasSemana { get; set; } = new();
    /// <summary>Últimas órdenes ingresadas (para la tabla de actividad reciente).</summary>
    public List<OrdenRecienteDto> OrdenesRecientes { get; set; } = new();
    /// <summary>Servicios más solicitados del mes (para la dona de distribución).</summary>
    public List<TopServicioGerencialDto> ServiciosMasSolicitados { get; set; } = new();
}

public record OrdenRecienteDto(int Numero, string ClienteNombre, string ServicioPrincipal, string EstadoProceso, decimal Total);

/// <summary>Piezas extra del dashboard que no salían de la vista gerencial (comparativos día/mes,
/// clientes, serie semanal y actividad reciente). Se calculan en una sola pasada al repositorio.</summary>
public class DashboardExtrasDto
{
    public int OrdenesHoy { get; set; }
    public int OrdenesAyer { get; set; }
    public decimal VentasAyer { get; set; }
    public int TotalClientes { get; set; }
    public int ClientesNuevosMes { get; set; }
    public int ClientesNuevosMesAnterior { get; set; }
    public List<PuntoTendenciaDto> VentasSemana { get; set; } = new();
    public List<OrdenRecienteDto> OrdenesRecientes { get; set; } = new();
}

public class PedidoContadoresDto
{
    public int PedidosDelMes { get; set; }
    public int TotalPendientes { get; set; }
    public int TotalOtros { get; set; }
    public int TotalUltimos { get; set; }
}

public record AreaConteoDto(int AreaId, string AreaNombre, int Cantidad);

public record PedidoAbandonadoDto(
    int PedidoId, int Numero, string ClienteNombre, string? ClienteCelular,
    decimal Total, decimal MontoPagado, DateTime FechaListo, int DiasEsperando);

public class RegistrarPagoRequest
{
    [Range(0.01, 100000)] public decimal Monto { get; set; }
    [Required] public string Metodo { get; set; } = "EFECTIVO";  // EFECTIVO | YAPE | PLIN | TRANSFERENCIA | POS | TARJETA
    [StringLength(300)] public string? Descripcion { get; set; }
}

/// <summary>Una línea de cobro (permite pago mixto: parte efectivo, parte Yape, etc.).</summary>
public class PagoLineaDto
{
    [Range(0.01, 100000)] public decimal Monto { get; set; }
    [Required] public string Metodo { get; set; } = "EFECTIVO";
}

/// <summary>Un ítem que se entrega en esta entrega, con la cantidad entregada ahora.</summary>
public class EntregaItemDto
{
    [Range(1, int.MaxValue)] public int PedidoItemId { get; set; }
    [Range(0.01, 100000)] public decimal Cantidad { get; set; }
}

/// <summary>
/// Registra una entrega (parcial o total) de un pedido: qué prendas se lleva el cliente esta
/// vez y con qué pagos (uno o varios métodos). No exige pagar el total: el saldo queda por cobrar.
/// </summary>
public class EntregarPedidoRequest
{
    /// <summary>Ítems entregados en esta visita. Vacío = no se entregan prendas ahora (solo cobro).</summary>
    public List<EntregaItemDto> Items { get; set; } = new();
    /// <summary>Cobros de esta visita, uno por método (pago mixto). Vacío = no se cobra ahora.</summary>
    public List<PagoLineaDto> Pagos { get; set; } = new();
    [StringLength(120)] public string? RecibidoPor { get; set; }
    [StringLength(300)] public string? Nota { get; set; }
}

public class EntregaDetalleDto
{
    public int PedidoItemId { get; set; }
    public string? ServicioNombre { get; set; }
    public string? ServicioUnidad { get; set; }
    public decimal Cantidad { get; set; }
}

public class PedidoEntregaDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? RecibidoPor { get; set; }
    public string? Nota { get; set; }
    public bool EsFinal { get; set; }
    public decimal MontoCobrado { get; set; }
    public List<EntregaDetalleDto> Items { get; set; } = new();
}

public class AgregarItemRequest
{
    [Range(1, int.MaxValue)] public int ServicioId { get; set; }
    [Range(0.01, 10000)] public decimal Cantidad { get; set; }
    [StringLength(200)] public string? Descripcion { get; set; }
}

public record AnularPedidoRequest([Required, StringLength(200, MinimumLength = 3)] string Motivo);

public class CambiarFechaEntregaRequest
{
    [Required] public DateTime Fecha { get; set; }
    [StringLength(200)] public string? Motivo { get; set; }
}

// Servicios editables
public class ServicioEditableDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Range(0.01, 10000)] public decimal Precio { get; set; }
    [Range(0, 10000)] public decimal Costo { get; set; }
    [Required, StringLength(30)] public string Unidad { get; set; } = "";
    public int? CategoriaId { get; set; }
    public string? CategoriaNombre { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>True si ya se usó en algún pedido: no se puede eliminar, solo desactivar.</summary>
    public bool EnUso { get; set; }
}

// Importación masiva de servicios (desde CSV / pegado de Excel en el frontend)
public class ImportarServiciosRequest
{
    public List<ImportarServicioFila> Filas { get; set; } = new();
    /// <summary>Si una categoría del archivo no existe, se crea automáticamente.</summary>
    public bool CrearCategorias { get; set; } = true;
}

public class ImportarServicioFila
{
    public string? Nombre { get; set; }
    public decimal Precio { get; set; }
    public string? Unidad { get; set; }
    public string? Categoria { get; set; }
}

public class ImportarServiciosResultado
{
    public int Creados { get; set; }
    public int Omitidos { get; set; }
    public List<string> CategoriasCreadas { get; set; } = new();
    public List<ImportarFilaError> Errores { get; set; } = new();
}

public class ImportarFilaError
{
    public int Fila { get; set; }
    public string Nombre { get; set; } = "";
    public string Motivo { get; set; } = "";
}

// Importación masiva de clientes (mismo patrón que servicios)
public class ImportarClientesRequest
{
    public List<ImportarClienteFila> Filas { get; set; } = new();
}

public class ImportarClienteFila
{
    public string? Nombre { get; set; }
    public string? Celular { get; set; }
    public string? Dni { get; set; }
    public string? Direccion { get; set; }
}

public class ImportarClientesResultado
{
    public int Creados { get; set; }
    public int Omitidos { get; set; }
    public List<ImportarFilaError> Errores { get; set; } = new();
}

// Importación masiva de insumos (inventario)
public class ImportarInsumosRequest
{
    public List<ImportarInsumoFila> Filas { get; set; } = new();
}

public class ImportarInsumoFila
{
    public string? Nombre { get; set; }
    public string? UnidadMedida { get; set; }
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public string? Clase { get; set; }   // EQUIPO | MATERIAL | INSUMO (opcional; por defecto INSUMO)
}

public class ImportarInsumosResultado
{
    public int Creados { get; set; }
    public int Omitidos { get; set; }
    public List<ImportarFilaError> Errores { get; set; } = new();
}

// ---------- Categorías ----------
public class CategoriaDto
{
    public int Id { get; set; }
    [Required, StringLength(80, MinimumLength = 2)] public string Nombre { get; set; } = "";
    public bool Activa { get; set; } = true;
    /// <summary>True si tiene servicios asociados: no se puede eliminar, solo desactivar.</summary>
    public bool EnUso { get; set; }
}

// ---------- Tipos de gasto ----------
public class TipoGastoEditableDto
{
    public int Id { get; set; }
    [Required, StringLength(80, MinimumLength = 2)] public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
    /// <summary>True si tiene movimientos de caja asociados: no se puede eliminar, solo desactivar.</summary>
    public bool EnUso { get; set; }
}

// ---------- Inventario de consumibles ----------
public class InsumoDto
{
    public int Id { get; set; }
    [Required, StringLength(80, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Required, StringLength(20)] public string UnidadMedida { get; set; } = "";
    /// <summary>EQUIPO, MATERIAL o INSUMO (consumible). Si viene vacío/ inválido se usa INSUMO.</summary>
    public string Clase { get; set; } = "INSUMO";
    /// <summary>Contenido de cada unidad, ej. 20 (litros). Opcional.</summary>
    [Range(0, 1000000)] public decimal? ContenidoValor { get; set; }
    [StringLength(20)] public string? ContenidoUnidad { get; set; }
    public decimal StockActual { get; set; }
    [Range(0, 1000000)] public decimal StockMinimo { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime? UltimaCompra { get; set; }
    /// <summary>Fecha de ingreso/registro al inventario (opcional), formato YYYY-MM-DD.</summary>
    public DateOnly? FechaIngreso { get; set; }
    /// <summary>Fecha de vencimiento/caducidad (opcional), formato YYYY-MM-DD.</summary>
    public DateOnly? FechaVencimiento { get; set; }
    /// <summary>True si tiene movimientos registrados: no se puede eliminar, solo desactivar.</summary>
    public bool EnUso { get; set; }
}

public class RegistrarMovimientoInsumoRequest
{
    [Required] public string Tipo { get; set; } = "";  // COMPRA | CONSUMO | AJUSTE
    [Range(-1000000, 1000000)] public decimal Cantidad { get; set; }
    [Range(0, 1000000)] public decimal? CostoTotal { get; set; }
    [StringLength(30)] public string? MetodoPago { get; set; }
    public int? TipoGastoId { get; set; }
    [StringLength(300)] public string? Descripcion { get; set; }
    /// <summary>Fecha del movimiento (cualquier tipo). Si es null se usa la fecha/hora actual.</summary>
    public DateTime? Fecha { get; set; }
}

/// <summary>Corrección de un movimiento ya registrado: solo fecha y nota (no toca el stock).</summary>
public class EditarMovimientoInsumoRequest
{
    [Required] public DateTime Fecha { get; set; }
    [StringLength(300)] public string? Descripcion { get; set; }
}

public class MovimientoInsumoDto
{
    public int Id { get; set; }
    public int InsumoId { get; set; }
    public string? InsumoNombre { get; set; }
    public string Tipo { get; set; } = "";
    public decimal Cantidad { get; set; }
    public decimal? CostoTotal { get; set; }
    public DateTime Fecha { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? Descripcion { get; set; }
}

// ---------- Motorizados (logistica de delivery) ----------
public class MotorizadoDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [StringLength(25)]
    [RegularExpression(@"^\+?\d{4,20}$", ErrorMessage = "Celular inválido. Ingresa solo números (para el extranjero, empieza con + y el código de país).")]
    public string? Celular { get; set; }
    public bool Activo { get; set; } = true;
}

public record AsignarMotorizadoRequest(int? MotorizadoId);

// ---------- Roles de personal (cargos) ----------
public class RolPersonalDto
{
    public int Id { get; set; }
    [Required, StringLength(60, MinimumLength = 2)] public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
}

// ---------- Personal ----------
public class EmpleadoDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [StringLength(8, MinimumLength = 8)]
    [RegularExpression(@"^\d{8}$", ErrorMessage = "El DNI debe tener 8 digitos.")]
    public string? Dni { get; set; }
    [StringLength(25)]
    [RegularExpression(@"^\+?\d{4,20}$", ErrorMessage = "Celular inválido. Ingresa solo números (para el extranjero, empieza con + y el código de país).")]
    public string? Celular { get; set; }
    [StringLength(60)] public string? Cargo { get; set; }
    public DateOnly? FechaIngreso { get; set; }
    public bool Activo { get; set; } = true;
}

// ---------- Plantillas de WhatsApp ----------
public class PlantillaWhatsappDto
{
    public int Id { get; set; }
    public string Evento { get; set; } = "";
    [Required, StringLength(1000, MinimumLength = 3)] public string Mensaje { get; set; } = "";
    public bool Activa { get; set; } = true;
}

public record PlantillaWhatsappActivaDto(string Evento, string Mensaje);

// ---------- Reportes ----------
public class ReporteResultDto
{
    public List<string> Columnas { get; set; } = new();
    public List<Dictionary<string, string>> Filas { get; set; } = new();
    /// <summary>Acción operativa disponible por fila (ej: "donar", "reenviar-almacen").
    /// Si no es null, cada fila incluye la clave "_id" con el Id del pedido.</summary>
    public string? Accion { get; set; }
}

// ---------- Tablero SLA / cuellos de botella ----------
public record SlaAreaDto(int AreaId, string AreaNombre, int Orden, int TiempoEstMinutos, double MinutosPromedioReal, int PedidosProcesados);
public record PedidoEstancadoDto(int PedidoId, int Numero, string ClienteNombre, int AreaId, string AreaNombre, int MinutosEnArea, int TiempoEstMinutos);

public class TableroSlaDto
{
    public List<SlaAreaDto> Areas { get; set; } = new();
    public List<PedidoEstancadoDto> Estancados { get; set; } = new();
}

// ---------- Vista gerencial unificada ----------
public class VistaGerencialDto
{
    public decimal VentasHoy { get; set; }
    public decimal CobradoHoy { get; set; }
    public decimal VentasMes { get; set; }
    public int PedidosEntregadosHoy { get; set; }
    public int PedidosEntregadosTiendaHoy { get; set; }
    public int PedidosEntregadosDomicilioHoy { get; set; }
    public int PedidosEntregadosSemana { get; set; }
    public int PedidosEntregadosMes { get; set; }
    public decimal SaldoPorCobrar { get; set; }
    public decimal GastosMes { get; set; }
    public decimal UtilidadMes { get; set; }
    public int PedidosActivos { get; set; }
    public int PedidosListosSinRecoger { get; set; }
    public int ComprobantesPendientes { get; set; }
    public int ComprobantesRechazados { get; set; }
    public int InsumosBajoStock { get; set; }
    public decimal CajaEsperadaHoy { get; set; }

    // --- Bloques pensados para el gerente: tendencia, comparativos y composición ---
    /// <summary>Ventas por día de los últimos 14 días (relleno con 0 en días sin ventas).</summary>
    public List<PuntoTendenciaDto> VentasUltimos14Dias { get; set; } = new();
    /// <summary>Ventas del mes anterior completo, para comparar contra VentasMes.</summary>
    public decimal VentasMesAnterior { get; set; }
    /// <summary>Ventas acumuladas del mes anterior hasta el MISMO día (comparación justa mes-a-mes).</summary>
    public decimal VentasMesAnteriorAlDia { get; set; }
    /// <summary>Cantidad de pedidos ingresados en el mes (para el ticket promedio).</summary>
    public int PedidosMesCount { get; set; }
    /// <summary>Ticket promedio del mes = VentasMes / PedidosMesCount.</summary>
    public decimal TicketPromedioMes { get; set; }
    // Composición de ingresos cobrados en el mes por medio de pago
    public decimal IngresosEfectivoMes { get; set; }
    public decimal IngresosDigitalMes { get; set; }
    public decimal IngresosTarjetaMes { get; set; }
    // Embudo de pedidos por estado (activos ahora)
    public int PedidosPendientes { get; set; }
    public int PedidosEnProceso { get; set; }
    /// <summary>Top servicios por facturación del mes (máx. 5).</summary>
    public List<TopServicioGerencialDto> TopServiciosMes { get; set; } = new();
}

public record PuntoTendenciaDto(string Fecha, decimal Total);
public record TopServicioGerencialDto(string Nombre, decimal Cantidad, decimal Total);

public record ConsolidadoSedeDto(int SedeId, string SedeNombre, decimal VentasHoy, decimal VentasMes,
    decimal SaldoPorCobrar, int PedidosActivos, int PedidosListos);

// ---------- Áreas de lavado ----------
public class AreaLavadoEditableDto
{
    public int Id { get; set; }
    [Required, StringLength(60, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Range(1, 100)] public int Orden { get; set; }
    [Range(1, 1000)] public int TiempoEstMinutos { get; set; } = 30;
    public bool Activa { get; set; } = true;
    /// <summary>True si algún pedido pasó por esta área: no se puede eliminar, solo desactivar.</summary>
    public bool EnUso { get; set; }
}

// ---------- Promociones ----------
public class PromocionDto
{
    public int Id { get; set; }
    [Required, StringLength(60, MinimumLength = 2)] public string Tipo { get; set; } = "VOLUMEN";
    [Required, StringLength(200, MinimumLength = 3)] public string Descripcion { get; set; } = "";
    [Range(0, 100)] public decimal? DescuentoPct { get; set; }
    [Range(0, 100000)] public decimal? DescuentoMonto { get; set; }
    public int? ServicioId { get; set; }
    public string? ServicioNombre { get; set; }
    [Range(0.01, 100000)] public decimal CantidadMinima { get; set; } = 1;
    public DateOnly? FechaInicio { get; set; }
    public DateOnly? FechaFin { get; set; }
    public bool Activa { get; set; } = true;
    [StringLength(30)] public string? Codigo { get; set; }
    // Códigos generados (solo lectura desde el listado)
    public int? ClienteId { get; set; }
    public string? ClienteNombre { get; set; }
    public string? Origen { get; set; }
    public int? MaxUsos { get; set; }
    public int Usos { get; set; }
}

/// <summary>Petición del generador automático de códigos de descuento.</summary>
public class GenerarCodigoRequest
{
    /// <summary>NUEVO | CUMPLE | REFERIDO | PUNTOS</summary>
    [Required] public string Origen { get; set; } = "";
    /// <summary>Cliente al que pertenece el código. Obligatorio para CUMPLE y PUNTOS.</summary>
    public int? ClienteId { get; set; }
    /// <summary>Descuento en % (para NUEVO/CUMPLE/REFERIDO). Se ignora en PUNTOS (se calcula del saldo de puntos).</summary>
    [Range(0, 100)] public decimal? DescuentoPct { get; set; }
    /// <summary>Puntos a convertir en descuento (solo PUNTOS). Se descuentan del saldo del cliente.</summary>
    [Range(1, 1000000)] public int? PuntosACanjear { get; set; }
    /// <summary>Días de vigencia del código desde hoy. Por defecto según el origen.</summary>
    [Range(1, 365)] public int? DiasVigencia { get; set; }
}

/// <summary>Respuesta del generador: el código creado + un mensaje listo para WhatsApp.</summary>
public class CodigoGeneradoDto
{
    public PromocionDto Promocion { get; set; } = new();
    public string MensajeWhatsapp { get; set; } = "";
    public string? Celular { get; set; }
}

public record CambiarEstadoPromocionRequest(bool Activa);

public class SiguienteAreaRequest
{
    [StringLength(120)] public string? RecibidoPor { get; set; }
}

public class PromocionValidaDto
{
    public int Id { get; set; }
    public string Descripcion { get; set; } = "";
    public decimal? DescuentoPct { get; set; }
    public decimal? DescuentoMonto { get; set; }
    public int? ServicioId { get; set; }
    public decimal CantidadMinima { get; set; }
}

// ---------- Catalogos ----------
public record ServicioDto(int Id, string Nombre, decimal Precio, string Unidad, int? CategoriaId);

/// <summary>Alta rápida de un servicio desde el registro de pedido (accesible al módulo PEDIDOS).</summary>
public record ServicioRapidoRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Nombre,
    [Range(0.01, 10000)] decimal Precio,
    [Required, StringLength(20, MinimumLength = 1)] string Unidad);
public record AreaLavadoDto(int Id, string Nombre, int Orden, int TiempoEstMinutos);

// ---------- Caja ----------
public record TipoGastoDto(int Id, string Nombre);

public class RegistrarGastoRequest
{
    [Range(0.01, 100000)] public decimal Monto { get; set; }
    [Required] public string MetodoPago { get; set; } = "EFECTIVO";
    public int? TipoGastoId { get; set; }
    [StringLength(300)] public string? Descripcion { get; set; }
}

public class MovimientoCajaDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Tipo { get; set; } = "";
    public string MetodoPago { get; set; } = "";
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
    public int? PedidoId { get; set; }
    public int? PedidoNumero { get; set; }
    public string? ClienteNombre { get; set; }
    public string? UsuarioNombre { get; set; }
    public int? TipoGastoId { get; set; }
    public string? TipoGastoNombre { get; set; }
}

/// <summary>Un cobro concreto del pedido (adelanto, saldo o pago total), con su metodo.</summary>
public class PagoPedidoDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string MetodoPago { get; set; } = "";
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
    public string? UsuarioNombre { get; set; }
}

public class GuardarCuadreRequest
{
    [Required] public DateTime Fecha { get; set; }
    /// <summary>Usuario cuyo turno se esta cuadrando. Un usuario no ADMIN solo puede cuadrarse a si mismo.</summary>
    public int? UsuarioId { get; set; }
    [Range(0, 1000000)] public decimal CajaInicial { get; set; }
    public decimal PedidosPagadosEfect { get; set; }
    public decimal Gastos { get; set; }
    [Range(0, 1000000)] public decimal TotalContado { get; set; }
    public decimal Diferencia { get; set; }
    public decimal CajaFinal { get; set; }
    /// <summary>Efectivo entregado/retirado al cierre. CajaFinal = TotalContado - Corte.</summary>
    [Range(0, 1000000)] public decimal Corte { get; set; }
    /// <summary>Pagos por transferencia móvil (Yape + Plin + Transferencia).</summary>
    public decimal IngresosDigital { get; set; }
    /// <summary>Pagos por POS / tarjeta.</summary>
    public decimal IngresosTarjeta { get; set; }
    [StringLength(300)] public string? Nota { get; set; }
    [StringLength(400)] public string? Observaciones { get; set; }
    /// <summary>Desglose del conteo billete por billete, como JSON {"100":2,...}. Opcional.</summary>
    [StringLength(500)] public string? DetalleConteo { get; set; }
}

public class CuadreCajaDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public int UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public decimal CajaInicial { get; set; }
    public decimal PedidosPagadosEfect { get; set; }
    public decimal Gastos { get; set; }
    public decimal TotalContado { get; set; }
    public decimal Diferencia { get; set; }
    public decimal CajaFinal { get; set; }
    public decimal Corte { get; set; }
    public decimal IngresosDigital { get; set; }
    public decimal IngresosTarjeta { get; set; }
    public string? Nota { get; set; }
    public string? Observaciones { get; set; }
    public string? DetalleConteo { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public record UsuarioDelDiaDto(int Id, string NombreCompleto, string RolNombre, int Movimientos, bool TieneCuadre);

// ---------- Reporte de Cuadres Diarios (pantalla dedicada) ----------
public record CuadreDiarioFilaDto(
    int Id,
    int UsuarioId,
    string UsuarioNombre,
    decimal CajaInicial,
    decimal IngresosEfectivo,
    decimal Egresos,
    decimal MontoEnCaja,
    decimal Corte,
    decimal CajaFinal,
    string Estado,          // CUADRA | SOBRA | FALTA
    decimal MargenError,
    string? Nota,
    decimal IngresosDigital,
    decimal IngresosTarjeta);

/// <summary>Operaciones de un método de pago en un día: cuántas y por cuánto.</summary>
public record FormaPagoDiaDto(string Metodo, int Cantidad, decimal Monto);

public record CuadreDiarioDiaDto(
    DateOnly Fecha,
    List<CuadreDiarioFilaDto> Cuadres,
    bool SinInformacion,
    decimal NoCuadradoIngresos,   // movimientos de un día sin cuadre guardado
    decimal NoCuadradoEgresos,
    List<FormaPagoDiaDto> FormasPago);

public record CuadresDiariosReporteDto(int Anio, int Mes, List<CuadreDiarioDiaDto> Dias);

// ---------- Facturación Electrónica ----------
public class ConfiguracionFacturacionDto
{
    [StringLength(150)] public string? RazonSocial { get; set; }
    [StringLength(11, MinimumLength = 11)] public string? RucEmisor { get; set; }
    public string Ambiente { get; set; } = "BETA"; // BETA | PRODUCCION
    [StringLength(50)] public string? SolUsuario { get; set; }
    /// <summary>Solo se envía al guardar una clave nueva; nunca se devuelve la clave real.</summary>
    [StringLength(200)] public string? SolClaveNueva { get; set; }
    /// <summary>Certificado .pfx en base64; solo se envía al subir uno nuevo.</summary>
    [StringLength(200000)] public string? CertificadoPfxBase64 { get; set; }
    [StringLength(200)] public string? CertificadoPasswordNueva { get; set; }
    [StringLength(4, MinimumLength = 4)] public string SerieBoleta { get; set; } = "B001";
    [StringLength(4, MinimumLength = 4)] public string SerieFactura { get; set; } = "F001";
    public bool Activo { get; set; }
    /// <summary>Régimen NRUS/RUS: solo boletas (sin Factura ni Liquidación).</summary>
    public bool SoloBoletas { get; set; }
    public bool TieneCertificado { get; set; }
    public bool TieneCredencialesSol { get; set; }
    public string Proveedor { get; set; } = "SUNAT_DIRECTO";
    [StringLength(100)] public string? ApiSunatPersonaId { get; set; }
    [StringLength(1000)] public string? ApiSunatTokenNuevo { get; set; }
    public bool TieneCredencialesApiSunat { get; set; }
    [StringLength(250)] public string? DireccionFiscal { get; set; }
    [StringLength(6, MinimumLength = 6)] public string? Ubigeo { get; set; }
    [StringLength(4, MinimumLength = 4)] public string CodigoEstablecimiento { get; set; } = "0000";
    [EmailAddress, StringLength(150)] public string? EmailEmisor { get; set; }
    public int CorrelativoBoleta { get; set; }
    public int CorrelativoFactura { get; set; }
    public bool RequiereCertificadoLocal { get; set; }
}

public record EmitirComprobanteRequest([Required] string Tipo); // BOLETA | FACTURA
public record AnularComprobanteRequest(
    [Required, StringLength(100, MinimumLength = 3)] string Motivo);

/// <summary>Solicitud de Nota de Crédito sobre un comprobante aceptado. MotivoCodigo es del
/// catálogo 09 de SUNAT (01 anulación de la operación, 02 anulación por error en el RUC,
/// 03 corrección por error en la descripción, 06 devolución total…).</summary>
public record NotaCreditoRequest(
    [Required, StringLength(2, MinimumLength = 2)] string MotivoCodigo,
    [Required, StringLength(250, MinimumLength = 3)] string Motivo);

public record NotaDebitoRequest(
    [Required, StringLength(2, MinimumLength = 2)] string MotivoCodigo,
    [Required, StringLength(250, MinimumLength = 3)] string Motivo);

/// <summary>Datos de traslado para emitir una Guía de Remisión Remitente (GRE) desde un comprobante.</summary>
public record GuiaRemisionRequest(
    [Required] string MotivoCodigo,                 // catálogo 20
    string? MotivoDescripcion,
    [Range(0.001, 999999)] decimal PesoBrutoTotal,
    string? UnidadPeso,                             // KGM por defecto
    int? NumeroBultos,
    [Required] DateTime FechaInicioTraslado,
    [Required] string ModalidadTransporte,          // 01 público / 02 privado
    [Required] string PartidaUbigeo,
    [Required] string PartidaDireccion,
    [Required] string LlegadaUbigeo,
    [Required] string LlegadaDireccion,
    // Transporte público (01)
    string? TransportistaNumDoc,
    string? TransportistaRazonSocial,
    // Transporte privado (02)
    string? VehiculoPlaca,
    string? ConductorTipoDoc,
    string? ConductorNumDoc,
    string? ConductorNombres,
    string? ConductorLicencia);

public record ResultadoConexionFacturacionDto(
    bool Exitoso, string Mensaje, bool? Produccion, string? UltimoNumero, string? NumeroSugerido);

public class ComprobanteDto
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int? PedidoNumero { get; set; }
    public string Tipo { get; set; } = "";
    public string Serie { get; set; } = "";
    public int Correlativo { get; set; }
    public string NumeroCompleto => $"{Serie}-{Correlativo}";
    public string ClienteNombre { get; set; } = "";
    public string ClienteTipoDoc { get; set; } = "";
    public string? ClienteNumDoc { get; set; }
    public decimal OpGravada { get; set; }
    public decimal Igv { get; set; }
    public decimal Total { get; set; }
    public string Estado { get; set; } = "";
    public string? DescripcionRespuestaSunat { get; set; }
    public DateTime FechaEmision { get; set; }
    public string Proveedor { get; set; } = "";
    public string Ambiente { get; set; } = "";
    public string? ExternalId { get; set; }
    public string? CodigoRespuestaSunat { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public DateTime? FechaRespuesta { get; set; }
    public bool EsSimulado { get; set; }
    public bool TieneXml { get; set; }
    public bool TieneCdr { get; set; }
    public string? EstadoAnulacion { get; set; }
    public string? MotivoAnulacion { get; set; }
    public DateTime? FechaAnulacion { get; set; }
    // Nota de Crédito: documento que corrige/anula y motivo (catálogo 09).
    public string? DocRefSerieNumero { get; set; }
    public string? MotivoNotaCodigo { get; set; }
    public string? MotivoNotaDescripcion { get; set; }
    // Guía de Remisión: datos de traslado (null en los demás tipos).
    public GuiaRemisionDto? Guia { get; set; }
    public List<ComprobanteDetalleDto> Detalles { get; set; } = [];
    public List<ComprobanteIntentoDto> Intentos { get; set; } = [];
}

public record GuiaRemisionDto(
    string MotivoTrasladoCodigo, string? MotivoTrasladoDescripcion, decimal PesoBrutoTotal, string UnidadPeso,
    int? NumeroBultos, DateTime FechaInicioTraslado, string ModalidadTransporte,
    string PartidaUbigeo, string PartidaDireccion, string LlegadaUbigeo, string LlegadaDireccion,
    string? TransportistaNumDoc, string? TransportistaRazonSocial, string? VehiculoPlaca,
    string? ConductorTipoDoc, string? ConductorNumDoc, string? ConductorNombres, string? ConductorLicencia);

public record ComprobanteDetalleDto(
    int NumeroLinea, string Descripcion, string UnidadMedida, decimal Cantidad,
    decimal PrecioUnitarioIgv, decimal ValorVenta, decimal Igv, decimal Total);

public record ComprobanteIntentoDto(
    long Id, string Accion, string Estado, string? Codigo, string? Descripcion,
    DateTime Fecha, int? UsuarioId);

/// <summary>KPI mensual de comprobantes: boletas y facturas emitidas por mes (conteo y monto).</summary>
public class KpiComprobantesMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public int BoletasCantidad { get; set; }
    public decimal BoletasMonto { get; set; }
    public int FacturasCantidad { get; set; }
    public decimal FacturasMonto { get; set; }
    public int TotalCantidad => BoletasCantidad + FacturasCantidad;
    public decimal TotalMonto => BoletasMonto + FacturasMonto;
}

/// <summary>Info del respaldo local de comprobantes: la carpeta donde se guardan XML+CDR+PDF.</summary>
public record RespaldoInfoDto(string Carpeta);

/// <summary>Resultado del backfill de respaldo: cuántos comprobantes se respaldaron y en qué carpeta.</summary>
public record RespaldoResultadoDto(int Respaldados, string Carpeta);

// ---------- Panel de propietario de plataforma (alta de negocios/tenants) ----------

public record NegocioResumenDto(
    int Id, string Nombre, string Slug, bool Activo, DateTime FechaCreacion,
    int CantidadSedes, int CantidadUsuarios,
    string PlanSuscripcion, string EstadoSuscripcion, decimal MontoMensual,
    DateOnly? ProximoPago, DateTime? UltimoAcceso, int PedidosMes);

/// <summary>KPIs del negocio-de-negocios para el tablero del propietario.</summary>
public record PlataformaResumenDto(
    int TotalEmpresas, int EmpresasActivas, int EmpresasSuspendidas, int EmpresasNuevasMes,
    decimal IngresoMensualRecurrente, int PedidosMesTotal, int EmpresasPorVencer, int EmpresasVencidas,
    decimal RecaudadoMes);

public record SedeResumenDto(int Id, string Nombre, string? Direccion, bool Activo);

public record UsuarioResumenDto(int Id, string Usuario, string NombreCompleto, string RolCodigo, bool Activo, DateTime? UltimoAcceso);

/// <summary>Ficha completa de una empresa para el panel del propietario.</summary>
public class NegocioDetalleDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? RucEmpresa { get; set; }
    public string? TitularNombre { get; set; }
    public string? TitularEmail { get; set; }
    public string? TitularCelular { get; set; }
    public string? NotasInternas { get; set; }
    public bool Activo { get; set; }
    public DateTime FechaCreacion { get; set; }
    public string PlanSuscripcion { get; set; } = "";
    public string EstadoSuscripcion { get; set; } = "";
    public decimal MontoMensual { get; set; }
    public DateOnly? ProximoPago { get; set; }
    public int PedidosMes { get; set; }
    public DateTime? UltimoAcceso { get; set; }
    public string? AdminUsuario { get; set; }
    public List<SedeResumenDto> Sedes { get; set; } = new();
    public List<UsuarioResumenDto> Usuarios { get; set; } = new();
}

public class CrearNegocioRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Required, StringLength(50, MinimumLength = 2)] public string Slug { get; set; } = "";
    public string? RucEmpresa { get; set; }
    public string? TitularNombre { get; set; }
    public string? TitularEmail { get; set; }
    public string? TitularCelular { get; set; }
    [Required, StringLength(80, MinimumLength = 2)] public string SedeNombre { get; set; } = "";
    [Required, StringLength(50, MinimumLength = 3)] public string AdminUsuario { get; set; } = "";
    [Required, StringLength(120, MinimumLength = 2)] public string AdminNombreCompleto { get; set; } = "";
    public string? AdminEmail { get; set; }
    [Required, StringLength(100, MinimumLength = 8)] public string AdminPassword { get; set; } = "";
}

public class EditarNegocioRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    public string? RucEmpresa { get; set; }
    public string? TitularNombre { get; set; }
    public string? TitularEmail { get; set; }
    [StringLength(20)] public string? TitularCelular { get; set; }
    [StringLength(500)] public string? NotasInternas { get; set; }
}

public class CambiarSuscripcionRequest
{
    [Required] public string PlanSuscripcion { get; set; } = "BASICO";
    [Required] public string EstadoSuscripcion { get; set; } = "ACTIVA";
    [Range(0, 100000)] public decimal MontoMensual { get; set; }
    public DateOnly? ProximoPago { get; set; }
}

public class ResetPasswordAdminRequest
{
    [Required, StringLength(100, MinimumLength = 8)] public string NuevaPassword { get; set; } = "";
}

public record CambiarEstadoNegocioRequest(bool Activo);

// ---------- Cobranza del propietario (pagos de suscripción + configuración de cobro) ----------

public record PagoSuscripcionDto(
    int Id, DateOnly Fecha, decimal Monto, string Metodo,
    DateOnly? PeriodoDesde, DateOnly? PeriodoHasta, string? Nota, DateTime FechaCreacion);

public class RegistrarPagoSuscripcionRequest
{
    [Range(0.01, 1000000)] public decimal Monto { get; set; }
    [Required] public string Metodo { get; set; } = "YAPE";
    /// <summary>Meses que cubre el pago (avanza el próximo pago esa cantidad de meses).</summary>
    [Range(1, 24)] public int Meses { get; set; } = 1;
    [StringLength(300)] public string? Nota { get; set; }
}

public class ConfiguracionPlataformaDto
{
    [Required, StringLength(100, MinimumLength = 2)] public string NombrePlataforma { get; set; } = "LunaLav";
    [StringLength(100)] public string? YapeNombre { get; set; }
    [StringLength(20)] public string? YapeNumero { get; set; }
    [StringLength(100)] public string? ContactoSoporte { get; set; }
    [Range(0, 60)] public int DiasAvisoCobro { get; set; } = 3;
}

/// <summary>Aviso de suscripción que ve la propia empresa en su dashboard.</summary>
public record MiSuscripcionDto(bool Mostrar, string Tipo, string Mensaje,
    DateOnly? ProximoPago, int? DiasParaVencer, string EstadoSuscripcion);

// ---------- Pagos online (Izipay) ----------
public class ConfiguracionPagosDto
{
    public string Proveedor { get; set; } = "IZIPAY";
    [StringLength(50)] public string? CodigoComercio { get; set; }
    [StringLength(5000)] public string? PublicKey { get; set; }
    /// <summary>Solo se aceptan al reemplazar credenciales; las claves reales nunca se devuelven.</summary>
    [StringLength(500)] public string? ApiKeyNueva { get; set; }
    [StringLength(500)] public string? HashKeyNueva { get; set; }
    public bool Activo { get; set; }
    public bool TieneApiKey { get; set; }
    public bool TieneHashKey { get; set; }
    public bool IntegracionDisponible { get; set; }
}

public class ConvertirDeliveryRequest
{
    [Required, StringLength(250)] public string DireccionEntrega { get; set; } = "";
    [Required, StringLength(100)] public string DistritoEntrega { get; set; } = "";
    [StringLength(250)] public string? ReferenciaEntrega { get; set; }
    [Range(-90d, 90d)] public decimal? LatitudEntrega { get; set; }
    [Range(-180d, 180d)] public decimal? LongitudEntrega { get; set; }
    // Tarifa de domicilio a cobrar por convertir a Delivery. Si no llega, se usa la tarifa
    // configurada por el negocio. Se agrega como ítem "Tarifa de domicilio" y sube el total.
    [Range(0, 10000)] public decimal? CostoDelivery { get; set; }
}

public record LinkSeguimientoDto(Guid Token);

public record PasoSeguimientoDto(string Codigo, string Nombre, bool Alcanzado, bool Actual);

public class SeguimientoPedidoDto
{
    public string NombreNegocio { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string ColorPrimario { get; set; } = "#0b57d0";
    public string? TelefonoNegocio { get; set; }
    public string? DireccionNegocio { get; set; }
    public int NumeroPedido { get; set; }
    public string Modalidad { get; set; } = "";
    public string? DireccionEntrega { get; set; }
    public string? DistritoEntrega { get; set; }
    public string? ReferenciaEntrega { get; set; }
    public decimal? LatitudEntrega { get; set; }
    public decimal? LongitudEntrega { get; set; }
    public string ResumenEstado { get; set; } = "";
    public DateTime? FechaCompromiso { get; set; }
    public string EtiquetaFechaCompromiso { get; set; } = "";
    public List<PasoSeguimientoDto> Pasos { get; set; } = new();
    public List<SeguimientoPedidoItemDto> Items { get; set; } = new();
    public bool Anulado { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal Saldo { get; set; }
    public bool RequierePago { get; set; }
    public string ProveedorPagos { get; set; } = "IZIPAY";
    public string? MensajePagoOnline { get; set; }
    public string? MotorizadoNombre { get; set; }
    public string? MotorizadoCelular { get; set; }
    public bool PuedeReprogramar { get; set; }

    // ---- Seguimiento en vivo del reparto (tipo Uber) ----
    /// <summary>SIN_RUTA · EN_RUTA · CERCA · LLEGO · ENTREGADO. Guia el mensaje y el mapa
    /// del cliente; el frontend dispara la notificacion del navegador al cambiar.</summary>
    public string EstadoRuta { get; set; } = "SIN_RUTA";
    public DateTime? RutaIniciadaEn { get; set; }
    public decimal? MotorizadoLat { get; set; }
    public decimal? MotorizadoLng { get; set; }
    public DateTime? MotorizadoUbicadoEn { get; set; }
    /// <summary>Distancia en metros del repartidor al destino (cuando hay GPS reciente).</summary>
    public int? DistanciaMetros { get; set; }
    /// <summary>Minutos estimados de llegada (heuristica a 18 km/h urbano).</summary>
    public int? EtaMinutos { get; set; }

    /// <summary>Fotos de evidencia visibles para el cliente (recepcion/entrega).</summary>
    public List<SeguimientoFotoDto> Fotos { get; set; } = new();
}

public record SeguimientoPedidoItemDto(string Nombre, decimal Cantidad);

/// <summary>Foto de evidencia tal como la ve el cliente en el seguimiento publico. La URL se
/// arma en el frontend con el token del enlace.</summary>
public record SeguimientoFotoDto(int Id, string Momento, DateTime Fecha);

/// <summary>Foto de evidencia para el panel del personal.</summary>
public record FotoPedidoDto(int Id, string Momento, DateTime Fecha, int TamanoBytes);

/// <summary>Vista que ve el repartidor al abrir su link publico de reparto.</summary>
public class RepartidorPedidoDto
{
    public string NombreNegocio { get; set; } = "";
    public string ColorPrimario { get; set; } = "#0b57d0";
    public int NumeroPedido { get; set; }
    public string ClienteNombre { get; set; } = "";
    public string? ClienteCelular { get; set; }
    public string? DireccionEntrega { get; set; }
    public string? DistritoEntrega { get; set; }
    public string? ReferenciaEntrega { get; set; }
    public decimal? LatitudEntrega { get; set; }
    public decimal? LongitudEntrega { get; set; }
    public decimal Saldo { get; set; }
    public bool Anulado { get; set; }
    public bool Entregado { get; set; }
    public string EstadoRuta { get; set; } = "SIN_RUTA";
    public DateTime? RutaIniciadaEn { get; set; }
    // Cobro por Yape/Plin del negocio: el repartidor muestra el QR al cliente en la puerta.
    public string? YapeNumero { get; set; }
    public string? YapeTitular { get; set; }
    public string? YapeQrUrl { get; set; }
}

public class UbicacionRepartidorRequest
{
    [Range(-90, 90)] public decimal Lat { get; set; }
    [Range(-180, 180)] public decimal Lng { get; set; }
}

public record LinkRepartidorDto(Guid Token);

public class UbicacionRepartidorResultDto
{
    public string EstadoRuta { get; set; } = "SIN_RUTA";
    public int? DistanciaMetros { get; set; }
    public int? EtaMinutos { get; set; }
}

public record ReprogramarPedidoPublicoRequest([Required] DateTime NuevaFecha);

public class CobrarSolicitudPagoResultDto
{
    public bool Exito { get; set; }
    public string? Mensaje { get; set; }
    public decimal SaldoPendiente { get; set; }
}

// ---------- Tendencias en barras (indicadores por ventana) ----------
/// <summary>Un punto de una serie de barras: etiqueta corta del eje X + valor.</summary>
public record TendenciaPuntoDto(string Etiqueta, int Valor);

/// <summary>Pedidos: dos series diarias para la ventana de Pedidos.</summary>
public record TendenciaPedidosDto(List<TendenciaPuntoDto> Recibidos, List<TendenciaPuntoDto> Entregados);
