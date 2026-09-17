import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CuadreDiarioDia, CuadreDiarioFila, CuadresDiariosReporte, ReportesService } from '../../core/services/reportes.service';
import { CajaService } from '../../core/services/caja.service';
import { MovimientoCaja } from '../../core/models/models';
import { mesLocalIso } from '../../core/util/fecha-local';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-reporte-cuadres-diarios',
  imports: [CommonModule, FormsModule, IconComponent, PageHeaderComponent],
  templateUrl: './reporte-cuadres-diarios.component.html',
  styleUrl: './reporte-cuadres-diarios.component.scss'
})
export class ReporteCuadresDiariosComponent implements OnInit {
  private readonly svc = inject(ReportesService);
  private readonly caja = inject(CajaService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  // Filtro por mes: input type="month" → 'YYYY-MM'
  mesFiltro = mesLocalIso();

  readonly data = signal<CuadresDiariosReporte | null>(null);
  readonly cargando = signal(false);
  readonly expandido = signal(false);

  // ===== Filtros =====
  readonly trabajador = signal('todos');
  readonly soloConDif = signal(false);

  readonly usuarios = computed(() => {
    const set = new Set<string>();
    for (const d of this.data()?.dias ?? []) for (const c of d.cuadres) set.add(c.usuarioNombre);
    return [...set].sort();
  });
  private pasaUsuario(nombre: string): boolean {
    return this.trabajador() === 'todos' || this.trabajador() === nombre;
  }

  // Días con dinero no cuadrado (sin cuadre guardado pero con movimientos).
  readonly noCuadrados = computed(() =>
    (this.data()?.dias ?? []).filter(d => d.sinInformacion && (d.noCuadradoIngresos > 0 || d.noCuadradoEgresos > 0))
  );

  // ===== Resumen del mes (tarjetas KPI para el dueño) — respeta filtro de trabajador =====
  readonly resumen = computed(() => {
    let efectivo = 0, digital = 0, tarjeta = 0, faltante = 0, sobrante = 0, cerrados = 0, cuadrados = 0;
    for (const d of this.data()?.dias ?? []) {
      for (const c of d.cuadres) {
        if (!this.pasaUsuario(c.usuarioNombre)) continue;
        efectivo += c.ingresosEfectivo; digital += c.ingresosDigital; tarjeta += c.ingresosTarjeta;
        cerrados++;
        if (c.estado === 'FALTA') faltante += c.margenError;
        else if (c.estado === 'SOBRA') sobrante += c.margenError;
        else cuadrados++;
      }
    }
    const pctCuadraron = cerrados > 0 ? Math.round((cuadrados / cerrados) * 100) : 0;
    return {
      efectivo, digital, tarjeta, faltante, sobrante,
      neto: sobrante - faltante, cerrados, cuadrados,
      conDiferencia: cerrados - cuadrados, sinCuadrar: this.noCuadrados().length, pctCuadraron,
    };
  });

  // ===== Resumen por TRABAJADOR (quién descuadra) =====
  readonly resumenTrab = computed(() => {
    const map = new Map<string, { usuario: string; ingresos: number; falto: number; sobro: number; descuadres: number; cierres: number }>();
    for (const d of this.data()?.dias ?? []) {
      for (const c of d.cuadres) {
        const cur = map.get(c.usuarioNombre) ?? { usuario: c.usuarioNombre, ingresos: 0, falto: 0, sobro: 0, descuadres: 0, cierres: 0 };
        cur.cierres++;
        cur.ingresos += c.ingresosEfectivo + c.ingresosDigital + c.ingresosTarjeta;
        if (c.estado === 'FALTA') { cur.falto += c.margenError; cur.descuadres++; }
        else if (c.estado === 'SOBRA') { cur.sobro += c.margenError; cur.descuadres++; }
        map.set(c.usuarioNombre, cur);
      }
    }
    return [...map.values()].sort((a, b) => (b.falto + b.sobro) - (a.falto + a.sobro));
  });

  // ===== Formas de pago del mes (cuántas operaciones y monto por método) =====
  readonly formasMes = computed(() => {
    const map = new Map<string, { metodo: string; cantidad: number; monto: number }>();
    for (const d of this.data()?.dias ?? []) {
      for (const f of d.formasPago ?? []) {
        const label = this.metodoLabel(f.metodo);
        const cur = map.get(label) ?? { metodo: label, cantidad: 0, monto: 0 };
        cur.cantidad += f.cantidad; cur.monto += f.monto;
        map.set(label, cur);
      }
    }
    const orden = ['Efectivo', 'Yape', 'Plin', 'Transferencia', 'Tarjeta'];
    return [...map.values()].sort((a, b) => orden.indexOf(a.metodo) - orden.indexOf(b.metodo));
  });
  readonly totalOperaciones = computed(() => this.formasMes().reduce((s, f) => s + f.cantidad, 0));

  metodoLabel(m: string): string {
    const u = (m || '').toUpperCase();
    if (u === 'EFECTIVO') return 'Efectivo';
    if (u === 'YAPE') return 'Yape';
    if (u === 'PLIN') return 'Plin';
    if (u === 'TRANSFERENCIA') return 'Transferencia';
    if (u === 'POS' || u === 'TARJETA') return 'Tarjeta';
    return m || '—';
  }
  metodoClase(label: string): string {
    return ({ Efectivo: 'm-efe', Yape: 'm-yape', Plin: 'm-plin', Transferencia: 'm-transf', Tarjeta: 'm-tar' } as Record<string, string>)[label] ?? 'm-otro';
  }

  // ===== Serie de DIFERENCIAS por día (dónde está el error) — respeta filtro =====
  readonly serieDif = computed(() => {
    const filas: { fecha: string; usuario: string; estado: string; valor: number }[] = [];
    for (const d of this.data()?.dias ?? []) {
      for (const c of d.cuadres) {
        if (!this.pasaUsuario(c.usuarioNombre)) continue;
        const valor = c.estado === 'FALTA' ? -c.margenError : (c.estado === 'SOBRA' ? c.margenError : 0);
        filas.push({ fecha: d.fecha, usuario: c.usuarioNombre, estado: c.estado, valor });
      }
    }
    return filas;
  });
  readonly maxDifAbs = computed(() => Math.max(1, ...this.serieDif().map(f => Math.abs(f.valor))));

  // ===== Serie de INGRESOS por día y método (desde formasPago: incluye días sin cuadre) =====
  readonly serieIng = computed(() => {
    const rows: { fecha: string; efectivo: number; digital: number; tarjeta: number; efeN: number; digN: number; tarN: number }[] = [];
    for (const d of this.data()?.dias ?? []) {
      let efectivo = 0, digital = 0, tarjeta = 0, efeN = 0, digN = 0, tarN = 0;
      const formas = d.formasPago ?? [];
      if (formas.length > 0) {
        for (const f of formas) {
          const label = this.metodoLabel(f.metodo);
          if (label === 'Efectivo') { efectivo += f.monto; efeN += f.cantidad; }
          else if (label === 'Tarjeta') { tarjeta += f.monto; tarN += f.cantidad; }
          else { digital += f.monto; digN += f.cantidad; }
        }
      } else {
        // Respaldo si el servidor aún no envía el desglose por método (backend previo):
        // usa los montos del cuadre para que la gráfica de ingresos siga funcionando.
        for (const c of d.cuadres) { efectivo += c.ingresosEfectivo; digital += c.ingresosDigital; tarjeta += c.ingresosTarjeta; }
      }
      if (efectivo + digital + tarjeta > 0) rows.push({ fecha: d.fecha, efectivo, digital, tarjeta, efeN, digN, tarN });
    }
    return rows;
  });
  readonly maxIng = computed(() => Math.max(1, ...this.serieIng().map(s => s.efectivo + s.digital + s.tarjeta)));

  // Días para la tabla: aplica filtro de trabajador y "solo con diferencia".
  readonly diasTabla = computed(() => {
    const out: { dia: CuadreDiarioDia; cuadres: CuadreDiarioFila[] }[] = [];
    for (const d of this.data()?.dias ?? []) {
      if (d.sinInformacion) {
        if (this.trabajador() === 'todos' && !this.soloConDif()) out.push({ dia: d, cuadres: [] });
        continue;
      }
      let cs = d.cuadres.filter(c => this.pasaUsuario(c.usuarioNombre));
      if (this.soloConDif()) cs = cs.filter(c => c.estado !== 'CUADRA');
      if (cs.length) out.push({ dia: d, cuadres: cs });
    }
    return out;
  });

  /** Porcentaje (0-100) de un valor respecto al máximo, para el ancho de las barras. */
  pct(valor: number, max: number): number { return Math.round((Math.abs(valor) / max) * 100); }

  /** Descarga el reporte del mes como CSV (Excel lo abre directo). */
  exportarCsv() {
    const rep = this.data();
    if (!rep) return;
    const filas: string[][] = [['Fecha', 'Trabajador', 'Estado', 'Caja inicial', 'Ingresos efectivo', 'Egresos', 'Contado', 'Corte', 'Caja final', 'Diferencia', 'Yape/Plin/Transf.', 'Tarjeta', 'Nota']];
    for (const d of rep.dias) {
      const fecha = d.fecha.slice(0, 10);
      if (d.sinInformacion) {
        if (d.noCuadradoIngresos > 0 || d.noCuadradoEgresos > 0)
          filas.push([fecha, '', 'SIN CUADRE', '', String(d.noCuadradoIngresos), String(d.noCuadradoEgresos), '', '', '', '', '', '', 'Movimientos sin cuadrar']);
        continue;
      }
      for (const c of d.cuadres) {
        const dif = c.estado === 'FALTA' ? -c.margenError : (c.estado === 'SOBRA' ? c.margenError : 0);
        filas.push([fecha, c.usuarioNombre, c.estado, String(c.cajaInicial), String(c.ingresosEfectivo), String(c.egresos),
          String(c.montoEnCaja), String(c.corte), String(c.cajaFinal), String(dif), String(c.ingresosDigital), String(c.ingresosTarjeta), (c.nota ?? '').replace(/[\r\n;]+/g, ' ')]);
      }
    }
    const csv = '﻿' + filas.map(f => f.map(v => `"${(v ?? '').replace(/"/g, '""')}"`).join(';')).join('\r\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = `cuadres-${rep.anio}-${String(rep.mes).padStart(2, '0')}.csv`;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  ngOnInit() { this.cargar(); }

  cargar() {
    const [a, m] = this.mesFiltro.split('-').map(Number);
    if (!a || !m) return;
    this.cargando.set(true);
    this.svc.cuadresDiarios(a, m).subscribe({
      next: d => { this.data.set(d); this.cargando.set(false); },
      error: () => { this.cargando.set(false); this.toast.error('No se pudo cargar el reporte.'); }
    });
  }

  toggleExpandido() { this.expandido.set(!this.expandido()); }

  volver() { this.router.navigate(['/reportes']); }

  // ===== Drill-down "¿dónde está el error?" =====
  /** id del cuadre cuya fila está desplegada (una a la vez). */
  readonly filaAbierta = signal<number | null>(null);
  /** Cache de movimientos por cuadre (id → movimientos del día de ese cajero). */
  private readonly movsPorFila = signal<Record<number, MovimientoCaja[]>>({});
  /** id del cuadre cuyos movimientos se están cargando. */
  readonly cargandoMovs = signal<number | null>(null);

  /** Efectivo que DEBÍA haber en caja = inicial + ingresos efectivo − egresos. */
  esperadoCaja(c: CuadreDiarioFila): number {
    return Math.round((c.cajaInicial + c.ingresosEfectivo - c.egresos) * 100) / 100;
  }
  /** Diferencia con signo: contado − esperado (negativa = falta, positiva = sobra). */
  diferenciaFirmada(c: CuadreDiarioFila): number {
    return Math.round((c.montoEnCaja - this.esperadoCaja(c)) * 100) / 100;
  }

  /** Abre/cierra el detalle de una fila; al abrir, carga los movimientos del día de ese cajero. */
  toggleDetalle(dia: CuadreDiarioDia, c: CuadreDiarioFila) {
    if (this.filaAbierta() === c.id) { this.filaAbierta.set(null); return; }
    this.filaAbierta.set(c.id);
    if (this.movsPorFila()[c.id]) return; // ya cacheado
    this.cargandoMovs.set(c.id);
    this.caja.movimientos(dia.fecha.slice(0, 10), c.usuarioId).subscribe({
      next: ms => { this.movsPorFila.update(m => ({ ...m, [c.id]: ms })); this.cargandoMovs.set(null); },
      error: () => { this.movsPorFila.update(m => ({ ...m, [c.id]: [] })); this.cargandoMovs.set(null); }
    });
  }

  /** Todos los movimientos del día de ese cajero. */
  movsDelDia(id: number): MovimientoCaja[] { return this.movsPorFila()[id] ?? []; }

  /** Movimientos que afectan el efectivo contado: cobros en efectivo y egresos (gastos). */
  movsEfectivo(id: number): MovimientoCaja[] {
    return this.movsDelDia(id).filter(
      m => (m.tipo === 'INGRESO' && (m.metodoPago || '').toUpperCase() === 'EFECTIVO') || m.tipo === 'GASTO'
    );
  }
  /** Cobros digitales (Yape/Plin/Transf/POS): no afectan el efectivo, pero un pago mal clasificado sí descuadra. */
  movsDigitales(id: number): MovimientoCaja[] {
    return this.movsDelDia(id).filter(m => m.tipo === 'INGRESO' && (m.metodoPago || '').toUpperCase() !== 'EFECTIVO');
  }
  /** ¿Ya se cargaron (aunque sea vacío) los movimientos de esta fila? */
  movsCargados(id: number): boolean { return this.movsPorFila()[id] !== undefined; }

  /** ¿El monto de este movimiento coincide (casi exacto) con la diferencia? → posible causa a revisar. */
  esCandidato(m: MovimientoCaja, c: CuadreDiarioFila): boolean {
    const dif = Math.abs(this.diferenciaFirmada(c));
    return dif > 0.01 && Math.abs(Math.abs(m.monto) - dif) < 0.05;
  }

  /** Pistas concretas de dónde puede estar el error, en lenguaje simple para el administrador. */
  pistasError(c: CuadreDiarioFila, id: number): string[] {
    const dif = this.diferenciaFirmada(c);
    if (Math.abs(dif) < 0.01) return [];
    const abs = Math.abs(dif);
    const movs = this.movsDelDia(id);
    const gastos = movs.filter(m => m.tipo === 'GASTO');
    const candidatos = movs.filter(m => this.esCandidato(m, c));
    const ref = (m: MovimientoCaja) => m.pedidoNumero ? `Pedido #${m.pedidoNumero}` : (m.tipoGastoNombre || m.descripcion || 'un movimiento');
    const out: string[] = [];

    if (dif < 0) { // FALTA: contó menos efectivo del que debía
      if (gastos.length === 0)
        out.push(`No hay ningún gasto ni retiro anotado este día. Si se sacó dinero de la caja sin registrarlo, ese sería el faltante de S/ ${abs.toFixed(2)}.`);
      for (const m of candidatos) {
        if (m.tipo === 'INGRESO' && (m.metodoPago || '').toUpperCase() === 'EFECTIVO')
          out.push(`El cobro de ${ref(m)} por S/ ${m.monto.toFixed(2)} coincide con el faltante: confirma que ese pago sí entró en efectivo (no por Yape/Plin).`);
        else if (m.tipo === 'INGRESO')
          out.push(`El cobro de ${ref(m)} por S/ ${m.monto.toFixed(2)} (${this.metodoLabel(m.metodoPago || '')}) coincide con el faltante: si en realidad fue en efectivo, está con el método equivocado.`);
      }
    } else { // SOBRA: contó más efectivo del que debía
      out.push('Sobró efectivo: puede haber una venta cobrada en efectivo que no se registró, o un gasto anotado que no se hizo.');
      for (const m of candidatos) {
        if (m.tipo === 'GASTO')
          out.push(`El gasto de ${ref(m)} por S/ ${m.monto.toFixed(2)} coincide con el sobrante: revisa si ese gasto realmente salió de la caja.`);
        else if (m.tipo === 'INGRESO' && (m.metodoPago || '').toUpperCase() !== 'EFECTIVO')
          out.push(`El cobro de ${ref(m)} por S/ ${m.monto.toFixed(2)} (${this.metodoLabel(m.metodoPago || '')}) coincide con el sobrante: si en realidad fue en efectivo, está con el método equivocado.`);
      }
    }
    out.push('Si nada de esto aplica, pudo ser un vuelto mal dado o un error al contar el efectivo.');
    return out;
  }

  claseEstado(estado: string): string {
    return ({ CUADRA: 'badge badge--verde', SOBRA: 'badge badge--naranja', FALTA: 'badge badge--rojo' } as Record<string, string>)[estado]
      ?? 'badge badge--gris';
  }

  // Al hacer click en un día no cuadrado, ir al cuadre de esa fecha para guardarlo.
  verDia(dia: CuadreDiarioDia) {
    this.verFecha(dia.fecha);
  }

  /** Abre el cuadre de una fecha concreta (para revisar el detalle del día). */
  verFecha(fechaIso: string) {
    this.router.navigate(['/cuadre-caja'], { queryParams: { fecha: fechaIso.slice(0, 10) } });
  }

  // ===== Detalle de "montos no cuadrados": los movimientos del día sin cuadre guardado =====
  readonly modalNoCuadrado = signal<CuadreDiarioDia | null>(null);
  readonly movsNoCuadrado = signal<MovimientoCaja[]>([]);
  readonly cargandoNoCuadrado = signal(false);

  verDetalleNoCuadrado(d: CuadreDiarioDia) {
    this.modalNoCuadrado.set(d);
    this.cargandoNoCuadrado.set(true);
    this.movsNoCuadrado.set([]);
    this.caja.movimientos(d.fecha.slice(0, 10)).subscribe({
      next: ms => { this.movsNoCuadrado.set(ms); this.cargandoNoCuadrado.set(false); },
      error: () => this.cargandoNoCuadrado.set(false)
    });
  }
  cerrarNoCuadrado() { this.modalNoCuadrado.set(null); }

  /** Ingresos del día mostrado en el modal de no cuadrados. */
  readonly ncIngresos = computed(() => this.movsNoCuadrado().filter(m => m.tipo === 'INGRESO').reduce((a, m) => a + m.monto, 0));
  readonly ncEgresos = computed(() => this.movsNoCuadrado().filter(m => m.tipo === 'GASTO').reduce((a, m) => a + m.monto, 0));
}
