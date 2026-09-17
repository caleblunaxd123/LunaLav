import { Injectable, signal } from '@angular/core';

export type ToastTipo = 'exito' | 'error' | 'info' | 'advertencia';

export interface Toast {
  id: number;
  tipo: ToastTipo;
  titulo?: string;
  mensaje: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private nextId = 1;

  exito(mensaje: string, titulo = 'Listo') {
    this.mostrar('exito', mensaje, titulo);
  }

  error(mensaje: string, titulo = 'Ups') {
    this.mostrar('error', mensaje, titulo, 6000);
  }

  info(mensaje: string, titulo?: string) {
    this.mostrar('info', mensaje, titulo);
  }

  advertencia(mensaje: string, titulo = 'Atención') {
    this.mostrar('advertencia', mensaje, titulo, 6000);
  }

  /** Para errores HTTP: si el backend respondió con un mensaje de negocio (validación 400/404/409)
   * se muestra como advertencia — es una regla del negocio, no una falla del sistema. El estilo
   * de error (✕ rojo) queda solo para fallas reales: sin conexión, 500, etc. */
  desdeHttp(err: { status?: number; error?: { mensaje?: string } }, fallback: string) {
    const mensajeNegocio = err?.error?.mensaje;
    if (mensajeNegocio && (err.status === 400 || err.status === 403 || err.status === 404 || err.status === 409)) {
      this.advertencia(mensajeNegocio);
    } else {
      this.error(mensajeNegocio ?? fallback);
    }
  }

  cerrar(id: number) {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }

  private mostrar(tipo: ToastTipo, mensaje: string, titulo?: string, duracionMs = 3800) {
    if (this.toasts().some(t => t.tipo === tipo && t.mensaje === mensaje)) return;
    const id = this.nextId++;
    this.toasts.update(list => [...list, { id, tipo, titulo, mensaje }]);
    setTimeout(() => this.cerrar(id), duracionMs);
  }
}
