import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { PagedResult } from './pedidos.service';

export interface ConfiguracionFacturacion {
  razonSocial?: string | null;
  rucEmisor?: string | null;
  ambiente: 'BETA' | 'PRODUCCION';
  solUsuario?: string | null;
  solClaveNueva?: string | null;
  certificadoPfxBase64?: string | null;
  certificadoPasswordNueva?: string | null;
  serieBoleta: string;
  serieFactura: string;
  activo: boolean;
  soloBoletas: boolean;
  tieneCertificado: boolean;
  tieneCredencialesSol: boolean;
  proveedor: 'SUNAT_DIRECTO' | 'APISUNAT';
  apiSunatPersonaId?: string | null;
  apiSunatTokenNuevo?: string | null;
  tieneCredencialesApiSunat: boolean;
  direccionFiscal?: string | null;
  ubigeo?: string | null;
  codigoEstablecimiento: string;
  emailEmisor?: string | null;
  correlativoBoleta: number;
  correlativoFactura: number;
  requiereCertificadoLocal: boolean;
}

export interface Comprobante {
  id: number;
  pedidoId: number;
  pedidoNumero?: number | null;
  tipo: 'BOLETA' | 'FACTURA' | 'NOTA_CREDITO' | 'NOTA_DEBITO' | 'GUIA_REMISION';
  serie: string;
  correlativo: number;
  numeroCompleto: string;
  clienteNombre: string;
  clienteTipoDoc: string;
  clienteNumDoc?: string | null;
  opGravada: number;
  igv: number;
  total: number;
  estado: 'PENDIENTE' | 'ACEPTADO' | 'RECHAZADO' | 'ANULADO' | 'ERROR' | 'SIMULADO';
  descripcionRespuestaSunat?: string | null;
  fechaEmision: string;
  proveedor: string;
  ambiente: string;
  externalId?: string | null;
  codigoRespuestaSunat?: string | null;
  fechaEnvio?: string | null;
  fechaRespuesta?: string | null;
  esSimulado: boolean;
  tieneXml: boolean;
  tieneCdr: boolean;
  estadoAnulacion?: string | null;
  motivoAnulacion?: string | null;
  fechaAnulacion?: string | null;
  // Nota de crédito: documento referenciado y motivo (catálogo 09).
  docRefSerieNumero?: string | null;
  motivoNotaCodigo?: string | null;
  motivoNotaDescripcion?: string | null;
  // Guía de remisión: datos de traslado (null en los demás tipos).
  guia?: GuiaRemision | null;
  detalles: ComprobanteDetalle[];
  intentos: ComprobanteIntento[];
}

export interface GuiaRemision {
  motivoTrasladoCodigo: string;
  motivoTrasladoDescripcion?: string | null;
  pesoBrutoTotal: number;
  unidadPeso: string;
  numeroBultos?: number | null;
  fechaInicioTraslado: string;
  modalidadTransporte: string;
  partidaUbigeo: string; partidaDireccion: string;
  llegadaUbigeo: string; llegadaDireccion: string;
  transportistaNumDoc?: string | null; transportistaRazonSocial?: string | null;
  vehiculoPlaca?: string | null;
  conductorTipoDoc?: string | null; conductorNumDoc?: string | null;
  conductorNombres?: string | null; conductorLicencia?: string | null;
}

export interface GuiaRemisionPayload {
  motivoCodigo: string; motivoDescripcion?: string;
  pesoBrutoTotal: number; unidadPeso?: string; numeroBultos?: number | null;
  fechaInicioTraslado: string; modalidadTransporte: string;
  partidaUbigeo: string; partidaDireccion: string;
  llegadaUbigeo: string; llegadaDireccion: string;
  transportistaNumDoc?: string; transportistaRazonSocial?: string;
  vehiculoPlaca?: string; conductorTipoDoc?: string; conductorNumDoc?: string;
  conductorNombres?: string; conductorLicencia?: string;
}

export interface ComprobanteDetalle { numeroLinea: number; descripcion: string; unidadMedida: string; cantidad: number; precioUnitarioIgv: number; valorVenta: number; igv: number; total: number; }
export interface ComprobanteIntento { id: number; accion: string; estado: string; codigo?: string | null; descripcion?: string | null; fecha: string; usuarioId?: number | null; }
export interface FiltrosComprobante { tipo?: string; estado?: string; busqueda?: string; desde?: string; hasta?: string; }

