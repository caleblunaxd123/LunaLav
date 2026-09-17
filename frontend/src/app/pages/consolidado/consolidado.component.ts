import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ConsolidadoSede, ReportesService } from '../../core/services/reportes.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { MiniBarrasComponent, PuntoBarra } from '../../shared/mini-barras/mini-barras.component';

@Component({
  selector: 'app-consolidado',
  imports: [PageHeaderComponent, CommonModule, MiniBarrasComponent],
  templateUrl: './consolidado.component.html',
  styleUrl: './consolidado.component.scss'
})
export class ConsolidadoComponent implements OnInit {
  private readonly svc = inject(ReportesService);
  private readonly router = inject(Router);

  readonly cargando = signal(true);
  readonly error = signal<string | null>(null);
  readonly sedes = signal<ConsolidadoSede[]>([]);

  readonly totales = computed(() => this.sedes().reduce((acc, s) => ({
    ventasHoy: acc.ventasHoy + s.ventasHoy,
    ventasMes: acc.ventasMes + s.ventasMes,
    saldoPorCobrar: acc.saldoPorCobrar + s.saldoPorCobrar,
    pedidosActivos: acc.pedidosActivos + s.pedidosActivos,
    pedidosListos: acc.pedidosListos + s.pedidosListos,
  }), { ventasHoy: 0, ventasMes: 0, saldoPorCobrar: 0, pedidosActivos: 0, pedidosListos: 0 }));

  readonly ventasPorSede = computed<PuntoBarra[]>(() =>
    this.sedes().map(s => ({ etiqueta: s.sedeNombre, valor: Math.round(s.ventasMes) })));
  readonly saldoPorSede = computed<PuntoBarra[]>(() =>
    this.sedes().map(s => ({ etiqueta: s.sedeNombre, valor: Math.round(s.saldoPorCobrar) })));

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.svc.consolidado().subscribe({
      next: list => { this.sedes.set(list); this.cargando.set(false); },
      error: () => { this.error.set('No se pudo cargar el consolidado.'); this.cargando.set(false); }
    });
  }

  volver() { this.router.navigate(['/reportes']); }
}
