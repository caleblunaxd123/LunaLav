import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface ReporteResult {
  columnas: string[];
  filas: Record<string, string>[];
  /** Acción por fila (ej: 'donar', 'reenviar-almacen'). Si está, cada fila trae '_id'. */
  accion?: string | null;
}

export type ReporteKey =
  | 'ordenes-pendientes' | 'gastos' | 'general' | 'servicios' | 'cuadres-caja'
  | 'ordenes-mensual' | 'almacen' | 'anulados' | 'registro-entregas' | 'pagos' | 'descuento-directo';

export interface SlaArea {
  areaId: number;
  areaNombre: string;
  orden: number;
  tiempoEstMinutos: number;
  minutosPromedioReal: number;
  pedidosProcesados: number;
}

export interface PedidoEstancado {
  pedidoId: number;
  numero: number;
  clienteNombre: string;
  areaId: number;
  areaNombre: string;
  minutosEnArea: number;
  tiempoEstMinutos: number;
}

export interface TableroSla {
  areas: SlaArea[];
  estancados: PedidoEstancado[];
}

export interface VistaGerencial {
  ventasHoy: number;
  cobradoHoy: number;
  ventasMes: number;
  pedidosEntregadosHoy: number;
  pedidosEntregadosTiendaHoy: number;
  pedidosEntregadosDomicilioHoy: number;
  pedidosEntregadosSemana: number;
  pedidosEntregadosMes: number;
  saldoPorCobrar: number;
  gastosMes: number;
  utilidadMes: number;
  pedidosActivos: number;
  pedidosListosSinRecoger: number;
  comprobantesPendientes: number;
  comprobantesRechazados: number;
  insumosBajoStock: number;
  cajaEsperadaHoy: number;
  // Bloques para el gerente
  ventasUltimos14Dias: PuntoTendencia[];
  ventasMesAnterior: number;
  ventasMesAnteriorAlDia: number;
  pedidosMesCount: number;
  ticketPromedioMes: number;
  ingresosEfectivoMes: number;
  ingresosDigitalMes: number;
  ingresosTarjetaMes: number;
  pedidosPendientes: number;
  pedidosEnProceso: number;
  topServiciosMes: TopServicioGerencial[];
}

export interface PuntoTendencia {
  fecha: string;
  total: number;
}

export interface TopServicioGerencial {
  nombre: string;
  cantidad: number;
  total: number;
}

export interface ConsolidadoSede {
  sedeId: number;
  sedeNombre: string;
  ventasHoy: number;
  ventasMes: number;
  saldoPorCobrar: number;
  pedidosActivos: number;
  pedidosListos: number;
}

@Injectable({ providedIn: 'root' })
export class ReportesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/reportes`;

  obtener(key: ReporteKey, desde?: string, hasta?: string) {
    let params = new HttpParams();
    if (desde) params = params.set('desde', desde);
    if (hasta) params = params.set('hasta', hasta);
    return this.http.get<ReporteResult>(`${this.base}/${key}`, { params });
  }

  sla(desde?: string, hasta?: string) {
    let params = new HttpParams();
    if (desde) params = params.set('desde', desde);
    if (hasta) params = params.set('hasta', hasta);
    return this.http.get<TableroSla>(`${this.base}/sla`, { params });
  }

  vistaGerencial() {
    return this.http.get<VistaGerencial>(`${this.base}/vista-gerencial`);
  }

  consolidado() {
    return this.http.get<ConsolidadoSede[]>(`${this.base}/consolidado`);
  }

  cuadresDiarios(anio: number, mes: number) {
    const params = new HttpParams().set('anio', anio).set('mes', mes);
    return this.http.get<CuadresDiariosReporte>(`${this.base}/cuadres-diarios`, { params });
  }

  /** Descarga el reporte como .xlsx real (generado en el backend). */
  exportarExcel(key: ReporteKey, desde?: string, hasta?: string) {
    let params = new HttpParams();
    if (desde) params = params.set('desde', desde);
    if (hasta) params = params.set('hasta', hasta);
    return this.http.get(`${this.base}/export/${key}`, { params, responseType: 'blob' });
  }

  // Acciones operativas desde reportes (mutan el pedido).
  donarPedido(pedidoId: number) {
    return this.http.post<void>(`${environment.apiUrl}/pedidos/${pedidoId}/donar`, {});
  }
  reenviarAlmacen(pedidoId: number) {
    return this.http.post<void>(`${environment.apiUrl}/pedidos/${pedidoId}/reenviar-almacen`, {});
  }
}

export interface CuadreDiarioFila {
  id: number;
  usuarioId: number;
  usuarioNombre: string;
  cajaInicial: number;
  ingresosEfectivo: number;
  egresos: number;
  montoEnCaja: number;
  corte: number;
  cajaFinal: number;
  estado: 'CUADRA' | 'SOBRA' | 'FALTA';
  margenError: number;
  nota?: string | null;
  ingresosDigital: number;
  ingresosTarjeta: number;
}

export interface FormaPagoDia {
  metodo: string;
  cantidad: number;
  monto: number;
}

export interface CuadreDiarioDia {
  fecha: string;
  cuadres: CuadreDiarioFila[];
  sinInformacion: boolean;
  noCuadradoIngresos: number;
  noCuadradoEgresos: number;
  formasPago: FormaPagoDia[];
}

export interface CuadresDiariosReporte {
  anio: number;
  mes: number;
  dias: CuadreDiarioDia[];
}
