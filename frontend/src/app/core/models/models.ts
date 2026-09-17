export interface UsuarioSesion {
  id: number;
  usuario: string;
  nombreCompleto: string;
  rol: 'ADMIN' | 'COORDINADOR' | 'TRABAJADOR' | 'PROPIETARIO' | string;
  modulosPermitidos: string[];
  negocioId: number;
  sedeId: number | null;
  sedeNombre?: string | null;
}

export interface Sede {
  id: number;
  nombre: string;
  direccion?: string | null;
  telefono?: string | null;
  activo: boolean;
}

export interface LoginRequest {
  usuario: string;
  password: string;
  empresaSlug?: string;
}

export interface LoginResponse {
  accessToken: string;
  expira: string;
  refreshToken: string;
  usuario: UsuarioSesion;
}

export interface ConfiguracionNegocio {
  id: number;
  nombreNegocio: string;
  logoUrl?: string | null;
  colorPrimario: string;
  colorSecundario: string;
  colorAcento: string;
  direccion?: string | null;
  telefono?: string | null;
  ruc?: string | null;
  horarioAtencion?: string | null;
  igv: number;
  metaMensual: number;
  solesPorPunto: number;
  anchoTicketMm: number;
  mensajePieTicket?: string | null;
  condicionesServicio?: string | null;
  notasProduccion?: string | null;
  costoDelivery: number;
  valorPuntoCanje: number;
  maxDescuentoPct: number;
  servicioDeliveryId?: number | null;
  // Cobro por Yape/Plin del negocio (mensaje de delivery + QR para el repartidor).
  yapeNumero?: string | null;
  yapeTitular?: string | null;
  yapeQrUrl?: string | null;
}

export interface Cliente {
  id: number;
  nombre: string;
  celular?: string | null;
  dni?: string | null;
  documentoFiscal?: string | null;
  direccion?: string | null;
  puntos: number;
  fechaCreacion?: string;
  fechaNacimiento?: string | null;
}

export interface Servicio {
  id: number;
  nombre: string;
  precio: number;
  unidad: string;
  categoriaId?: number | null;
}

export interface AreaLavado {
  id: number;
  nombre: string;
  orden: number;
  tiempoEstMinutos: number;
}

export interface PedidoItem {
  id?: number;
  servicioId: number;
  servicioNombre?: string;
  servicioUnidad?: string | null;
  cantidad: number;
  precioUnit: number;
  total: number;
  descripcion?: string | null;
  /** Cantidad ya entregada al cliente (entregas parciales). Pendiente = cantidad - cantidadEntregada. */
  cantidadEntregada?: number;
}

export type EstadoPago = 'PENDIENTE' | 'PARCIAL' | 'PAGADO';
export type EstadoProceso = 'PENDIENTE' | 'EN_PROCESO' | 'LISTO' | 'ENTREGA_PARCIAL' | 'ENTREGADO' | 'ANULADO';
export type ModalidadPedido = 'Tienda' | 'Recojo' | 'Delivery';

export interface Pedido {
  id: number;
  numero: number;
  clienteId: number;
  clienteNombre?: string;
  clienteCelular?: string;
  clienteDni?: string | null;
  clientePuntos?: number;
  usuarioNombre?: string | null;
  fechaIngreso: string;
  fechaEntregaEst?: string | null;
  modalidad: ModalidadPedido;
  direccionEntrega?: string | null;
  distritoEntrega?: string | null;
  referenciaEntrega?: string | null;
  latitudEntrega?: number | null;
  longitudEntrega?: number | null;
  subtotal: number;
  descuento: number;
  esUrgente: boolean;
  recargoUrgente: number;
  redondeo: number;
  total: number;
  montoPagado: number;
  estadoPago: EstadoPago;
  estadoProceso: EstadoProceso;
  areaActualId?: number | null;
  areaActualNombre?: string | null;
  observaciones?: string | null;
  anulado: boolean;
  motivoAnulacion?: string | null;
  codigoAntiguo?: string | null;
  motorizadoId?: number | null;
  motorizadoNombre?: string | null;
  motorizadoCelular?: string | null;
  items: PedidoItem[];
}

export interface TipoGasto {
  id: number;
  nombre: string;
}

export interface MovimientoCaja {
  id: number;
  fecha: string;
  tipo: 'INGRESO' | 'GASTO';
  metodoPago: string;
  monto: number;
  descripcion?: string | null;
  pedidoId?: number | null;
  pedidoNumero?: number | null;
  clienteNombre?: string | null;
  usuarioNombre?: string | null;
  tipoGastoId?: number | null;
  tipoGastoNombre?: string | null;
}

