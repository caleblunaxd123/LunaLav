import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { environment } from '../../../environments/environment';

/**
 * Detecta cuando el sistema fue actualizado en el servidor (nuevo despliegue).
 * Consulta /api/version cada cierto tiempo; si la marca cambió respecto a la que
 * se cargó al abrir la app, avisa (para mostrar "hay una nueva versión, recargar").
 */
@Injectable({ providedIn: 'root' })
export class VersionCheckService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/version`;
  private base: string | null = null;
  private timer: any = null;

  /** true cuando el servidor tiene una versión distinta a la que cargó esta pestaña. */
  readonly hayNuevaVersion = signal(false);

  iniciar(intervaloMs = 120000) {
    if (this.timer) return;
    // Marca de referencia al abrir.
    this.leer().then(v => { this.base = v; });
    this.timer = setInterval(() => this.revisar(), intervaloMs);
    // También al volver a enfocar la pestaña (por si estuvo en segundo plano).
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') this.revisar();
    });
  }

  private async revisar() {
    if (this.hayNuevaVersion()) return; // ya avisamos
    const v = await this.leer();
    if (v && this.base && v !== this.base) this.hayNuevaVersion.set(true);
    else if (v && !this.base) this.base = v;
  }

  private async leer(): Promise<string | null> {
    try {
      const r = await fetch(this.url, { cache: 'no-store' });
      if (!r.ok) return null;
      const j = await r.json();
      return j?.version ?? null;
    } catch {
      return null;
    }
  }

  recargar() {
    // Recarga forzada para traer los nuevos archivos.
    location.reload();
  }
}
