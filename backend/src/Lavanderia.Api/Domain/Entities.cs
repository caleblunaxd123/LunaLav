namespace Lavanderia.Api.Domain;

public class CuadreCaja
{
    public int Id { get; set; }
    public int SedeId { get; set; }
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
    /// <summary>Desglose del conteo billete por billete, como JSON {"100":2,"50":1,...}. Null si se cerró por total directo.</summary>
    public string? DetalleConteo { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public class ConfiguracionNegocio
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string NombreNegocio { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string ColorPrimario { get; set; } = "#0b57d0";
    public string ColorSecundario { get; set; } = "#29b6f6";
    public string ColorAcento { get; set; } = "#f5a623";
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Ruc { get; set; }
    public string? HorarioAtencion { get; set; }
    public decimal Igv { get; set; } = 18.00m;
    public decimal MetaMensual { get; set; }
    public decimal SolesPorPunto { get; set; } = 1m;
    public int AnchoTicketMm { get; set; } = 80;
    public string? MensajePieTicket { get; set; }
    public string? CondicionesServicio { get; set; }
    public string? NotasProduccion { get; set; }
    public decimal CostoDelivery { get; set; }
    public decimal ValorPuntoCanje { get; set; }   // soles que vale 1 punto al canjear (0 = canje off)
    public decimal MaxDescuentoPct { get; set; }    // tope de descuento manual (0 = sin tope)
    // Cobro por Yape/Plin del negocio (para que el cliente le pague a la lavandería).
    public string? YapeNumero { get; set; }
    public string? YapeTitular { get; set; }
    public string? YapeQrUrl { get; set; }
}

public class Rol
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    /// <summary>null = rol de sistema (global, p. ej. ADMIN); si tiene valor, es un rol propio del negocio.</summary>
    public int? NegocioId { get; set; }
    /// <summary>Roles de sistema (ADMIN, PROPIETARIO): no se pueden editar ni eliminar.</summary>
    public bool EsSistema { get; set; }
    /// <summary>Solo informativo (listado de roles): true si algún usuario lo tiene asignado.</summary>
    public bool EnUso { get; set; }
}

public class RolPermiso
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public int RolId { get; set; }
    public string RolCodigo { get; set; } = "";
    public string RolNombre { get; set; } = "";
    public string Modulo { get; set; } = "";
    public bool PuedeAcceder { get; set; }
}

public class Usuario
{
    public int Id { get; set; }
    public string UsuarioLogin { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public int RolId { get; set; }
    public string RolCodigo { get; set; } = "";
    public bool Activo { get; set; } = true;
    public int NegocioId { get; set; }
    public int? SedeId { get; set; }
    public string? SedeNombre { get; set; }
    public DateTime? UltimoAcceso { get; set; }
}

public class Negocio
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? RucEmpresa { get; set; }
    public string? TitularNombre { get; set; }
    public string? TitularEmail { get; set; }
    public string? TitularCelular { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
    // Suscripción (panel del propietario del SaaS)
    public string PlanSuscripcion { get; set; } = "BASICO";
    public string EstadoSuscripcion { get; set; } = "ACTIVA"; // PRUEBA / ACTIVA / VENCIDA / SUSPENDIDA
    public decimal MontoMensual { get; set; }
    public DateOnly? ProximoPago { get; set; }
    public string? NotasInternas { get; set; }
}

public class Sede
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
}

/// <summary>Pago mensual que una empresa (tenant) le hace al dueño del SaaS por la suscripción.</summary>
public class PagoSuscripcion
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public DateOnly Fecha { get; set; }
    public decimal Monto { get; set; }
    public string Metodo { get; set; } = "YAPE"; // YAPE / PLIN / TRANSFERENCIA / EFECTIVO / OTRO
    public DateOnly? PeriodoDesde { get; set; }
    public DateOnly? PeriodoHasta { get; set; }
    public string? Nota { get; set; }
    public int? RegistradoPorUsuarioId { get; set; }
    public DateTime FechaCreacion { get; set; }
}

/// <summary>Configuración del dueño del SaaS (fila única): datos de cobro para recordatorios y recibos.</summary>
public class ConfiguracionPlataforma
{
    public string NombrePlataforma { get; set; } = "LunaLav";
    public string? YapeNombre { get; set; }
    public string? YapeNumero { get; set; }
    public string? ContactoSoporte { get; set; }
    public int DiasAvisoCobro { get; set; } = 3;
}

