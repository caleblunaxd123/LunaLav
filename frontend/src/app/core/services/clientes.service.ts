import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { Cliente } from '../models/models';
import { PuntoBarra } from '../../shared/mini-barras/mini-barras.component';

export interface ClienteFrecuente {
  clienteId: number;
  nombre: string;
  celular: string | null;
  visitas: number;
}

export interface ClienteAnalitica {
  clienteId: number;
  nombre: string;
  celular: string | null;
  totalPedidos: number;
  ticketPromedio: number;
  ultimaCompra: string;
  diasSinComprar: number;
  deudaTotal: number;
}

export interface ClienteCumpleanos {
  clienteId: number;
  nombre: string;
  celular: string | null;
  fechaNacimiento: string;
  diasParaCumpleanos: number;
}

export interface MovimientoPuntos {
  id: number;
  clienteId: number;
  fecha: string;
  motivo: string;
  puntos: number;
  tipo: 'SUMA' | 'RESTA';
  usuarioNombre: string | null;
}

@Injectable({ providedIn: 'root' })
export class ClientesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/clientes`;

  tendencia(meses = 6) {
    return this.http.get<PuntoBarra[]>(`${this.base}/tendencia`, { params: new HttpParams().set('meses', meses) });
  }

  buscar(texto?: string, campo?: string, limite = 50) {
    let params = new HttpParams().set('limite', limite);
    if (texto) params = params.set('texto', texto);
    if (campo) params = params.set('campo', campo);
    return this.http.get<Cliente[]>(this.base, { params });
  }

  frecuentes(desde?: string, hasta?: string, limite = 25) {
    let params = new HttpParams().set('limite', limite);
    if (desde) params = params.set('desde', desde);
    if (hasta) params = params.set('hasta', hasta);
    return this.http.get<ClienteFrecuente[]>(`${this.base}/frecuentes`, { params });
  }

  obtener(id: number) {
    return this.http.get<Cliente>(`${this.base}/${id}`);
  }

  crear(c: Partial<Cliente>) {
    return this.http.post<Cliente>(this.base, c);
  }

  actualizar(id: number, c: Partial<Cliente>) {
    return this.http.put<void>(`${this.base}/${id}`, c);
  }

  desactivar(id: number) {
    return this.http.delete<{ mensaje: string }>(`${this.base}/${id}`);
  }

  fusionar(origenId: number, destinoId: number) {
    return this.http.post<{ mensaje: string }>(`${this.base}/fusionar`, { origenId, destinoId });
  }

  listarPuntos(clienteId: number) {
    return this.http.get<MovimientoPuntos[]>(`${this.base}/${clienteId}/puntos`);
  }

  agregarPuntos(clienteId: number, motivo: string, puntos: number, tipo: 'SUMA' | 'RESTA') {
    return this.http.post<void>(`${this.base}/${clienteId}/puntos`, { motivo, puntos, tipo });
  }

  analitica() {
    return this.http.get<ClienteAnalitica[]>(`${this.base}/analitica`);
  }

  cumpleanosProximos(dias = 30) {
    return this.http.get<ClienteCumpleanos[]>(`${this.base}/cumpleanos-proximos`, { params: { dias } });
  }

  importar(filas: Array<Record<string, string | number | null>>) {
    return this.http.post<ImportarClientesResultado>(`${this.base}/importar`, { filas });
  }
}

export interface ImportarClientesResultado {
  creados: number;
  omitidos: number;
  errores: Array<{ fila: number; nombre: string; motivo: string }>;
}