export interface PedidoAbandonado {
  pedidoId: number;
  numero: number;
  clienteNombre: string;
  clienteCelular?: string | null;
  total: number;
  montoPagado: number;
  fechaListo: string;
  diasEsperando: number;
}

export interface MiSuscripcion {
  mostrar: boolean;
  tipo: 'OK' | 'AVISO' | 'VENCIDA' | string;
  mensaje: string;
  proximoPago?: string | null;
  diasParaVencer?: number | null;
  estadoSuscripcion: string;
}

export interface NegocioResumen {
  id: number;
  nombre: string;
  slug: string;
  activo: boolean;
  fechaCreacion: string;
  cantidadSedes: number;
  cantidadUsuarios: number;
  planSuscripcion: string;
  estadoSuscripcion: string;
  montoMensual: number;
  proximoPago?: string | null;
  ultimoAcceso?: string | null;
  pedidosMes: number;
}

export interface CrearNegocioRequest {
  nombre: string;
  slug: string;
  rucEmpresa?: string | null;
  titularNombre?: string | null;
  titularEmail?: string | null;
  titularCelular?: string | null;
  sedeNombre: string;
  adminUsuario: string;
  adminNombreCompleto: string;
  adminEmail?: string | null;
  adminPassword: string;
}

export interface PlataformaResumen {
  totalEmpresas: number;
  empresasActivas: number;
  empresasSuspendidas: number;
  empresasNuevasMes: number;
  ingresoMensualRecurrente: number;
  pedidosMesTotal: number;
  empresasPorVencer: number;
  empresasVencidas: number;
  recaudadoMes: number;
}

export interface SedeResumen { id: number; nombre: string; direccion?: string | null; activo: boolean; }
export interface UsuarioResumen { id: number; usuario: string; nombreCompleto: string; rolCodigo: string; activo: boolean; ultimoAcceso?: string | null; }

export interface NegocioDetalle {
  id: number;
  nombre: string;
  slug: string;
  rucEmpresa?: string | null;
  titularNombre?: string | null;
  titularEmail?: string | null;
  titularCelular?: string | null;
  notasInternas?: string | null;
  activo: boolean;
  fechaCreacion: string;
  planSuscripcion: string;
  estadoSuscripcion: string;
  montoMensual: number;
  proximoPago?: string | null;
  pedidosMes: number;
  ultimoAcceso?: string | null;
  adminUsuario?: string | null;
  sedes: SedeResumen[];
  usuarios: UsuarioResumen[];
}

export interface EditarNegocioRequest {
  nombre: string;
  rucEmpresa?: string | null;
  titularNombre?: string | null;
  titularEmail?: string | null;
  titularCelular?: string | null;
  notasInternas?: string | null;
}

export interface CambiarSuscripcionRequest {
  planSuscripcion: string;
  estadoSuscripcion: string;
  montoMensual: number;
  proximoPago?: string | null;
}

// ---------- Cobranza del propietario ----------
export interface PagoSuscripcion {
  id: number;
  fecha: string;
  monto: number;
  metodo: string;
  periodoDesde?: string | null;
  periodoHasta?: string | null;
  nota?: string | null;
  fechaCreacion: string;
}

export interface RegistrarPagoSuscripcionRequest {
  monto: number;
  metodo: string;
  meses: number;
  nota?: string | null;
}

export interface ConfiguracionPlataforma {
  nombrePlataforma: string;
  yapeNombre?: string | null;
  yapeNumero?: string | null;
  contactoSoporte?: string | null;
  diasAvisoCobro: number;
}

export interface CrearPedidoRequest {
  clienteId?: number;
  clienteNuevo?: Partial<Cliente>;
  modalidad: ModalidadPedido;
  direccionEntrega?: string | null;
  distritoEntrega?: string | null;
  referenciaEntrega?: string | null;
  latitudEntrega?: number | null;
  longitudEntrega?: number | null;
  items: Array<{
    servicioId: number;
    cantidad: number;
    precioUnit: number;
    total: number;
    descripcion?: string | null;
  }>;
  descuentoPct: number;
  puntosACanjear?: number | null;
  esUrgente: boolean;
  recargoUrgentePct: number;
  costoDelivery?: number | null;
  montoPagado: number;
  metodoPagoInicial: string;
  fechaEntregaEst?: string | null;
  observaciones?: string | null;
  areaInicialId?: number | null;
  fechaIngreso?: string | null;
  codigoAntiguo?: string | null;
}

export interface DestinoDeliveryRequest {
  direccionEntrega: string;
  distritoEntrega: string;
  referenciaEntrega?: string | null;
  latitudEntrega?: number | null;
  longitudEntrega?: number | null;
}
