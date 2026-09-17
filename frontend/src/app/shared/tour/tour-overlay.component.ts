import { CommonModule } from '@angular/common';
import { Component, HostListener, effect, inject, signal } from '@angular/core';
import { TourService } from '../../core/services/tour.service';
import { IconComponent } from '../icon/icon.component';

interface Recuadro { top: number; left: number; width: number; height: number; }

/**
 * Overlay del tour guiado. Se monta una sola vez (en la raíz de la app) y reacciona
 * al TourService: oscurece la pantalla, recorta un "foco" sobre la sección del paso
 * actual y muestra una tarjeta con el texto y los controles.
 */
@Component({
  selector: 'app-tour-overlay',
  standalone: true,
  imports: [CommonModule, IconComponent],
  templateUrl: './tour-overlay.component.html',
  styleUrl: './tour-overlay.component.scss'
})
export class TourOverlayComponent {
  readonly tour = inject(TourService);

  /** Rectángulo del elemento resaltado; null = paso centrado sin ancla. */
  readonly foco = signal<Recuadro | null>(null);

  constructor() {
    // Cada vez que cambia el paso activo, ubica y resalta su ancla.
    effect(() => {
      const paso = this.tour.pasoActual();
      if (!paso) { this.foco.set(null); return; }
      // Espera un tick para que el DOM del paso esté visible antes de medir.
      queueMicrotask(() => this.ubicarAncla(paso.ancla));
    });
  }

  private ubicarAncla(ancla?: string) {
    if (!ancla) { this.foco.set(null); return; }
    const el = document.querySelector<HTMLElement>(`[data-tour="${ancla}"]`);
    if (!el) { this.foco.set(null); return; }
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    // Deja que el scroll se asiente y luego mide.
    setTimeout(() => this.medir(el), 260);
  }

  private medir(el: HTMLElement) {
    const r = el.getBoundingClientRect();
    const pad = 8;
    this.foco.set({
      top: r.top - pad,
      left: r.left - pad,
      width: r.width + pad * 2,
      height: r.height + pad * 2
    });
  }

  @HostListener('window:resize')
  @HostListener('window:scroll')
  reubicar() {
    const paso = this.tour.pasoActual();
    if (!paso?.ancla) return;
    const el = document.querySelector<HTMLElement>(`[data-tour="${paso.ancla}"]`);
    if (el) this.medir(el);
  }

  @HostListener('document:keydown', ['$event'])
  teclas(e: KeyboardEvent) {
    if (!this.tour.activo()) return;
    if (e.key === 'Escape') this.tour.cerrar();
    else if (e.key === 'ArrowRight' || e.key === 'Enter') this.tour.siguiente();
    else if (e.key === 'ArrowLeft') this.tour.anterior();
  }

  /** Posición de la tarjeta: debajo del foco si hay espacio, si no encima; centrada si no hay ancla. */
  posicionTarjeta(): { [k: string]: string } {
    const f = this.foco();
    if (!f) return { top: '50%', left: '50%', transform: 'translate(-50%, -50%)' };
    const alturaAprox = 190;
    const debajo = f.top + f.height + alturaAprox < window.innerHeight;
    const left = Math.min(Math.max(16, f.left), window.innerWidth - 360 - 16);
    return debajo
      ? { top: `${f.top + f.height + 14}px`, left: `${left}px` }
      : { top: `${Math.max(16, f.top - alturaAprox - 14)}px`, left: `${left}px` };
  }
}
