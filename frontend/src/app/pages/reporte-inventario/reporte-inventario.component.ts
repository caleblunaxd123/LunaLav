import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { InsumosService, Insumo, MovimientoInsumo } from '../../core/services/insumos.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { fechaLocalIso } from '../../core/util/fecha-local';

interface DiaSerie { fecha: string; consumo: number; compra: number; }

interface BarraVM {
  fecha: string;
  consumoX: number; consumoY: number; consumoH: number; consumo: number;
  compraX: number; compraY: number; compraH: number; compra: number;
  cx: number; etiqueta: string; mostrarEtiqueta: boolean;
}
interface TickVM { y: number; valor: string; }
interface GraficoVM {
  W: number; H: number; baseY: number; mL: number; plotDcho: number; barW: number;
  barras: BarraVM[]; ticks: TickVM[]; maxY: number; hayDatos: boolean;
}

/** Redondea un máximo a un valor "bonito" para la escala del eje Y. */
function maximoBonito(v: number): number {
  if (v <= 0) return 1;
  const pot = Math.pow(10, Math.floor(Math.log10(v)));
  const n = v / pot;
  const paso = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10;
  return paso * pot;
}

/**
 * Reporte de inventario por insumo: consumo y compras por día, con filtro de fechas,
 * gráfico con ejes X/Y, tarjetas resumen y tabla de movimientos del periodo.
 */
@Component({
  selector: 'app-reporte-inventario',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent],
  templateUrl: './reporte-inventario.component.html',
  styleUrl: './reporte-inventario.component.scss'
})
export class ReporteInventarioComponent implements OnInit {
  private readonly svc = inject(InsumosService);
  private readonly router = inject(Router);

  readonly insumos = signal<Insumo[]>([]);
  readonly insumoId = signal<number | null>(null);
  readonly movimientos = signal<MovimientoInsumo[]>([]);
  readonly cargando = signal(false);
  readonly hoy = fechaLocalIso(new Date());
  /** Rango rápido activo (para resaltar el chip elegido); null = fechas personalizadas. */
  readonly rangoActivo = signal<number | 'mes' | 'm6' | 'm12' | null>(30);

