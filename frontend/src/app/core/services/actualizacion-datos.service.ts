import { Injectable } from '@angular/core';
import { Observable, Subject, filter } from 'rxjs';

export type CanalActualizacion =
  | 'pedidos'
  | 'dashboard'
  | 'caja'
  | 'clientes'
  | 'inventario'
  | 'facturacion'
  | 'plataforma'
  | 'catalogos'
  | 'reparto'
  | 'datos'
  | 'foco';

export interface EventoActualizacion {
  canales: CanalActualizacion[];
  instante: number;
  origen: string;
}

const CANAL_NAVEGADOR = 'lavanderia.actualizaciones';
const STORAGE_EVENTO = 'lavanderia.actualizacion';

/**
 * Bus liviano para mantener sincronizadas pantallas, contadores y pestañas.
 * El servidor sigue siendo la fuente de verdad: el evento solo indica qué volver a consultar.
 */
@Injectable({ providedIn: 'root' })
export class ActualizacionDatosService {
  private readonly eventos = new Subject<EventoActualizacion>();
  private readonly origen = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
  private readonly canal = typeof BroadcastChannel !== 'undefined'
    ? new BroadcastChannel(CANAL_NAVEGADOR)
    : null;

  constructor() {
    this.canal?.addEventListener('message', event => this.recibir(event.data));
    if (typeof window !== 'undefined') {
      window.addEventListener('storage', event => {
        if (event.key === STORAGE_EVENTO && event.newValue) {
          try { this.recibir(JSON.parse(event.newValue)); } catch { /* evento inválido */ }
        }
      });
      window.addEventListener('focus', () => this.emitirLocal(['foco']));
    }
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') this.emitirLocal(['foco']);
      });
    }
  }

  cambios(...canales: CanalActualizacion[]): Observable<EventoActualizacion> {
    const esperados = new Set(canales);
    return this.eventos.pipe(filter(evento =>
      evento.canales.includes('datos') || evento.canales.some(canal => esperados.has(canal))
    ));
  }

  notificar(canales: CanalActualizacion[]): void {
    if (canales.length === 0) return;
    const evento: EventoActualizacion = {
      canales: [...new Set(canales)],
      instante: Date.now(),
      origen: this.origen
    };
    this.eventos.next(evento);
    if (this.canal) {
      this.canal.postMessage(evento);
      return;
    }
    try {
      localStorage.setItem(STORAGE_EVENTO, JSON.stringify(evento));
      localStorage.removeItem(STORAGE_EVENTO);
    } catch { /* sincronización entre pestañas opcional */ }
  }

  private emitirLocal(canales: CanalActualizacion[]): void {
    this.eventos.next({ canales, instante: Date.now(), origen: this.origen });
  }

  private recibir(valor: unknown): void {
    const evento = valor as Partial<EventoActualizacion>;
    if (!Array.isArray(evento?.canales) || evento.origen === this.origen) return;
    this.eventos.next(evento as EventoActualizacion);
  }
}
