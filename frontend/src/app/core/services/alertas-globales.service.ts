import { Injectable, computed, inject, signal } from '@angular/core';
import { forkJoin, of } from 'rxjs';
import { catchError, debounceTime, finalize } from 'rxjs/operators';
import { ActualizacionDatosService } from './actualizacion-datos.service';
import { AuthService } from './auth.service';
import { NegociosPlataformaService } from './negocios-plataforma.service';
import { Dashboard, PedidosService } from './pedidos.service';
import { SuscripcionService } from './suscripcion.service';
import { formatearDuracion as fmtDuracion } from '../util/duracion';

export type NivelAlertaGlobal = 'critica' | 'advertencia' | 'informativa' | 'exito';
export type IconoAlertaGlobal = 'warning' | 'calendar' | 'phone-alert' | 'package' | 'check' | 'note' | 'info' | 'users';

export interface AlertaGlobal {
  clave: string;
  huella: string;
  titulo: string;
  detalle: string;
  accion: string;
  ruta: string;
  queryParams?: Record<string, string>;
  nivel: NivelAlertaGlobal;
  icono: IconoAlertaGlobal;
  cantidad: number;
  ambito: 'negocio' | 'plataforma';
}

const STORAGE_KEY = 'lav.alertas.descartadas';

@Injectable({ providedIn: 'root' })
export class AlertasGlobalesService {
  private readonly auth = inject(AuthService);
  private readonly pedidos = inject(PedidosService);
  private readonly suscripcion = inject(SuscripcionService);
  private readonly plataforma = inject(NegociosPlataformaService);
  private readonly actualizaciones = inject(ActualizacionDatosService);

  private readonly todas = signal<AlertaGlobal[]>([]);
  private readonly descartadas = signal<Record<string, number>>(this.leerDescartadas());
  private contexto = '';
  private timer?: ReturnType<typeof setInterval>;
  private generacion = 0;

  readonly cargando = signal(false);
  readonly error = signal(false);
  readonly actualizadoEn = signal<Date | null>(null);
  readonly alertas = computed(() => {
    const ahora = Date.now();
    const descartadas = this.descartadas();
    return this.todas().filter(alerta => (descartadas[this.idDescarte(alerta)] ?? 0) <= ahora);
  });
  readonly total = computed(() => Math.min(99, this.alertas().reduce((suma, alerta) => suma + alerta.cantidad, 0)));

  constructor() {
    this.actualizaciones
      .cambios('pedidos', 'dashboard', 'caja', 'inventario', 'facturacion', 'plataforma', 'foco')
      .pipe(debounceTime(180))
      .subscribe(() => {
        if (this.contexto) this.refrescar(true);
      });
  }

  iniciar(): void {
    const usuario = this.auth.usuario();
    if (!usuario) return;
    if (usuario.rol !== 'PROPIETARIO' && !usuario.sedeId) {
      this.detener();
      this.todas.set([]);
      this.cargando.set(false);
      return;
    }
    const nuevoContexto = `${usuario.rol}:${usuario.negocioId}:${usuario.sedeId ?? 'sin-sede'}`;
    if (this.contexto === nuevoContexto && this.timer) return;

    this.detener();
    this.contexto = nuevoContexto;
    this.refrescar();
    this.timer = setInterval(() => {
      if (typeof document === 'undefined' || document.visibilityState === 'visible') this.refrescar(true);
    }, 15_000);
  }

  detener(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = undefined;
    this.contexto = '';
    this.generacion++;
  }

  refrescar(silencioso = false): void {
    const usuario = this.auth.usuario();
    if (!usuario) return;
    if (usuario.rol !== 'PROPIETARIO' && !usuario.sedeId) {
      this.todas.set([]);
      this.cargando.set(false);
      return;
    }
    const generacion = ++this.generacion;
    if (!silencioso) this.cargando.set(true);
    this.error.set(false);

    if (usuario.rol === 'PROPIETARIO') {
      this.plataforma.resumen().pipe(
        catchError(() => of(null)),
        finalize(() => {
          if (generacion === this.generacion) this.cargando.set(false);
        })
      ).subscribe(resumen => {
        if (generacion !== this.generacion) return;
        if (!resumen) {
          if (!silencioso) this.error.set(true);
          return;
        }
        this.establecerAlertas(this.crearAlertasPlataforma(resumen));
        this.actualizadoEn.set(new Date());
        this.limpiarDescartadasVencidas();
      });
      return;
    }

    const puedeVerInicio = this.tieneModulo('INICIO');
    forkJoin({
      dashboard: puedeVerInicio
        ? this.pedidos.dashboard().pipe(catchError(() => of(null)))
        : of<Dashboard | null>(null),
      suscripcion: this.suscripcion.mia().pipe(catchError(() => of(null)))
    }).pipe(finalize(() => {
      if (generacion === this.generacion) this.cargando.set(false);
    })).subscribe(({ dashboard, suscripcion }) => {
      if (generacion !== this.generacion) return;
      if (!dashboard && !suscripcion) {
        if (!silencioso) this.error.set(true);
        return;
      }
      this.establecerAlertas(this.crearAlertasNegocio(dashboard, suscripcion));
      this.actualizadoEn.set(new Date());
      this.limpiarDescartadasVencidas();
    });
  }