public class Cliente
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Celular { get; set; }
    public string? Dni { get; set; }
    public string? DocumentoFiscal { get; set; }
    public string? Direccion { get; set; }
    public int Puntos { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
    public DateOnly? FechaNacimiento { get; set; }
}

public class MovimientoPuntos
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public int ClienteId { get; set; }
    public DateTime Fecha { get; set; }
    public string Motivo { get; set; } = "";
    public int Puntos { get; set; }
    public string Tipo { get; set; } = "SUMA"; // SUMA | RESTA
    public int? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public int? PedidoId { get; set; }
}

public class Categoria
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activa { get; set; } = true;
    /// <summary>No persistida: la calcula la query de listado (tiene servicios asociados).</summary>
    public bool EnUso { get; set; }
}

public class Servicio
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public decimal Precio { get; set; }
    /// <summary>Costo estimado del servicio (para calcular margen/rentabilidad). 0 = sin definir.</summary>
    public decimal Costo { get; set; }
    public string Unidad { get; set; } = "";
    public int? CategoriaId { get; set; }
    public string? CategoriaNombre { get; set; }
    public bool Activo { get; set; } = true;
    public bool EsCargoDelivery { get; set; }
    /// <summary>No persistida: la calcula la query de listado (usado en algún pedido).</summary>
    public bool EnUso { get; set; }
}

public class AreaLavado
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public string Nombre { get; set; } = "";
    public int Orden { get; set; }
    public int TiempoEstMinutos { get; set; }
    public bool Activa { get; set; } = true;
    /// <summary>No persistida: la calcula la query de listado (algún pedido pasó por el área).</summary>
    public bool EnUso { get; set; }
}

public class Promocion
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Tipo { get; set; } = "";  // VOLUMEN | FRECUENCIA | FIJA
    public string Descripcion { get; set; } = "";
    public decimal? DescuentoPct { get; set; }
    public decimal? DescuentoMonto { get; set; }
    public int? ServicioId { get; set; }
    public string? ServicioNombre { get; set; }
    public decimal CantidadMinima { get; set; } = 1;
    public DateOnly? FechaInicio { get; set; }
    public DateOnly? FechaFin { get; set; }
    public bool Activa { get; set; } = true;
    public string? Codigo { get; set; }
    // Códigos generados (ver 045). Null en las promos de marketing clásicas.
    public int? ClienteId { get; set; }
    public string? ClienteNombre { get; set; }
    public string? Origen { get; set; }      // NUEVO | CUMPLE | REFERIDO | PUNTOS | MANUAL
    public int? MaxUsos { get; set; }         // null = ilimitado
    public int Usos { get; set; }
}

public class Pedido
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public int Numero { get; set; }
    public int ClienteId { get; set; }
    public string? ClienteNombre { get; set; }
    public string? ClienteCelular { get; set; }
    public string? ClienteDni { get; set; }
    public int ClientePuntos { get; set; }
    public int UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public DateTime FechaIngreso { get; set; }
    public DateTime? FechaEntregaEst { get; set; }
    public string Modalidad { get; set; } = "Tienda";
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
    public string MetodoPagoInicial { get; set; } = "EFECTIVO";
    public string EstadoPago { get; set; } = "PENDIENTE";
    public string EstadoProceso { get; set; } = "PENDIENTE";
    public int? AreaActualId { get; set; }
    public string? AreaActualNombre { get; set; }
    public string? Observaciones { get; set; }
    public DateTime? FechaEntregaReal { get; set; }
    public bool Anulado { get; set; }
    public string? MotivoAnulacion { get; set; }
    public string? CodigoAntiguo { get; set; }
    public int? MotorizadoId { get; set; }
    public string? MotorizadoNombre { get; set; }
    public string? MotorizadoCelular { get; set; }
    // Seguimiento en vivo del reparto (tipo Uber).
    public DateTime? RutaIniciadaEn { get; set; }
    public decimal? MotorizadoLat { get; set; }
    public decimal? MotorizadoLng { get; set; }
    public DateTime? MotorizadoUbicadoEn { get; set; }
    public Guid? TokenRuta { get; set; }
    public bool NotifRutaEnviada { get; set; }
    public bool NotifCercaEnviada { get; set; }
    public bool NotifLlegadaEnviada { get; set; }
    public List<PedidoItem> Items { get; set; } = new();
}

