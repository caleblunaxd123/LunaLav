import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ReporteKey, ReporteResult, ReportesService } from '../../core/services/reportes.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { MiniBarrasComponent, PuntoBarra } from '../../shared/mini-barras/mini-barras.component';

interface ReporteMeta {
  titulo: string;
  descripcion: string;
  usaRango: boolean;
}

/** Config de la gráfica de barras de cada reporte, derivada de las mismas columnas
 *  que ya trae la tabla. modo: 'directo' = una barra por fila; 'suma'/'conteo' = agrupa por eje. */
interface GraficaCfg {
  titulo: string;
  eje: string;            // columna que va en el eje X (etiqueta)
  valor?: string;         // columna numérica (no aplica a 'conteo')
  modo: 'directo' | 'suma' | 'conteo';
  color: 'azul' | 'verde' | 'ambar';
  reverse?: boolean;      // invertir orden (reportes por fecha vienen DESC)
  max?: number;           // tope de barras (deja las de mayor valor)
}

const REPORTES: Record<ReporteKey, ReporteMeta> = {
  'ordenes-pendientes': { titulo: 'Órdenes Pendientes', descripcion: 'Pedidos en proceso ahora mismo, con los días que llevan sin terminar.', usaRango: false },
  'gastos': { titulo: 'Gastos', descripcion: 'Gastos agrupados por tipo en el rango de fechas.', usaRango: true },
  'general': { titulo: 'General', descripcion: 'Ingresos, gastos y utilidad neta por día.', usaRango: true },
  'servicios': { titulo: 'Servicios', descripcion: 'Qué servicios venden más y cuánto generan.', usaRango: true },
  'cuadres-caja': { titulo: 'Cuadres de Caja', descripcion: 'Historial de cuadres diarios guardados.', usaRango: true },
  'ordenes-mensual': { titulo: 'Órdenes Mensual', descripcion: 'Pedidos, montos facturados y pagados por mes.', usaRango: true },
  'almacen': { titulo: 'Almacén', descripcion: 'Pedidos listos sin recoger, con días en custodia.', usaRango: false },
  'anulados': { titulo: 'Anulados', descripcion: 'Pedidos anulados, responsable y motivo.', usaRango: true },
  'registro-entregas': { titulo: 'Registro y Entregas', descripcion: 'Quién registró y quién entregó cada pedido.', usaRango: true },
  'pagos': { titulo: 'Pagos', descripcion: 'Todos los pagos recibidos, método y responsable.', usaRango: true },
  'descuento-directo': { titulo: 'Descuento Directo', descripcion: 'Pedidos con descuento aplicado y quién lo hizo.', usaRango: true },
};

const GRAFICAS: Partial<Record<ReporteKey, GraficaCfg>> = {
  'general': { titulo: 'Ingresos por día', eje: 'Día', valor: 'Ingresos', modo: 'directo', color: 'azul', reverse: true, max: 31 },
  'gastos': { titulo: 'Gasto por tipo', eje: 'Tipo de gasto', valor: 'Monto total', modo: 'directo', color: 'ambar' },
  'servicios': { titulo: 'Top servicios por ingreso', eje: 'Servicio', valor: 'Ingreso generado', modo: 'directo', color: 'azul', max: 12 },
  'cuadres-caja': { titulo: 'Total contado por día', eje: 'Fecha', valor: 'Total contado', modo: 'directo', color: 'verde', reverse: true, max: 31 },
  'ordenes-mensual': { titulo: 'Facturado por mes', eje: 'Mes', valor: 'Total facturado', modo: 'directo', color: 'azul', reverse: true },
  'pagos': { titulo: 'Cobros por método', eje: 'Método de pago', valor: 'Monto', modo: 'suma', color: 'verde' },
  'ordenes-pendientes': { titulo: 'Pedidos por área', eje: 'Área actual', modo: 'conteo', color: 'azul' },
  'anulados': { titulo: 'Anulados por responsable', eje: 'Responsable', modo: 'conteo', color: 'ambar' },
  'registro-entregas': { titulo: 'Movimientos por responsable', eje: 'Responsable', modo: 'conteo', color: 'azul' },
  'descuento-directo': { titulo: 'Descuento por responsable', eje: 'Responsable', valor: 'Descuento', modo: 'suma', color: 'ambar' },
  'almacen': { titulo: 'Días en custodia por pedido', eje: 'N°', valor: 'Días en custodia', modo: 'directo', color: 'ambar', max: 15 },
};

