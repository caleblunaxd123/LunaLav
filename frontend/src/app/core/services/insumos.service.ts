import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { PuntoBarra } from '../../shared/mini-barras/mini-barras.component';

export type ClaseInsumo = 'EQUIPO' | 'MATERIAL' | 'INSUMO';

export interface Insumo {
  id: number;
  nombre: string;
  unidadMedida: string;
  clase: ClaseInsumo;
  contenidoValor?: number | null;
  contenidoUnidad?: string | null;
  stockActual: number;
  stockMinimo: number;
  activo: boolean;
  ultimaCompra?: string | null;
  /** Fecha de ingreso/registro al inventario (YYYY-MM-DD), opcional. */
  fechaIngreso?: string | null;
  /** Fecha de vencimiento/caducidad (YYYY-MM-DD), opcional. */
  fechaVencimiento?: string | null;
  enUso?: boolean;
}

export interface MovimientoInsumo {
  id: number;
  insumoId: number;
  insumoNombre?: string;
  tipo: 'COMPRA' | 'CONSUMO' | 'AJUSTE';
  cantidad: number;
  costoTotal: number | null;
  fecha: string;
  usuarioNombre?: string;
  descripcion?: string | null;
}

export interface RegistrarMovimientoInsumoRequest {
  tipo: 'COMPRA' | 'CONSUMO' | 'AJUSTE';
  cantidad: number;
  costoTotal?: number | null;
  metodoPago?: string | null;
  tipoGastoId?: number | null;
  descripcion?: string | null;
  fecha?: string | null;
}

@Injectable({ providedIn: 'root' })
export class InsumosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/insumos`;

  listar() { return this.http.get<Insumo[]>(this.base); }
  bajoStock() { return this.http.get<Insumo[]>(`${this.base}/bajo-stock`); }
  tendenciaConsumo(dias = 14) {
    return this.http.get<PuntoBarra[]>(`${this.base}/tendencia-consumo`, { params: new HttpParams().set('dias', dias) });
  }
  crear(i: Partial<Insumo>) { return this.http.post<Insumo>(this.base, i); }
  actualizar(id: number, i: Partial<Insumo>) { return this.http.put<void>(`${this.base}/${id}`, i); }
  desactivar(id: number) { return this.http.delete<{ mensaje: string }>(`${this.base}/${id}`); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }
  importar(filas: Array<Record<string, string | number | null>>) {
    return this.http.post<ImportarInsumosResultado>(`${this.base}/importar`, { filas });
  }

  registrarMovimiento(insumoId: number, req: RegistrarMovimientoInsumoRequest) {
    return this.http.post<{ id: number; mensaje: string }>(`${this.base}/${insumoId}/movimientos`, req);
  }

  /** Corrige fecha y nota de un movimiento (solo admin). No cambia el stock. */
  editarMovimiento(movimientoId: number, req: { fecha: string; descripcion?: string | null }) {
    return this.http.put<void>(`${this.base}/movimientos/${movimientoId}`, req);
  }

  /** Elimina un movimiento y revierte su efecto en el stock (solo admin). */
  eliminarMovimiento(movimientoId: number) {
    return this.http.delete<{ mensaje: string }>(`${this.base}/movimientos/${movimientoId}`);
  }

  movimientos(insumoId?: number, desde?: string, hasta?: string) {
    let params = new HttpParams();
    if (insumoId) params = params.set('insumoId', insumoId);
    if (desde) params = params.set('desde', desde);
    if (hasta) params = params.set('hasta', hasta);
    return this.http.get<MovimientoInsumo[]>(`${this.base}/movimientos`, { params });
  }
}

export interface ImportarInsumosResultado {
  creados: number;
  omitidos: number;
  errores: Array<{ fila: number; nombre: string; motivo: string }>;
}
