import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'app-paginacion',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './paginacion.component.html',
  styleUrl: './paginacion.component.scss'
})
export class PaginacionComponent {
  @Input() set pagina(v: number) { this._pagina.set(v); }
  get pagina() { return this._pagina(); }

  @Input() set tamanoPagina(v: number) { this._tamanoPagina.set(v); }
  get tamanoPagina() { return this._tamanoPagina(); }

  @Input() set total(v: number) { this._total.set(v); }
  get total() { return this._total(); }

  @Input() opcionesTamano: number[] = [10, 15, 25, 50];

  @Output() paginaChange = new EventEmitter<number>();
  @Output() tamanoPaginaChange = new EventEmitter<number>();

  private readonly _pagina = signal(1);
  private readonly _tamanoPagina = signal(15);
  private readonly _total = signal(0);

  readonly totalPaginas = computed(() => Math.max(1, Math.ceil(this._total() / this._tamanoPagina())));
  readonly desde = computed(() => this._total() === 0 ? 0 : (this._pagina() - 1) * this._tamanoPagina() + 1);
  readonly hasta = computed(() => Math.min(this._total(), this._pagina() * this._tamanoPagina()));

  irA(p: number) {
    const destino = Math.min(Math.max(1, p), this.totalPaginas());
    if (destino === this._pagina()) return;
    this._pagina.set(destino);
    this.paginaChange.emit(destino);
  }

  cambiarTamano(valor: string) {
    const t = Number(valor);
    this._tamanoPagina.set(t);
    this.tamanoPaginaChange.emit(t);
    this.irA(1);
  }
}