public class PedidoItem
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int ServicioId { get; set; }
    public string? ServicioNombre { get; set; }
    public string? ServicioUnidad { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioUnit { get; set; }
    public decimal Total { get; set; }
    public string? Descripcion { get; set; }
    /// <summary>Cantidad ya entregada al cliente (para entregas parciales). Lo pendiente = Cantidad - CantidadEntregada.</summary>
    public decimal CantidadEntregada { get; set; }
}

/// <summary>Una entrega (parcial o final) que se le hace al cliente sobre un pedido.</summary>
public class PedidoEntrega
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int SedeId { get; set; }
    public DateTime Fecha { get; set; }
    public int? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? RecibidoPor { get; set; }
    public string? Nota { get; set; }
    public bool EsFinal { get; set; }
    public decimal MontoCobrado { get; set; }
    public List<PedidoEntregaDetalle> Items { get; set; } = new();
}

public class PedidoEntregaDetalle
{
    public int Id { get; set; }
    public int EntregaId { get; set; }
    public int PedidoItemId { get; set; }
    public decimal Cantidad { get; set; }
    public string? ServicioNombre { get; set; }
    public string? ServicioUnidad { get; set; }
}

public class PedidoHistorial
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int? AreaId { get; set; }
    public string? AreaNombre { get; set; }
    public string EstadoProceso { get; set; } = "";
    public int? UsuarioId { get; set; }
    public string ActorTipo { get; set; } = "USUARIO";
    public string? ActorDescripcion { get; set; }
    public DateTime Fecha { get; set; }
    public string? Nota { get; set; }
    public bool NotificadoWsp { get; set; }
}

public class MovimientoCaja
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public DateTime Fecha { get; set; }
    public string Tipo { get; set; } = "";       // INGRESO | GASTO
    public string MetodoPago { get; set; } = ""; // EFECTIVO | YAPE | PLIN | TRANSFERENCIA | POS | TARJETA
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
    public int? PedidoId { get; set; }
    public int? PedidoNumero { get; set; }
    public string? ClienteNombre { get; set; }
    public int UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public int? TipoGastoId { get; set; }
    public string? TipoGastoNombre { get; set; }
}

public class TipoGasto
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
    /// <summary>No persistida: la calcula la query de listado (tiene movimientos de caja asociados).</summary>
    public bool EnUso { get; set; }
}

public class Insumo
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public string Nombre { get; set; } = "";
    public string UnidadMedida { get; set; } = "";
    /// <summary>Clase de inventario: EQUIPO, MATERIAL o INSUMO (consumible).</summary>
    public string Clase { get; set; } = "INSUMO";
    /// <summary>Marca de favorito para priorizar insumos de uso frecuente dentro de la sede.</summary>
    public bool Favorito { get; set; }
    /// <summary>Contenido de cada unidad de stock, ej. bidón x 20 litros (opcional).</summary>
    public decimal? ContenidoValor { get; set; }
    public string? ContenidoUnidad { get; set; }
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime? UltimaCompra { get; set; }
    /// <summary>Fecha en que el insumo ingresó/se registró en el inventario (opcional, editable).</summary>
    public DateOnly? FechaIngreso { get; set; }
    /// <summary>Fecha de vencimiento/caducidad del insumo (opcional). Para avisar cuando está por vencer.</summary>
    public DateOnly? FechaVencimiento { get; set; }
    /// <summary>No persistida: la calcula la query de listado (tiene movimientos registrados).</summary>
    public bool EnUso { get; set; }
}

public class MovimientoInsumo
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public int InsumoId { get; set; }
    public string? InsumoNombre { get; set; }
    public string Tipo { get; set; } = "";  // COMPRA | CONSUMO | AJUSTE
    public decimal Cantidad { get; set; }
    public decimal? CostoTotal { get; set; }
    public DateTime Fecha { get; set; }
    public int UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? Descripcion { get; set; }
    public int? MovimientoCajaId { get; set; }
}

