import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { CrearPedidoRequest, DestinoDeliveryRequest, Pedido, PedidoAbandonado } from '../models/models';
import { PromocionValida } from './promociones.service';
import { PuntoBarra } from '../../shared/mini-barras/mini-barras.component';

export interface TendenciaPedidos {
  recibidos: PuntoBarra[];
  entregados: PuntoBarra[];
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  pagina: number;
  tamanoPagina: number;
}

export interface PedidoHistorial {
  id: number;
  areaId: number | null;
  areaNombre: string | null;
  estadoProceso: string;
  fecha: string;
  nota: string | null;
  notificadoWsp: boolean;
}

export interface Dashboard {
  pedidosPorEstado: Record<string, number>;
  pedidosPorArea: Array<{ areaId: number; areaNombre: string; cantidad: number }>;
  ventasDelDia: number;
  cobradoDelDia: number | null;
  saldoPorCobrar: number | null;
  cajaEsperadaHoy: number | null;
  pedidosEntregadosHoy: number;
  pedidosEntregadosTiendaHoy: number;
  pedidosEntregadosDomicilioHoy: number;
  pedidosEntregadosSemana: number;
  pedidosEntregadosMes: number;
  totalPendientes: number;
  totalEnProceso: number;
  totalListos: number;
  pedidosDelMes: number;
  metaMensual: number;
  insumosBajoStock: number | null;
  // Bloques estilo panel (comparativos, actividad y distribución)
  ordenesHoy: number;
  ordenesAyer: number;
  ventasAyer: number;
  totalClientes: number;
  clientesNuevosMes: number;
  clientesNuevosMesAnterior: number;
  ventasSemana: Array<{ fecha: string; total: number }>;
  ordenesRecientes: OrdenReciente[];
  serviciosMasSolicitados: Array<{ nombre: string; cantidad: number; total: number }>;
  comprobantesPendientes: number | null;
  comprobantesRechazados: number | null;
  totalPedidosEstancados: number;
  totalPedidosAbandonados: number;
  slaPorArea: Array<{
    areaId: number;
    areaNombre: string;
    orden: number;
    tiempoEstMinutos: number;
    minutosPromedioReal: number;
    pedidosProcesados: number;
  }>;
  pedidosEstancados: Array<{
    pedidoId: number;
    numero: number;
    clienteNombre: string;
    areaId: number;
    areaNombre: string;
    minutosEnArea: number;
    tiempoEstMinutos: number;
  }>;
  pedidosAbandonados: Array<{
    pedidoId: number;
    numero: number;
    clienteNombre: string;
    clienteCelular?: string | null;
    total: number;
    montoPagado: number;
    fechaListo: string;
    diasEsperando: number;
  }>;
  actualizadoEn: string;
}

export interface OrdenReciente {
  numero: number;
  clienteNombre: string;
  servicioPrincipal: string;
  estadoProceso: string;
  total: number;
}

export interface PedidoContadores {
  pedidosDelMes: number;
  totalPendientes: number;
  totalOtros: number;
  totalUltimos: number;
}

