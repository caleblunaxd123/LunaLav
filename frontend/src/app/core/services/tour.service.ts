import { Injectable, computed, signal } from '@angular/core';

/** Un paso del tour guiado. Si `ancla` apunta a un [data-tour="..."] existente,
 *  el overlay lo resalta; si no, el paso se muestra centrado. */
export interface PasoTour {
  ancla?: string;
  titulo: string;
  texto: string;
}

/**
 * Motor del tour guiado por módulo. Un módulo llama `iniciar(pasos)` (normalmente
 * desde el botón "?" del encabezado) y el <app-tour-overlay> global se encarga de
 * resaltar cada sección y mostrar el texto, con Anterior / Siguiente / Saltar.
 */
@Injectable({ providedIn: 'root' })
export class TourService {
  private readonly _pasos = signal<PasoTour[]>([]);
  private readonly _indice = signal(0);

  readonly pasos = this._pasos.asReadonly();
  readonly indice = this._indice.asReadonly();
  readonly activo = computed(() => this._pasos().length > 0);
  readonly total = computed(() => this._pasos().length);
  readonly pasoActual = computed(() => this._pasos()[this._indice()] ?? null);
  readonly esUltimo = computed(() => this._indice() >= this._pasos().length - 1);
  readonly esPrimero = computed(() => this._indice() === 0);

  iniciar(pasos: PasoTour[]) {
    if (!pasos?.length) return;
    this._pasos.set(pasos);
    this._indice.set(0);
  }

  siguiente() {
    if (this.esUltimo()) { this.cerrar(); return; }
    this._indice.update(i => i + 1);
  }

  anterior() {
    if (!this.esPrimero()) this._indice.update(i => i - 1);
  }

  irA(indice: number) {
    if (indice >= 0 && indice < this._pasos().length) this._indice.set(indice);
  }

  cerrar() {
    this._pasos.set([]);
    this._indice.set(0);
  }
}
