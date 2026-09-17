import { CommonModule } from '@angular/common';
import { Component, Input, computed, signal } from '@angular/core';

export interface PuntoBarra {
  etiqueta: string;
  valor: number;
}

/** Mini gráfico de barras verticales de tendencia. Resalta la última barra
 *  (hoy / mes actual) y rellena con 0 los periodos sin datos. */
@Component({
  selector: 'app-mini-barras',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './mini-barras.component.html',
  styleUrl: './mini-barras.component.scss'
})
export class MiniBarrasComponent {
  @Input() titulo = '';
  @Input() subtitulo?: string;
  @Input() color: 'azul' | 'verde' | 'ambar' = 'azul';
  @Input() cargando = false;
  @Input() icono?: string;

  private readonly _datos = signal<PuntoBarra[]>([]);
  @Input() set datos(v: PuntoBarra[] | null | undefined) { this._datos.set(v ?? []); }
  get datos() { return this._datos(); }

  readonly maximo = computed(() => Math.max(1, ...this._datos().map(p => p.valor)));
  readonly hayDatos = computed(() => this._datos().some(p => p.valor > 0));
  readonly total = computed(() => this._datos().reduce((s, p) => s + p.valor, 0));

  altura(valor: number): number {
    if (valor <= 0) return 0;
    // mínimo 6% para que un valor pequeño siga siendo visible
    return Math.max(6, Math.round((valor / this.maximo()) * 100));
  }
}