public class Motorizado
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Celular { get; set; }
    public bool Activo { get; set; } = true;
}

public class RolPersonal
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
}

public class Empleado
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Dni { get; set; }
    public string? Celular { get; set; }
    public string? Cargo { get; set; }
    public DateOnly? FechaIngreso { get; set; }
    public bool Activo { get; set; } = true;
}

public class PlantillaWhatsapp
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Evento { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public bool Activa { get; set; } = true;
}

public class PedidoAbandonado
{
    public int PedidoId { get; set; }
    public int Numero { get; set; }
    public string ClienteNombre { get; set; } = "";
    public string? ClienteCelular { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public DateTime FechaListo { get; set; }
    public int DiasEsperando { get; set; }
}

public class ConfiguracionFacturacion
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string? RazonSocial { get; set; }
    public string? RucEmisor { get; set; }
    public string Ambiente { get; set; } = "BETA"; // BETA | PRODUCCION
    public string? SolUsuario { get; set; }
    public string? SolClaveCifrada { get; set; }
    public byte[]? CertificadoPfx { get; set; }
    public string? CertificadoPasswordCifrada { get; set; }
    public string SerieBoleta { get; set; } = "B001";
    public string SerieFactura { get; set; } = "F001";
    public int CorrelativoBoleta { get; set; }
    public int CorrelativoFactura { get; set; }
    // Series y correlativos propios de las Notas de Crédito (SUNAT: la serie inicia con F o B).
    public string SerieNotaCreditoFactura { get; set; } = "FC01";
    public string SerieNotaCreditoBoleta { get; set; } = "BC01";
    public int CorrelativoNotaCreditoFactura { get; set; }
    public int CorrelativoNotaCreditoBoleta { get; set; }
    public string SerieNotaDebitoFactura { get; set; } = "FD01";
    public string SerieNotaDebitoBoleta { get; set; } = "BD01";
    public int CorrelativoNotaDebitoFactura { get; set; }
    public int CorrelativoNotaDebitoBoleta { get; set; }
    public string SerieGuiaRemision { get; set; } = "T001";
    public int CorrelativoGuiaRemision { get; set; }
    public bool Activo { get; set; }
    /// <summary>Régimen NRUS/RUS: solo puede emitir Boletas (no Factura ni Liquidación).</summary>
    public bool SoloBoletas { get; set; }
    public string Proveedor { get; set; } = "SUNAT_DIRECTO";
    public string? ApiSunatPersonaId { get; set; }
    public string? ApiSunatTokenCifrado { get; set; }
    public string? DireccionFiscal { get; set; }
    public string? Ubigeo { get; set; }
    public string CodigoEstablecimiento { get; set; } = "0000";
    public string? EmailEmisor { get; set; }
}

public class ConfiguracionPagos
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Proveedor { get; set; } = "IZIPAY";
    public string? CodigoComercio { get; set; }
    public string? PublicKey { get; set; }
    public string? ApiKeyCifrada { get; set; }
    public string? HashKeyCifrada { get; set; }
    public bool Activo { get; set; }
}

public class SolicitudPago
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public int SedeId { get; set; }
    public int PedidoId { get; set; }
    public Guid Token { get; set; }
    public decimal Monto { get; set; }
    public string Estado { get; set; } = "PENDIENTE"; // PENDIENTE | PAGADO | EXPIRADO | CANCELADO
    public string? ProveedorOperacionId { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaExpiracion { get; set; }
    public DateTime? FechaPago { get; set; }
}