@Component({
  selector: 'app-reporte-detalle',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent, MiniBarrasComponent],
  templateUrl: './reporte-detalle.component.html',
  styleUrl: './reporte-detalle.component.scss'
})
export class ReporteDetalleComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly svc = inject(ReportesService);
  private readonly toast = inject(ToastService);

  clave: ReporteKey | null = null;
  meta: ReporteMeta | null = null;

  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly resultado = signal<ReporteResult | null>(null);

  desde = this.formatoLocal(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000));
  hasta = this.formatoLocal(new Date());

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly filasPaginadas = computed(() => {
    const filas = this.resultado()?.filas ?? [];
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return filas.slice(inicio, inicio + this.tamanoPagina());
  });

  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  // --- Gráfica de barras derivada de la tabla ---
  readonly graficaCfg = computed<GraficaCfg | null>(() => (this.clave ? GRAFICAS[this.clave] ?? null : null));
  readonly datosGrafica = computed<PuntoBarra[]>(() => {
    const cfg = this.graficaCfg();
    const r = this.resultado();
    if (!cfg || !r || r.filas.length === 0) return [];
    const num = (s: string | undefined) => {
      if (!s) return 0;
      const n = parseFloat(s.replace(/[^0-9.-]/g, ''));
      return isNaN(n) ? 0 : n;
    };
    let puntos: PuntoBarra[];
    if (cfg.modo === 'directo') {
      puntos = r.filas.map(f => ({ etiqueta: (f[cfg.eje] ?? '—').toString().slice(0, 12), valor: num(f[cfg.valor!]) }));
      if (cfg.reverse) puntos = puntos.slice().reverse();
    } else {
      const mapa = new Map<string, number>();
      for (const f of r.filas) {
        const k = (f[cfg.eje] ?? '—').toString().slice(0, 12);
        const v = cfg.modo === 'conteo' ? 1 : num(f[cfg.valor!]);
        mapa.set(k, (mapa.get(k) ?? 0) + v);
      }
      puntos = [...mapa.entries()].map(([etiqueta, valor]) => ({ etiqueta, valor: Math.round(valor) }));
    }
    if (cfg.max && puntos.length > cfg.max) {
      puntos = puntos.slice().sort((a, b) => b.valor - a.valor).slice(0, cfg.max);
      if (cfg.reverse) puntos = puntos.slice().reverse();
    }
    return puntos;
  });

  // Acción operativa por fila (donar / reenviar-almacen), si el reporte la ofrece.
  readonly accion = computed(() => this.resultado()?.accion ?? null);
  readonly accionLabel = computed(() => ({ 'donar': 'Donar', 'reenviar-almacen': 'A almacén' } as Record<string, string>)[this.accion() ?? ''] ?? '');
  readonly confirmarDonar = signal<{ id: number; texto: string } | null>(null);
  readonly ejecutandoAccion = signal(false);
  readonly exportando = signal(false);

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      const clave = params.get('key') as ReporteKey;
      if (!clave || !REPORTES[clave]) {
        this.router.navigate(['/reportes']);
        return;
      }
      this.clave = clave;
      this.meta = REPORTES[clave];
      this.cargar();
    });
  }

  private formatoLocal(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }

  cargar() {
    if (!this.clave || !this.meta) return;
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.obtener(this.clave, this.meta.usaRango ? this.desde : undefined, this.meta.usaRango ? this.hasta : undefined).subscribe({
      next: r => { this.resultado.set(r); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar el reporte.'));
      }
    });
  }

  exportarCsv() {
    const r = this.resultado();
    if (!r || r.filas.length === 0) {
      this.toast.advertencia('No hay datos para exportar.');
      return;
    }
    const filas = [
      r.columnas.join(','),
      ...r.filas.map(fila => r.columnas.map(c => `"${(fila[c] ?? '').replace(/"/g, '""')}"`).join(','))
    ];
    const blob = new Blob(['﻿' + filas.join('\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `reporte-${this.clave}-${this.desde || 'todo'}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  // --- Acciones operativas ---
  onAccion(fila: Record<string, string>) {
    const id = Number(fila['_id']);
    if (!id) return;
    if (this.accion() === 'donar') {
      this.confirmarDonar.set({ id, texto: fila['Cliente'] ? `${fila['N°']} · ${fila['Cliente']}` : fila['N°'] });
    } else if (this.accion() === 'reenviar-almacen') {
      this.reenviarAlmacen(id);
    }
  }

  confirmarDonarOk() {
    const c = this.confirmarDonar();
    if (!c) return;
    this.ejecutandoAccion.set(true);
    this.svc.donarPedido(c.id).subscribe({
      next: () => {
        this.ejecutandoAccion.set(false);
        this.confirmarDonar.set(null);
        this.toast.exito('Pedido enviado a donación.');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.ejecutandoAccion.set(false);
        this.toast.desdeHttp(err, 'No se pudo enviar a donación.');
      }
    });
  }

  private reenviarAlmacen(id: number) {
    this.ejecutandoAccion.set(true);
    this.svc.reenviarAlmacen(id).subscribe({
      next: () => {
        this.ejecutandoAccion.set(false);
        this.toast.exito('Pedido reenviado a almacén (listo para recojo).');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.ejecutandoAccion.set(false);
        this.toast.desdeHttp(err, 'No se pudo reenviar a almacén.');
      }
    });
  }

  exportarExcel() {
    if (!this.clave) return;
    const r = this.resultado();
    if (!r || r.filas.length === 0) { this.toast.advertencia('No hay datos para exportar.'); return; }
    this.exportando.set(true);
    this.svc.exportarExcel(this.clave, this.meta?.usaRango ? this.desde : undefined, this.meta?.usaRango ? this.hasta : undefined).subscribe({
      next: blob => {
        this.exportando.set(false);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `reporte-${this.clave}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => { this.exportando.set(false); this.toast.error('No se pudo exportar a Excel.'); }
    });
  }

  volver() { this.router.navigate(['/reportes']); }
}