export interface KpiComprobantesMes {
  anio: number;
  mes: number;
  boletasCantidad: number;
  boletasMonto: number;
  facturasCantidad: number;
  facturasMonto: number;
  totalCantidad: number;
  totalMonto: number;
}

export interface ResultadoConexionFacturacion {
  exitoso: boolean;
  mensaje: string;
  produccion?: boolean | null;
  ultimoNumero?: string | null;
  numeroSugerido?: string | null;
}

@Injectable({ providedIn: 'root' })
export class FacturacionService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/facturacion`;

  obtenerConfiguracion() {
    return this.http.get<ConfiguracionFacturacion>(`${this.base}/configuracion`);
  }

  /** Estado ligero para el punto de venta (lo puede leer la trabajadora). */
  estado() {
    return this.http.get<{ activa: boolean; soloBoletas: boolean }>(`${this.base}/estado`);
  }

  guardarConfiguracion(c: ConfiguracionFacturacion) {
    return this.http.put<void>(`${this.base}/configuracion`, c);
  }

  probarConfiguracion(c: ConfiguracionFacturacion) {
    return this.http.post<ResultadoConexionFacturacion>(`${this.base}/configuracion/probar`, c);
  }

  emitirComprobante(pedidoId: number, tipo: 'BOLETA' | 'FACTURA') {
    return this.http.post<Comprobante>(`${environment.apiUrl}/pedidos/${pedidoId}/comprobante`, { tipo });
  }

  listarComprobantes(pagina = 1, tamanoPagina = 15, filtros: FiltrosComprobante = {}) {
    let params = new HttpParams().set('pagina', pagina).set('tamanoPagina', tamanoPagina);
    Object.entries(filtros).forEach(([k, v]) => { if (v) params = params.set(k, v); });
    return this.http.get<PagedResult<Comprobante>>(`${this.base}/comprobantes`, { params });
  }

  /** KPI mensual de boletas y facturas (conteo + monto por mes). */
  kpiMensual(meses = 6) {
    return this.http.get<KpiComprobantesMes[]>(`${this.base}/kpi`, { params: new HttpParams().set('meses', meses) });
  }

  obtenerComprobante(id: number) {
    return this.http.get<Comprobante>(`${this.base}/comprobantes/${id}`);
  }

  /** Blob del PDF: se pide vía HttpClient (no un link directo) para que el interceptor adjunte el token. */
  descargarPdf(id: number) {
    return this.http.get(`${this.base}/comprobantes/${id}/pdf`, { responseType: 'blob' });
  }

  anular(id: number, motivo: string) {
    return this.http.post<Comprobante>(`${this.base}/comprobantes/${id}/anular`, { motivo });
  }

  /** Emite una Nota de Crédito (catálogo 09) sobre un comprobante aceptado. */
  emitirNotaCredito(id: number, motivoCodigo: string, motivo: string) {
    return this.http.post<Comprobante>(`${this.base}/comprobantes/${id}/nota-credito`, { motivoCodigo, motivo });
  }

  /** Emite una Nota de Débito (catálogo 10) sobre un comprobante aceptado. */
  emitirNotaDebito(id: number, motivoCodigo: string, motivo: string) {
    return this.http.post<Comprobante>(`${this.base}/comprobantes/${id}/nota-debito`, { motivoCodigo, motivo });
  }

  /** Emite una Guía de Remisión Remitente (GRE) desde una boleta/factura. */
  emitirGuiaRemision(id: number, payload: GuiaRemisionPayload) {
    return this.http.post<Comprobante>(`${this.base}/comprobantes/${id}/guia-remision`, payload);
  }

  reenviar(id: number) {
    return this.http.post<Comprobante>(`${this.base}/comprobantes/${id}/reenviar`, {});
  }

  sincronizar(id: number) { return this.http.post<Comprobante>(`${this.base}/comprobantes/${id}/sincronizar`, {}); }
  descargarXml(id: number) { return this.http.get(`${this.base}/comprobantes/${id}/xml`, { responseType: 'blob' }); }
  descargarCdr(id: number) { return this.http.get(`${this.base}/comprobantes/${id}/cdr`, { responseType: 'blob' }); }

  // Respaldo local (SUNAT): carpeta de respaldo y backfill de comprobantes ya emitidos.
  respaldoInfo() { return this.http.get<{ carpeta: string }>(`${this.base}/respaldo`); }
  respaldarTodos() { return this.http.post<{ respaldados: number; carpeta: string }>(`${this.base}/respaldo/generar-todos`, {}); }
}