public class ComprobanteElectronico
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public int SedeId { get; set; }
    public int PedidoId { get; set; }
    public int? PedidoNumero { get; set; }
    public string Tipo { get; set; } = "";          // BOLETA | FACTURA
    public string Serie { get; set; } = "";
    public int Correlativo { get; set; }
    public string ClienteNombre { get; set; } = "";
    public string ClienteTipoDoc { get; set; } = ""; // DNI | RUC | SIN_DOC
    public string? ClienteNumDoc { get; set; }
    public decimal OpGravada { get; set; }
    public decimal Igv { get; set; }
    public decimal Total { get; set; }
    public string Estado { get; set; } = "PENDIENTE"; // PENDIENTE|ACEPTADO|RECHAZADO|ANULADO|ERROR
    public string? CodigoRespuestaSunat { get; set; }
    public string? DescripcionRespuestaSunat { get; set; }
    public byte[]? XmlFirmado { get; set; }
    public byte[]? CdrZip { get; set; }
    public string? HashCpe { get; set; }
    public DateTime FechaEmision { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public int UsuarioId { get; set; }
    public string Proveedor { get; set; } = "SUNAT_DIRECTO";
    public string Ambiente { get; set; } = "BETA";
    public string? ExternalId { get; set; }
    public string? RucEmisor { get; set; }
    public string? RazonSocialEmisor { get; set; }
    public string? DireccionFiscalEmisor { get; set; }
    public string? UbigeoEmisor { get; set; }
    public string? CodigoEstablecimientoEmisor { get; set; }
    public string Moneda { get; set; } = "PEN";
    public decimal IgvPorcentaje { get; set; } = 18m;
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Recargo { get; set; }
    public decimal Redondeo { get; set; }
    public bool EsSimulado { get; set; }
    public DateTime FechaActualizacion { get; set; }
    public DateTime? FechaRespuesta { get; set; }
    public string? EstadoAnulacion { get; set; }
    public string? MotivoAnulacion { get; set; }
    public DateTime? FechaSolicitudAnulacion { get; set; }
    public DateTime? FechaAnulacion { get; set; }
    // Nota de Crédito (Tipo = NOTA_CREDITO): referencia al comprobante que corrige/anula + motivo (catálogo 09).
    public int? ComprobanteRefId { get; set; }
    public string? DocRefTipo { get; set; }          // 01 factura / 03 boleta referenciada
    public string? DocRefSerieNumero { get; set; }   // p.ej. F001-00000123
    public string? MotivoNotaCodigo { get; set; }    // catálogo 09 (01, 02, 03, 06…)
    public string? MotivoNotaDescripcion { get; set; }
    public List<ComprobanteElectronicoDetalle> Detalles { get; set; } = [];
    public List<ComprobanteElectronicoIntento> Intentos { get; set; } = [];
    // Guía de Remisión (Tipo = GUIA_REMISION): datos de traslado.
    public GuiaRemisionDatos? Guia { get; set; }
}

/// <summary>Datos de traslado de una Guía de Remisión Remitente (GRE, SUNAT 09), 1:1 con el comprobante.</summary>
public class GuiaRemisionDatos
{
    public int ComprobanteId { get; set; }
    public string MotivoTrasladoCodigo { get; set; } = "01";   // catálogo 20
    public string? MotivoTrasladoDescripcion { get; set; }
    public decimal PesoBrutoTotal { get; set; }
    public string UnidadPeso { get; set; } = "KGM";
    public int? NumeroBultos { get; set; }
    public DateTime FechaInicioTraslado { get; set; }
    public string ModalidadTransporte { get; set; } = "02";    // 01 público / 02 privado
    public string PartidaUbigeo { get; set; } = "";
    public string PartidaDireccion { get; set; } = "";
    public string LlegadaUbigeo { get; set; } = "";
    public string LlegadaDireccion { get; set; } = "";
    // Transporte público (01)
    public string? TransportistaNumDoc { get; set; }
    public string? TransportistaRazonSocial { get; set; }
    // Transporte privado (02)
    public string? VehiculoPlaca { get; set; }
    public string? ConductorTipoDoc { get; set; }
    public string? ConductorNumDoc { get; set; }
    public string? ConductorNombres { get; set; }
    public string? ConductorLicencia { get; set; }
}

public class ComprobanteElectronicoDetalle
{
    public int Id { get; set; }
    public int ComprobanteId { get; set; }
    public int NumeroLinea { get; set; }
    public int? ServicioId { get; set; }
    public string Descripcion { get; set; } = "";
    public string UnidadMedida { get; set; } = "ZZ";
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitarioIgv { get; set; }
    public decimal ValorVenta { get; set; }
    public decimal Igv { get; set; }
    public decimal Total { get; set; }
}

public class ComprobanteElectronicoIntento
{
    public long Id { get; set; }
    public int ComprobanteId { get; set; }
    public string Accion { get; set; } = "";
    public string Estado { get; set; } = "";
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public DateTime Fecha { get; set; }
    public int? UsuarioId { get; set; }
}
