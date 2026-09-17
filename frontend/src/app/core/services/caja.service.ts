import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { MovimientoCaja, TipoGasto } from '../models/models';

@Injectable({ providedIn: 'root' })
export class CajaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/caja`;

  tiposGasto() {
    return this.http.get<TipoGasto[]>(`${this.base}/tipos-gasto`);
  }

  movimientos(fecha: string, usuarioId?: number) {
    let params = new HttpParams().set('fecha', fecha);
    if (usuarioId != null) params = params.set('usuarioId', usuarioId);
    return this.http.get<MovimientoCaja[]>(`${this.base}/movimientos`, { params });
  }

  usuariosDelDia(fecha: string) {
    return this.http.get<UsuarioDelDia[]>(`${this.base}/usuarios-dia`, { params: new HttpParams().set('fecha', fecha) });
  }

  cuadreDelUsuario(fecha: string, usuarioId: number) {
    return this.http.get<CuadreCajaGuardado>(`${this.base}/cuadres/del-usuario`, {
      params: new HttpParams().set('fecha', fecha).set('usuarioId', usuarioId)
    });
  }

  registrarGasto(monto: number, metodoPago: string, tipoGastoId: number | null, descripcion?: string) {
    return this.http.post<MovimientoCaja>(`${this.base}/gastos`, { monto, metodoPago, tipoGastoId, descripcion });
  }

  guardarCuadre(data: {
    fecha: string;
    usuarioId?: number;
    cajaInicial: number;
    pedidosPagadosEfect: number;
    gastos: number;
    totalContado: number;
    diferencia: number;
    cajaFinal: number;
    corte: number;
    ingresosDigital: number;
    ingresosTarjeta: number;
    nota?: string;
    observaciones?: string;
    detalleConteo?: string;
  }) {
    return this.http.post<CuadreCajaGuardado>(`${this.base}/cuadres`, data);
  }

  obtenerCuadre(id: number) {
    return this.http.get<CuadreCajaGuardado>(`${this.base}/cuadres/${id}`);
  }

  obtenerUltimoAnterior(fecha: string) {
    return this.http.get<CuadreCajaGuardado>(`${this.base}/cuadres/ultimo-anterior`, { params: new HttpParams().set('fecha', fecha) });
  }
}

export interface UsuarioDelDia {
  id: number;
  nombreCompleto: string;
  rolNombre: string;
  movimientos: number;
  tieneCuadre: boolean;
}

export interface CuadreCajaGuardado {
  id: number;
  fecha: string;
  usuarioId: number;
  usuarioNombre?: string;
  cajaInicial: number;
  pedidosPagadosEfect: number;
  gastos: number;
  totalContado: number;
  diferencia: number;
  cajaFinal: number;
  corte: number;
  ingresosDigital: number;
  ingresosTarjeta: number;
  nota?: string;
  observaciones?: string;
  detalleConteo?: string | null;
  fechaCreacion: string;
}