  private readonly mesesAbrev = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];

  desde = '';
  hasta = '';

  readonly insumoSel = computed(() => this.insumos().find(i => i.id === this.insumoId()) ?? null);
  readonly unidad = computed(() => this.insumoSel()?.unidadMedida ?? 'und');

  ngOnInit(): void {
    const hoy = new Date();
    const inicio = new Date();
    inicio.setDate(hoy.getDate() - 29);          // por defecto, últimos 30 días
    this.hasta = fechaLocalIso(hoy);
    this.desde = fechaLocalIso(inicio);

    this.svc.listar().subscribe(list => {
      this.insumos.set(list);
      if (list.length) {
        this.insumoId.set(list[0].id);
        this.cargar();
      }
    });
  }

  volver(): void { this.router.navigate(['/inventario']); }

  cambiarInsumo(id: number): void { this.insumoId.set(Number(id)); this.cargar(); }

  /** Rango rápido: últimos N días (incluye hoy). */
  setRangoDias(dias: number): void {
    const fin = new Date();
    const ini = new Date();
    ini.setDate(fin.getDate() - (dias - 1));
    this.desde = fechaLocalIso(ini);
    this.hasta = fechaLocalIso(fin);
    this.rangoActivo.set(dias);
    this.cargar();
  }

  /** Rango rápido: del día 1 del mes actual hasta hoy. */
  setEsteMes(): void {
    const hoy = new Date();
    this.desde = fechaLocalIso(new Date(hoy.getFullYear(), hoy.getMonth(), 1));
    this.hasta = fechaLocalIso(hoy);
    this.rangoActivo.set('mes');
    this.cargar();
  }

  /** Rango rápido: últimos N meses (el gráfico se agrupa por mes automáticamente). */
  setRangoMeses(meses: number): void {
    const fin = new Date();
    const ini = new Date(fin.getFullYear(), fin.getMonth() - (meses - 1), 1);
    this.desde = fechaLocalIso(ini);
    this.hasta = fechaLocalIso(fin);
    this.rangoActivo.set(meses === 6 ? 'm6' : 'm12');
    this.cargar();
  }

  /** Al cambiar una fecha a mano, el rango pasa a "personalizado". */
  fechaManual(): void { this.rangoActivo.set(null); this.cargar(); }

  /** Abre el calendario nativo al tocar cualquier parte del campo (cómodo en celular). */
  abrirPicker(ev: Event): void {
    const el = ev.target as HTMLInputElement & { showPicker?: () => void };
    try { el.showPicker?.(); } catch { /* algunos navegadores lo bloquean; el ícono sigue funcionando */ }
  }

  cargar(): void {
    const id = this.insumoId();
    if (!id || !this.desde || !this.hasta) return;
    if (this.desde > this.hasta) { const t = this.desde; this.desde = this.hasta; this.hasta = t; }
    this.cargando.set(true);
    this.svc.movimientos(id, this.desde, this.hasta).subscribe({
      next: m => { this.movimientos.set(m); this.cargando.set(false); },
      error: () => { this.movimientos.set([]); this.cargando.set(false); }
    });
  }

  /** Serie por día en el rango elegido (rellena con 0 los días sin movimiento). */
  readonly serie = computed<DiaSerie[]>(() => {
    if (!this.desde || !this.hasta) return [];
    const dias: DiaSerie[] = [];
    const mapa = new Map<string, DiaSerie>();
    const fin = new Date(this.hasta + 'T00:00:00');
    for (let dt = new Date(this.desde + 'T00:00:00'); dt <= fin; dt.setDate(dt.getDate() + 1)) {
      const clave = fechaLocalIso(dt);
      const o: DiaSerie = { fecha: clave, consumo: 0, compra: 0 };
      mapa.set(clave, o);
      dias.push(o);
    }
    for (const mv of this.movimientos()) {
      const clave = (mv.fecha ?? '').slice(0, 10);
      const o = mapa.get(clave);
      if (!o) continue;
      if (mv.tipo === 'CONSUMO') o.consumo += mv.cantidad;
      else if (mv.tipo === 'COMPRA') o.compra += mv.cantidad;
    }
    return dias;
  });

  /** Para rangos largos (más de ~3 meses) el gráfico se agrupa por mes en vez de por día. */
  readonly agrupadoPorMes = computed(() => this.serie().length > 92);

  /** Serie lista para graficar: por día, o agrupada por mes si el rango es largo. */
  readonly serieGrafico = computed<{ clave: string; consumo: number; compra: number; etiqueta: string }[]>(() => {
    const dias = this.serie();
    if (!this.agrupadoPorMes()) {
      return dias.map(d => ({ clave: d.fecha, consumo: d.consumo, compra: d.compra, etiqueta: `${d.fecha.slice(8, 10)}/${d.fecha.slice(5, 7)}` }));
    }
    const mapa = new Map<string, { clave: string; consumo: number; compra: number; etiqueta: string }>();
    const orden: { clave: string; consumo: number; compra: number; etiqueta: string }[] = [];
    for (const d of dias) {
      const ym = d.fecha.slice(0, 7);
      let b = mapa.get(ym);
      if (!b) {
        const mesIdx = Number(d.fecha.slice(5, 7)) - 1;
        b = { clave: ym, consumo: 0, compra: 0, etiqueta: `${this.mesesAbrev[mesIdx]} ${d.fecha.slice(2, 4)}` };
        mapa.set(ym, b);
        orden.push(b);
      }
      b.consumo += d.consumo;
      b.compra += d.compra;
    }
    return orden;
  });

  readonly totalConsumo = computed(() => this.serie().reduce((s, d) => s + d.consumo, 0));
  readonly totalCompra = computed(() => this.serie().reduce((s, d) => s + d.compra, 0));
  readonly promedioConsumo = computed(() => {
    const s = this.serie();
    return s.length ? this.totalConsumo() / s.length : 0;
  });
  /** Movimientos del periodo ordenados del más reciente al más antiguo (para la tabla). */
  readonly movimientosOrdenados = computed(() =>
    [...this.movimientos()].sort((a, b) => (b.fecha ?? '').localeCompare(a.fecha ?? '')));

  /** Geometría del gráfico de barras con ejes (consumo vs. compra, por día o por mes). */
  readonly grafico = computed<GraficoVM>(() => {
    const serie = this.serieGrafico();
    const porMes = this.agrupadoPorMes();
    const W = 920, H = 340, mL = 52, mR = 14, mT = 16, mB = 40;
    const plotW = W - mL - mR;
    const plotH = H - mT - mB;
    const baseY = mT + plotH;
    const n = Math.max(1, serie.length);

    const maxDato = Math.max(0, ...serie.map(d => Math.max(d.consumo, d.compra)));
    const maxY = maximoBonito(maxDato);

    const slot = plotW / n;
    const barW = Math.min(slot * 0.34, porMes ? 20 : 13);
    const pasoEtiqueta = porMes ? 1 : Math.max(1, Math.ceil(n / 8));   // por mes: todas las etiquetas

    const barras: BarraVM[] = serie.map((d, i) => {
      const cx = mL + slot * i + slot / 2;
      const consumoH = maxY > 0 ? (d.consumo / maxY) * plotH : 0;
      const compraH = maxY > 0 ? (d.compra / maxY) * plotH : 0;
      return {
        fecha: d.clave,
        consumo: d.consumo, compra: d.compra,
        consumoX: cx - barW - 1, consumoH, consumoY: baseY - consumoH,
        compraX: cx + 1, compraH, compraY: baseY - compraH,
        cx, etiqueta: d.etiqueta,
        // Etiquetas espaciadas + siempre la última; se ocultan las regulares que quedarían
        // pegadas a la última para que no se encimen.
        mostrarEtiqueta: i === n - 1 || (i % pasoEtiqueta === 0 && (n - 1 - i) >= pasoEtiqueta)
      };
    });

    const ticks: TickVM[] = [0, 0.25, 0.5, 0.75, 1].map(f => ({
      y: baseY - f * plotH,
      valor: this.formatoNum(maxY * f)
    }));

    return { W, H, baseY, mL, plotDcho: W - mR, barW, barras, ticks, maxY, hayDatos: maxDato > 0 };
  });

  /** Formato compacto de cantidad (sin decimales innecesarios). */
  formatoNum(v: number): string {
    const r = Math.round(v * 1000) / 1000;
    return Number.isInteger(r) ? String(r) : r.toFixed(r < 10 ? 2 : 1);
  }
}