  descartar(alerta: AlertaGlobal): void {
    const duracion = alerta.nivel === 'critica'
      ? 60 * 60_000
      : alerta.nivel === 'advertencia'
        ? 4 * 60 * 60_000
        : 12 * 60 * 60_000;
    const estado = { ...this.descartadas(), [this.idDescarte(alerta)]: Date.now() + duracion };
    this.descartadas.set(estado);
    this.guardarDescartadas(estado);
  }

  private crearAlertasNegocio(d: Dashboard | null, suscripcion: { mostrar: boolean; tipo: string; mensaje: string } | null): AlertaGlobal[] {
    const alertas: AlertaGlobal[] = [];

    if (suscripcion?.mostrar) {
      const critica = suscripcion.tipo === 'VENCIDA';
      alertas.push({
        clave: 'suscripcion', huella: suscripcion.tipo, titulo: critica ? 'Suscripción vencida' : 'Suscripción próxima a vencer',
        detalle: suscripcion.mensaje, accion: this.tieneModulo('AJUSTES') ? 'Revisar suscripción' : 'Ver inicio',
        ruta: this.tieneModulo('AJUSTES') ? '/ajustes' : '/inicio', nivel: critica ? 'critica' : 'advertencia',
        icono: critica ? 'warning' : 'calendar', cantidad: 1, ambito: 'negocio'
      });
    }
    if (!d) return alertas;

    if (this.tieneModulo('PEDIDOS') && d.totalPedidosEstancados > 0) {
      const primero = d.pedidosEstancados[0];
      alertas.push({
        clave: 'estancados', huella: `${d.totalPedidosEstancados}:${primero?.pedidoId ?? 0}`,
        titulo: `${d.totalPedidosEstancados} pedido${d.totalPedidosEstancados === 1 ? '' : 's'} fuera de tiempo`,
        detalle: primero ? `La orden #${primero.numero} lleva ${this.formatearDuracion(primero.minutosEnArea)} en ${primero.areaNombre}.` : 'Hay pedidos que excedieron el tiempo esperado de su etapa.',
        accion: 'Revisar pedidos', ruta: '/pedidos', queryParams: { ver: 'fuera-de-tiempo' },
        nivel: 'critica', icono: 'warning',
        cantidad: d.totalPedidosEstancados, ambito: 'negocio'
      });
    }
    if (this.tieneModulo('AJUSTES') && (d.comprobantesRechazados ?? 0) > 0) {
      alertas.push({
        clave: 'comprobantes-error', huella: String(d.comprobantesRechazados),
        titulo: `${d.comprobantesRechazados} comprobante${d.comprobantesRechazados === 1 ? '' : 's'} con error`,
        detalle: 'Corrige o reenvía la emisión para evitar comprobantes sin validez.', accion: 'Resolver ahora',
        ruta: '/facturacion/comprobantes', nivel: 'critica', icono: 'note',
        cantidad: d.comprobantesRechazados ?? 0, ambito: 'negocio'
      });
    }
    if (this.tieneModulo('PEDIDOS') && d.totalPedidosAbandonados > 0) {
      const primero = d.pedidosAbandonados[0];
      alertas.push({
        clave: 'abandonados', huella: `${d.totalPedidosAbandonados}:${primero?.pedidoId ?? 0}`,
        titulo: `${d.totalPedidosAbandonados} pedido${d.totalPedidosAbandonados === 1 ? '' : 's'} esperando recojo`,
        detalle: primero ? `La orden #${primero.numero} de ${primero.clienteNombre} lleva ${primero.diasEsperando} días lista.` : 'Hay pedidos listos sin recoger desde hace varios días.',
        accion: 'Gestionar recojos', ruta: '/pedidos', queryParams: { ver: 'abandonados' },
        nivel: 'advertencia', icono: 'phone-alert',
        cantidad: d.totalPedidosAbandonados, ambito: 'negocio'
      });
    }
    if (this.tieneModulo('INVENTARIO') && (d.insumosBajoStock ?? 0) > 0) {
      alertas.push({
        clave: 'stock', huella: String(d.insumosBajoStock),
        titulo: `${d.insumosBajoStock} insumo${d.insumosBajoStock === 1 ? '' : 's'} bajo stock mínimo`,
        detalle: 'Repón existencias antes de que afecten la atención de pedidos.', accion: 'Abrir inventario',
        ruta: '/inventario', queryParams: { ver: 'bajo-stock' }, nivel: 'advertencia', icono: 'package',
        cantidad: d.insumosBajoStock ?? 0, ambito: 'negocio'
      });
    }
    if (this.tieneModulo('PEDIDOS') && d.totalListos > 0) {
      alertas.push({
        clave: 'listos', huella: String(d.totalListos),
        titulo: `${d.totalListos} pedido${d.totalListos === 1 ? '' : 's'} listo${d.totalListos === 1 ? '' : 's'} para entregar`,
        detalle: 'Confirma el cobro y la identidad de quien recibe antes de completar la entrega.', accion: 'Ver pedidos',
        ruta: '/pedidos', queryParams: { ver: 'listos' }, nivel: 'informativa', icono: 'check', cantidad: d.totalListos, ambito: 'negocio'
      });
    }
    if (this.tieneModulo('AJUSTES') && (d.comprobantesPendientes ?? 0) > 0) {
      alertas.push({
        clave: 'comprobantes-pendientes', huella: String(d.comprobantesPendientes),
        titulo: `${d.comprobantesPendientes} comprobante${d.comprobantesPendientes === 1 ? '' : 's'} pendiente${d.comprobantesPendientes === 1 ? '' : 's'}`,
        detalle: 'La emisión electrónica todavía no ha sido confirmada.', accion: 'Ver estado',
        ruta: '/facturacion/comprobantes', nivel: 'informativa', icono: 'info',
        cantidad: d.comprobantesPendientes ?? 0, ambito: 'negocio'
      });
    }
    return alertas;
  }