@Injectable({ providedIn: 'root' })
export class PedidosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/pedidos`;

  tendencia(dias = 14) {
    return this.http.get<TendenciaPedidos>(`${this.base}/tendencia`, { params: new HttpParams().set('dias', dias) });
  }

  listar(
    filtro?: string,
    pagina = 1,
    tamanoPagina = 15,
    busqueda?: string,
    desde?: string,
    hasta?: string,
    campoFecha?: 'ingreso' | 'entrega'
  ) {
    let params = new HttpParams().set('pagina', pagina).set('tamanoPagina', tamanoPagina);
    if (busqueda) params = params.set('busqueda', busqueda);
    else if (filtro) params = params.set('filtro', filtro);
    if (desde) params = params.set('desde', desde);
    if (hasta) params = params.set('hasta', hasta);
    if (campoFecha) params = params.set('campoFecha', campoFecha);
    return this.http.get<PagedResult<Pedido>>(this.base, { params });
  }

  obtener(id: number) {
    return this.http.get<Pedido>(`${this.base}/${id}`);
  }

  listarPorCliente(clienteId: number, filtro?: string, pagina = 1, tamanoPagina = 10) {
    let params = new HttpParams().set('pagina', pagina).set('tamanoPagina', tamanoPagina);
    if (filtro) params = params.set('filtro', filtro);
    return this.http.get<PagedResult<Pedido>>(`${this.base}/por-cliente/${clienteId}`, { params });
  }

  crear(req: CrearPedidoRequest) {
    return this.http.post<Pedido>(this.base, req);
  }

  avanzar(id: number, nuevaAreaId: number | null, nuevoEstado: string, nota?: string) {
    return this.http.post<void>(`${this.base}/${id}/avanzar`, { nuevaAreaId, nuevoEstado, nota });
  }

  siguienteArea(id: number, recibidoPor?: string) {
    return this.http.post<void>(`${this.base}/${id}/siguiente-area`, { recibidoPor: recibidoPor || null });
  }

  historial(id: number) {
    return this.http.get<PedidoHistorial[]>(`${this.base}/${id}/historial`);
  }

  /** Cobros del pedido, cada uno con su método (efectivo, Yape, Plin, ...). */
  pagos(id: number) {
    return this.http.get<PagoPedido[]>(`${this.base}/${id}/pagos`);
  }

  dashboard() {
    return this.http.get<Dashboard>(`${this.base}/dashboard`);
  }

  /** Tendencia de ventas (S/ por día) para el rango de días indicado (dashboard). */
  ventasTendencia(dias: number) {
    return this.http.get<Array<{ fecha: string; total: number }>>(`${this.base}/ventas-tendencia`, { params: { dias } });
  }

  contadores() {
    return this.http.get<PedidoContadores>(`${this.base}/contadores`);
  }

  siguienteNumero() {
    return this.http.get<number>(`${this.base}/siguiente-numero`);
  }

  validarCodigoPromocion(codigo: string, clienteId?: number | null) {
    let params: Record<string, string | number> = { codigo };
    if (clienteId) params = { ...params, clienteId };
    return this.http.get<PromocionValida>(`${this.base}/promocion/validar`, { params });
  }

  abandonados(dias = 3) {
    return this.http.get<PedidoAbandonado[]>(`${this.base}/abandonados`, { params: { dias } });
  }

  registrarPago(id: number, monto: number, metodo: string, descripcion?: string) {
    return this.http.post<void>(`${this.base}/${id}/pagos`, { monto, metodo, descripcion });
  }

  /** Registra una entrega (parcial o final): qué prendas se lleva el cliente y con qué pagos (mixto). */
  entregar(id: number, req: EntregarPedidoRequest) {
    return this.http.post<{ estadoProceso: string }>(`${this.base}/${id}/entregar`, req);
  }

  /** Historial de entregas (parciales y final) del pedido. */
  entregas(id: number) {
    return this.http.get<PedidoEntrega[]>(`${this.base}/${id}/entregas`);
  }

  agregarItem(id: number, servicioId: number, cantidad: number, descripcion?: string) {
    return this.http.post<void>(`${this.base}/${id}/items`, { servicioId, cantidad, descripcion });
  }

  anular(id: number, motivo: string) {
    return this.http.post<void>(`${this.base}/${id}/anular`, { motivo });
  }

  cambiarFechaEntrega(id: number, fecha: string, motivo?: string) {
    return this.http.put<void>(`${this.base}/${id}/fecha-entrega`, { fecha, motivo });
  }

  convertirDelivery(id: number, destino: DestinoDeliveryRequest) {
    return this.http.post<void>(`${this.base}/${id}/convertir-delivery`, destino);
  }

  linkSeguimiento(id: number) {
    return this.http.get<{ token: string }>(`${this.base}/${id}/link-seguimiento`);
  }

  linkRepartidor(id: number) {
    return this.http.get<{ token: string }>(`${this.base}/${id}/link-repartidor`);
  }

  asignarMotorizado(id: number, motorizadoId: number | null) {
    return this.http.put<void>(`${this.base}/${id}/motorizado`, { motorizadoId });
  }
}

export interface PagoPedido {
  id: number;
  fecha: string;
  metodoPago: string;
  monto: number;
  descripcion?: string | null;
  usuarioNombre?: string | null;
}

/** Una línea de cobro (pago mixto: parte efectivo, parte Yape, etc.). */
export interface PagoLinea {
  metodo: string;
  monto: number;
}

/** Un ítem que se entrega en esta visita, con la cantidad entregada ahora. */
export interface EntregaItem {
  pedidoItemId: number;
  cantidad: number;
}

export interface EntregarPedidoRequest {
  items: EntregaItem[];
  pagos: PagoLinea[];
  recibidoPor?: string | null;
  nota?: string | null;
}

export interface EntregaDetalle {
  pedidoItemId: number;
  servicioNombre?: string | null;
  servicioUnidad?: string | null;
  cantidad: number;
}

export interface PedidoEntrega {
  id: number;
  fecha: string;
  usuarioNombre?: string | null;
  recibidoPor?: string | null;
  nota?: string | null;
  esFinal: boolean;
  montoCobrado: number;
  items: EntregaDetalle[];
}
