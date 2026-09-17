import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { fechaLocalIso } from '../../core/util/fecha-local';
import { Router } from '@angular/router';
import { ReportesService, TableroSla, VistaGerencial } from '../../core/services/reportes.service';
import { formatearDuracion } from '../../core/util/duracion';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface BarraDia { fecha: string; total: number; etiqueta: string; alturaPct: number; esHoy: boolean; }

@Component({
  selector: 'app-vista-gerencial',
  imports: [PageHeaderComponent, CommonModule],
  templateUrl: './vista-gerencial.component.html',
  styleUrl: './vista-gerencial.component.scss'
})
export class VistaGerencialComponent implements OnInit {
  private readonly svc = inject(ReportesService);
  private readonly router = inject(Router);

  readonly cargando = signal(true);
  readonly kpis = signal<VistaGerencial | null>(null);
  readonly sla = signal<TableroSla | null>(null);

  // --- Crecimiento del mes vs mes anterior (comparación justa: hasta el mismo día) ---
  readonly crecimientoPct = computed(() => {
    const k = this.kpis(); if (!k) return null;
    const base = k.ventasMesAnteriorAlDia;
    if (base <= 0) return k.ventasMes > 0 ? 100 : 0;
    return Math.round(((k.ventasMes - base) / base) * 100);
  });

  // --- Margen de utilidad del mes (%) ---
  readonly margenPct = computed(() => {
    const k = this.kpis(); if (!k || k.ventasMes <= 0) return 0;
    return Math.round((k.utilidadMes / k.ventasMes) * 100);
  });

  // --- % de lo facturado en el mes que ya se cobró en caja (cobrado del mes vs vendido del mes) ---
  readonly cobranzaPct = computed(() => {
    const k = this.kpis(); if (!k || k.ventasMes <= 0) return 100;
    const cobradoMes = k.ingresosEfectivoMes + k.ingresosDigitalMes + k.ingresosTarjetaMes;
    return Math.min(100, Math.round((cobradoMes / k.ventasMes) * 100));
  });

  // --- Tendencia de ventas: barras normalizadas al máximo de la serie ---
  readonly barras = computed<BarraDia[]>(() => {
    const k = this.kpis(); if (!k) return [];
    const serie = k.ventasUltimos14Dias ?? [];
    const max = Math.max(1, ...serie.map(p => p.total));
    const hoyIso = fechaLocalIso();
    return serie.map(p => ({
      fecha: p.fecha,
      total: p.total,
      etiqueta: this.etiquetaDia(p.fecha),
      alturaPct: Math.round((p.total / max) * 100),
      esHoy: p.fecha === hoyIso
    }));
  });

  readonly ventaMaxTendencia = computed(() =>
    Math.max(0, ...(this.kpis()?.ventasUltimos14Dias ?? []).map(p => p.total)));

  readonly promedioDiario14 = computed(() => {
    const serie = this.kpis()?.ventasUltimos14Dias ?? [];
    if (!serie.length) return 0;
    return serie.reduce((a, p) => a + p.total, 0) / serie.length;
  });

  // --- Composición de ingresos por método (efectivo / digital / tarjeta) ---
  readonly totalIngresosMes = computed(() => {
    const k = this.kpis(); if (!k) return 0;
    return k.ingresosEfectivoMes + k.ingresosDigitalMes + k.ingresosTarjetaMes;
  });
  pctMetodo(monto: number): number {
    const t = this.totalIngresosMes();
    return t > 0 ? Math.round((monto / t) * 100) : 0;
  }

  // --- Embudo de pedidos por estado ---
  readonly totalEmbudo = computed(() => {
    const k = this.kpis(); if (!k) return 0;
    return k.pedidosPendientes + k.pedidosEnProceso + k.pedidosListosSinRecoger;
  });

  // Máximo de facturación entre el top de servicios (para escalar las barras)
  readonly maxTopServicio = computed(() =>
    Math.max(1, ...(this.kpis()?.topServiciosMes ?? []).map(s => s.total)));

  ngOnInit() {
    this.cargando.set(true);
    this.svc.vistaGerencial().subscribe({
      next: k => { this.kpis.set(k); this.cargando.set(false); },
      error: () => this.cargando.set(false)
    });
    this.svc.sla().subscribe({ next: s => this.sla.set(s), error: () => {} });
  }

  volver() { this.router.navigate(['/reportes']); }

  /** "lun 4", "mar 5"… para el eje de la tendencia. */
  private etiquetaDia(iso: string): string {
    const d = new Date(iso + 'T00:00:00');
    const dia = ['dom', 'lun', 'mar', 'mié', 'jue', 'vie', 'sáb'][d.getDay()];
    return `${dia} ${d.getDate()}`;
  }

  porcentajeDesvio(area: { tiempoEstMinutos: number; minutosPromedioReal: number }): number {
    if (area.tiempoEstMinutos <= 0) return 0;
    return Math.round(((area.minutosPromedioReal - area.tiempoEstMinutos) / area.tiempoEstMinutos) * 100);
  }

  /** Duración legible: "45 min", "6 h", "3 días"… (en vez de "1224 min"). */
  duracion(minutos: number): string { return formatearDuracion(minutos); }

  /** Etiqueta cualitativa del desvío vs meta (evita números absurdos como "+8058%"). */
  desvioEtiqueta(area: { tiempoEstMinutos: number; minutosPromedioReal: number }): string {
    const d = this.porcentajeDesvio(area);
    if (Math.abs(d) <= 20) return 'En meta';
    if (d > 0) return d > 300 ? 'Muy por encima' : `+${d}% sobre meta`;
    return d < -300 ? 'Muy por debajo' : `${d}% bajo meta`;
  }

  /** 'mal' = tarda de más, 'bajo' = mucho más rápido de lo esperado, 'ok' = en meta. */
  desvioClase(area: { tiempoEstMinutos: number; minutosPromedioReal: number }): 'mal' | 'bajo' | 'ok' {
    const d = this.porcentajeDesvio(area);
    return d > 20 ? 'mal' : (d < -20 ? 'bajo' : 'ok');
  }
}