  private crearAlertasPlataforma(r: { empresasVencidas: number; empresasPorVencer: number; empresasSuspendidas: number }): AlertaGlobal[] {
    const alertas: AlertaGlobal[] = [];
    if (r.empresasVencidas > 0) {
      alertas.push({
        clave: 'empresas-vencidas', huella: String(r.empresasVencidas),
        titulo: `${r.empresasVencidas} empresa${r.empresasVencidas === 1 ? '' : 's'} con suscripción vencida`,
        detalle: 'Revisa el cobro o suspende el acceso para mantener la cartera al día.', accion: 'Gestionar empresas',
        ruta: '/plataforma', nivel: 'critica', icono: 'warning', cantidad: r.empresasVencidas, ambito: 'plataforma'
      });
    }
    if (r.empresasPorVencer > 0) {
      alertas.push({
        clave: 'empresas-por-vencer', huella: String(r.empresasPorVencer),
        titulo: `${r.empresasPorVencer} empresa${r.empresasPorVencer === 1 ? '' : 's'} próxima${r.empresasPorVencer === 1 ? '' : 's'} a vencer`,
        detalle: 'Hay suscripciones con fecha de pago dentro de los próximos siete días.', accion: 'Revisar cobros',
        ruta: '/plataforma', nivel: 'advertencia', icono: 'calendar', cantidad: r.empresasPorVencer, ambito: 'plataforma'
      });
    }
    if (r.empresasSuspendidas > 0) {
      alertas.push({
        clave: 'empresas-suspendidas', huella: String(r.empresasSuspendidas),
        titulo: `${r.empresasSuspendidas} empresa${r.empresasSuspendidas === 1 ? '' : 's'} suspendida${r.empresasSuspendidas === 1 ? '' : 's'}`,
        detalle: 'Sus usuarios no pueden ingresar mientras el acceso permanezca suspendido.', accion: 'Ver empresas',
        ruta: '/plataforma', nivel: 'informativa', icono: 'info', cantidad: r.empresasSuspendidas, ambito: 'plataforma'
      });
    }
    return alertas;
  }

  private tieneModulo(modulo: string): boolean {
    const usuario = this.auth.usuario();
    return usuario?.rol === 'ADMIN' || (usuario?.modulosPermitidos ?? []).includes(modulo);
  }

  private formatearDuracion(minutos: number): string {
    return fmtDuracion(minutos);
  }

  private idDescarte(alerta: AlertaGlobal): string {
    return `${this.contexto}:${alerta.clave}:${alerta.huella}`;
  }

  private establecerAlertas(alertas: AlertaGlobal[]): void {
    const ahora = Date.now();
    const prefijo = `${this.contexto}:`;
    const activas = new Set(alertas.map(alerta => this.idDescarte(alerta)));
    const descartadas = Object.fromEntries(Object.entries(this.descartadas()).filter(([clave, expira]) =>
      expira > ahora && (!clave.startsWith(prefijo) || activas.has(clave))
    ));
    this.descartadas.set(descartadas);
    this.guardarDescartadas(descartadas);
    this.todas.set(alertas);
  }

  private leerDescartadas(): Record<string, number> {
    try {
      return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Record<string, number>;
    } catch {
      return {};
    }
  }

  private guardarDescartadas(estado: Record<string, number>): void {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(estado)); } catch { /* almacenamiento opcional */ }
  }

  private limpiarDescartadasVencidas(): void {
    const ahora = Date.now();
    const vigentes = Object.fromEntries(Object.entries(this.descartadas()).filter(([, expira]) => expira > ahora));
    this.descartadas.set(vigentes);
    this.guardarDescartadas(vigentes);
  }
}
