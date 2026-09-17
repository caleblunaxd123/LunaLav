import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { debounceTime, forkJoin } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { CajaService } from '../../core/services/caja.service';
import { CatalogosService } from '../../core/services/catalogos.service';
import { fechaLocalIso } from '../../core/util/fecha-local';
import { ClientesService, ClienteCumpleanos } from '../../core/services/clientes.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { Dashboard, PedidosService } from '../../core/services/pedidos.service';
import { PersonalService } from '../../core/services/personal.service';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent, IconName } from '../../shared/icon/icon.component';
import { SkeletonComponent } from '../../shared/skeleton/skeleton.component';
import { TourService } from '../../core/services/tour.service';
import { TOURS } from '../../core/constants/tours';
import { ActualizacionDatosService } from '../../core/services/actualizacion-datos.service';
import { formatearDuracion as fmtDuracion } from '../../core/util/duracion';

interface PasoOnboarding {
  clave: string;
  titulo: string;
  hecho: boolean;
  ruta: string;
}

interface AlertaInicio {
  clave: string;
  titulo: string;
  detalle: string;
  accion: string;
  ruta: string;
  queryParams?: Record<string, string>;
  nivel: 'critica' | 'advertencia' | 'informativa';
  icono: IconName;
}

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, RouterLink, SkeletonComponent, IconComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly svc = inject(PedidosService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly config = inject(ConfiguracionService);
  private readonly catalogos = inject(CatalogosService);
  private readonly personalSvc = inject(PersonalService);
  private readonly clientesSvc = inject(ClientesService);
  private readonly cajaSvc = inject(CajaService);
  private readonly actualizaciones = inject(ActualizacionDatosService);
  private readonly destroyRef = inject(DestroyRef);

  // Clientes que cumplen años dentro de la semana (para alerta de fidelización).
  readonly cumpleanos = signal<ClienteCumpleanos[]>([]);

  // Turnos de HOY con movimientos de caja pero sin cierre guardado (para alerta "caja sin cerrar").
  readonly cajaHoyPendiente = signal(0);

  readonly data = signal<Dashboard | null>(null);
  readonly cargando = signal(false);
  readonly actualizando = signal(false);
  readonly usuario = this.auth.usuario;
  readonly pasosOnboarding = signal<PasoOnboarding[]>([]);
  readonly onboardingCerrado = signal(false);

  readonly mostrarOnboarding = computed(() =>
    this.usuario()?.rol === 'ADMIN' &&
    !this.onboardingCerrado() &&
    this.pasosOnboarding().some(p => !p.hecho)
  );

  readonly puedeVerFinanzas = computed(() =>
    this.tieneModulo('CAJA') || this.tieneModulo('REPORTES')
  );

  readonly progresoMeta = computed(() => {
    const d = this.data();
    if (!d || d.metaMensual <= 0) return 0;
    return Math.min(100, Math.round((d.pedidosDelMes / d.metaMensual) * 100));
  });

  // ===== Panel visual (estilo mockup): comparativos, curva semanal y dona =====
  private pctDelta(actual: number, previo: number): number | null {
    if (previo <= 0) return actual > 0 ? 100 : null;
    return Math.round(((actual - previo) / previo) * 100);
  }
  readonly deltaOrdenes = computed(() => {
    const d = this.data(); return d ? this.pctDelta(d.ordenesHoy, d.ordenesAyer) : null;
  });
  readonly deltaVentas = computed(() => {
    const d = this.data(); return d ? this.pctDelta(d.ventasDelDia, d.ventasAyer) : null;
  });
  readonly deltaClientes = computed(() => {
    const d = this.data(); return d ? this.pctDelta(d.clientesNuevosMes, d.clientesNuevosMesAnterior) : null;
  });

  // ===== Tendencia de ventas con rango seleccionable =====
  readonly rangosVentas = [
    { dias: 7, etiqueta: '7 días' },
    { dias: 14, etiqueta: '14 días' },
    { dias: 30, etiqueta: '30 días' },
    { dias: 90, etiqueta: '90 días' },
  ];
  readonly rangoVentas = signal(30);
  readonly ventasSerie = signal<Array<{ fecha: string; total: number }>>([]);
  readonly cargandoTendencia = signal(false);

  cambiarRangoVentas(dias: number) {
    if (this.rangoVentas() === dias) return;
    this.rangoVentas.set(dias);
    this.cargarTendenciaVentas();
  }

  private cargarTendenciaVentas() {
    if (!this.puedeVerFinanzas()) return;
    this.cargandoTendencia.set(true);
    this.svc.ventasTendencia(this.rangoVentas()).subscribe({
      next: s => { this.ventasSerie.set(s); this.cargandoTendencia.set(false); },
      error: () => this.cargandoTendencia.set(false)
    });
  }

  /** Serie de ventas del rango elegido como coordenadas SVG (viewBox 0 0 320 120). */
  readonly semana = computed(() => {
    const serie = this.ventasSerie();
    const W = 320, H = 120, padX = 10, padY = 14;
    const max = Math.max(1, ...serie.map(p => p.total));
    const n = Math.max(1, serie.length);
    const stepX = (W - padX * 2) / Math.max(1, n - 1);
    const fmtFecha = (iso: string) => { const p = iso.split('-'); return p.length === 3 ? `${p[2]}/${p[1]}` : iso; };
    const puntos = serie.map((p, i) => ({
      x: Math.round(padX + i * stepX),
      y: Math.round(H - padY - (p.total / max) * (H - padY * 2)),
      total: p.total,
      fecha: p.fecha
    }));
    // Etiquetas espaciadas (máx ~7) para que no se amontonen en rangos largos.
    const maxLabels = 7;
    const paso = Math.max(1, Math.ceil(n / maxLabels));
    const labels = serie.map((p, i) => ({ i, text: fmtFecha(p.fecha) }))
      .filter(l => l.i % paso === 0 || l.i === n - 1)
      .map(l => l.text);
    const linePath = puntos.map((pt, i) => `${i === 0 ? 'M' : 'L'}${pt.x} ${pt.y}`).join(' ');
    const areaPath = puntos.length
      ? `${linePath} L${puntos[puntos.length - 1].x} ${H - padY} L${puntos[0].x} ${H - padY} Z`
      : '';
    return { puntos, linePath, areaPath, max, W, H, labels, mostrarPuntos: n <= 14, hayVentas: serie.some(p => p.total > 0) };
  });

  /** Dona de servicios (stroke-dasharray sobre un círculo r=54). */
  private readonly coloresDona = ['#053465', '#06B0BD', '#046086', '#7cc7d6', '#94a3b8'];
  readonly dona = computed(() => {
    const items = this.data()?.serviciosMasSolicitados ?? [];
    const total = items.reduce((a, s) => a + s.total, 0);
    const C = 2 * Math.PI * 54;
    let acumulado = 0;
    const segmentos = items.map((s, i) => {
      const pct = total > 0 ? s.total / total : 0;
      const seg = {
        nombre: s.nombre,
        pctTexto: Math.round(pct * 100),
        color: this.coloresDona[i % this.coloresDona.length],
        dash: `${(pct * C).toFixed(2)} ${(C - pct * C).toFixed(2)}`,
        offset: (-acumulado * C).toFixed(2)
      };
      acumulado += pct;
      return seg;
    });
    return { total, segmentos, C };
  });

  estadoBadge(estado: string): { label: string; clase: string } {
    switch (estado) {
      case 'LISTO': return { label: 'Listo', clase: 'ok' };
      case 'EN_PROCESO': return { label: 'En proceso', clase: 'proc' };
      case 'PENDIENTE': return { label: 'Pendiente', clase: 'pend' };
      case 'ENTREGADO': return { label: 'Entregado', clase: 'entregado' };
      default: return { label: estado, clase: 'neutral' };
    }
  }

  readonly alertas = computed<AlertaInicio[]>(() => {
    const d = this.data();
    if (!d) return [];
    const alertas: AlertaInicio[] = [];

    if (this.tieneModulo('CAJA') && this.cajaHoyPendiente() > 0) {
      const n = this.cajaHoyPendiente();
      alertas.push({
        clave: 'caja-sin-cerrar',
        titulo: 'La caja de hoy aún no se ha cerrado',
        detalle: n === 1
          ? 'Hay un turno con movimientos sin cuadrar. Ciérralo para dejar el conteo registrado.'
          : `Hay ${n} turnos con movimientos sin cuadrar. Ciérralos para dejar el conteo registrado.`,
        accion: 'Cerrar caja',
        ruta: '/cuadre-caja',
        nivel: 'advertencia',
        icono: 'cash'
      });
    }

    if (this.tieneModulo('PEDIDOS') && d.totalPedidosEstancados > 0 && d.pedidosEstancados.length > 0) {
      const primero = d.pedidosEstancados[0];
      const adicionales = d.totalPedidosEstancados - 1;
      alertas.push({
        clave: 'estancados',
        titulo: `${d.totalPedidosEstancados} ${d.totalPedidosEstancados === 1 ? 'pedido excedió' : 'pedidos excedieron'} el tiempo de proceso`,
        detalle: `Pedido #${primero.numero} lleva ${this.formatearDuracion(primero.minutosEnArea)} en ${primero.areaNombre}${adicionales > 0 ? ` y hay ${adicionales} más` : ''}.`,
        accion: 'Revisar pedidos',
        ruta: '/pedidos',
        queryParams: { ver: 'fuera-de-tiempo' },
        nivel: 'critica',
        icono: 'warning'
      });
    }

    if (this.tieneModulo('PEDIDOS') && d.totalPedidosAbandonados > 0 && d.pedidosAbandonados.length > 0) {
      const primero = d.pedidosAbandonados[0];
      alertas.push({
        clave: 'abandonados',
        titulo: `${d.totalPedidosAbandonados} ${d.totalPedidosAbandonados === 1 ? 'pedido espera' : 'pedidos esperan'} recojo hace varios días`,
        detalle: `El pedido #${primero.numero} de ${primero.clienteNombre} lleva ${primero.diasEsperando} días listo.`,
        accion: 'Gestionar recojos',
        ruta: this.tieneModulo('REPORTES') ? '/reportes/almacen' : '/pedidos',
        nivel: 'advertencia',
        icono: 'phone-alert'
      });
    }

    if (this.tieneModulo('AJUSTES') && (d.comprobantesRechazados ?? 0) > 0) {
      alertas.push({
        clave: 'comprobantes-rechazados',
        titulo: `${d.comprobantesRechazados} comprobante${d.comprobantesRechazados === 1 ? '' : 's'} con error`,
        detalle: 'Requiere corrección o reenvío para completar la emisión electrónica.',
        accion: 'Resolver comprobantes',
        ruta: '/facturacion/comprobantes',
        nivel: 'critica',
        icono: 'warning'
      });
    }

    if (this.tieneModulo('INVENTARIO') && (d.insumosBajoStock ?? 0) > 0) {
      alertas.push({
        clave: 'stock',
        titulo: `${d.insumosBajoStock} ${d.insumosBajoStock === 1 ? 'insumo está' : 'insumos están'} bajo el mínimo`,
        detalle: 'Revisa existencias antes de que afecten la operación.',
        accion: 'Abrir inventario',
        ruta: '/inventario',
        nivel: 'advertencia',
        icono: 'package'
      });
    }

    if (this.tieneModulo('AJUSTES') && (d.comprobantesPendientes ?? 0) > 0) {
      alertas.push({
        clave: 'comprobantes-pendientes',
        titulo: `${d.comprobantesPendientes} comprobante${d.comprobantesPendientes === 1 ? '' : 's'} pendiente${d.comprobantesPendientes === 1 ? '' : 's'}`,
        detalle: 'La emisión todavía no ha sido confirmada.',
        accion: 'Ver estado',
        ruta: '/facturacion/comprobantes',
        nivel: 'informativa',
        icono: 'info'
      });
    }

    const cumple = this.cumpleanos();
    if (this.tieneModulo('CLIENTES') && cumple.length > 0) {
      const hoy = cumple.filter(c => c.diasParaCumpleanos === 0).length;
      alertas.push({
        clave: 'cumpleanos',
        titulo: `${cumple.length} cliente${cumple.length === 1 ? '' : 's'} de cumpleaños esta semana`,
        detalle: hoy > 0
          ? `${hoy} cumple${hoy === 1 ? '' : 'n'} hoy — buen momento para saludar o dar una promo.`
          : `El más próximo: ${cumple[0].nombre} en ${cumple[0].diasParaCumpleanos} día${cumple[0].diasParaCumpleanos === 1 ? '' : 's'}.`,
        accion: 'Ver clientes',
        ruta: '/clientes',
        nivel: 'informativa',
        icono: 'users'
      });
    }

    return alertas.slice(0, 6);
  });

  private readonly tour = inject(TourService);
  iniciarTour() { this.tour.iniciar(TOURS['inicio']); }

  readonly saludo = computed(() => {
    const hora = new Date().getHours();
    if (hora < 12) return 'Buenos días';
    if (hora < 19) return 'Buenas tardes';
    return 'Buenas noches';
  });

  readonly nombreCorto = computed(() =>
    (this.usuario()?.nombreCompleto ?? '').trim().split(/\s+/)[0] || ''
  );
  readonly nombreNegocio = computed(() => this.config.configuracion().nombreNegocio);
  readonly fechaHoy = computed(() => {
    const texto = new Intl.DateTimeFormat('es-PE', {
      weekday: 'long', day: 'numeric', month: 'long'
    }).format(new Date());
    return texto.charAt(0).toUpperCase() + texto.slice(1);
  });

  private timerId?: ReturnType<typeof setInterval>;
  private versionCarga = 0;

  constructor() {
    this.actualizaciones.cambios('dashboard', 'pedidos', 'caja', 'inventario', 'facturacion', 'clientes', 'foco').pipe(
      debounceTime(180),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => this.cargarSiVisible());
  }

  ngOnInit() {
    this.cargar();
    this.cargarOnboarding();
    // Un visitante de la demo no conoce el sistema: el recorrido inicia solo al entrar.
    if (this.usuario()?.rol === 'DEMO') queueMicrotask(() => this.iniciarTour());
    if (this.tieneModulo('CLIENTES')) {
      this.clientesSvc.cumpleanosProximos(7).subscribe({
        next: cs => this.cumpleanos.set(
          [...cs].sort((a, b) => a.diasParaCumpleanos - b.diasParaCumpleanos)),
        error: () => undefined
      });
    }
    // Sin auto-refresco periódico (incómodo al trabajar); se actualiza por foco/cambios.
  }

  ngOnDestroy() {
    if (this.timerId) clearInterval(this.timerId);
  }

  cargar(silencioso = false) {
    const version = ++this.versionCarga;
    if (silencioso) this.actualizando.set(true);
    else this.cargando.set(true);

    this.cargarEstadoCaja();
    this.cargarTendenciaVentas();
    this.svc.dashboard().subscribe({
      next: d => {
        if (version !== this.versionCarga) return;
        this.data.set(d);
        this.cargando.set(false);
        this.actualizando.set(false);
      },
      error: () => {
        if (version !== this.versionCarga) return;
        this.cargando.set(false);
        this.actualizando.set(false);
        if (!silencioso) this.toast.error('No se pudo cargar el resumen. Intenta nuevamente.');
      }
    });
  }

  private cargarSiVisible() {
    if (typeof document !== 'undefined' && document.visibilityState !== 'visible') return;
    if (!this.cargando() && !this.actualizando()) this.cargar(true);
  }

  /** Revisa si la caja de HOY tiene movimientos sin cuadrar (para el aviso del dashboard). */
  private cargarEstadoCaja() {
    if (!this.tieneModulo('CAJA')) return;
    this.cajaSvc.usuariosDelDia(fechaLocalIso()).subscribe({
      next: list => this.cajaHoyPendiente.set(
        list.filter(u => u.movimientos > 0 && !u.tieneCuadre).length),
      error: () => { /* silencioso: el aviso simplemente no aparece */ }
    });
  }

  cerrarOnboarding() {
    localStorage.setItem(this.claveCierre(), '1');
    this.onboardingCerrado.set(true);
  }

  tieneModulo(modulo: string): boolean {
    return this.usuario()?.modulosPermitidos?.includes(modulo) ?? false;
  }

  actualizadoTexto(fecha: string): string {
    if (!fecha) return '';
    return new Intl.DateTimeFormat('es-PE', {
      hour: '2-digit', minute: '2-digit'
    }).format(new Date(fecha));
  }

  atrasadosEnArea(areaId: number): number {
    return this.data()?.pedidosEstancados.filter(p => p.areaId === areaId).length ?? 0;
  }

  estadoArea(areaId: number): 'critico' | 'advertencia' | 'correcto' | 'neutral' {
    const d = this.data();
    if (!d) return 'neutral';
    if (this.atrasadosEnArea(areaId) > 0) return 'critico';
    const sla = d.slaPorArea.find(a => a.areaId === areaId);
    if (!sla || sla.pedidosProcesados === 0) return 'neutral';
    if (sla.minutosPromedioReal > sla.tiempoEstMinutos) return 'advertencia';
    return 'correcto';
  }

  textoSla(areaId: number): string {
    const atrasados = this.atrasadosEnArea(areaId);
    if (atrasados > 0) return `${atrasados} fuera de tiempo`;
    const sla = this.data()?.slaPorArea.find(a => a.areaId === areaId);
    if (!sla || sla.pedidosProcesados === 0) return 'Sin historial SLA';
    return `Prom. ${this.formatearDuracion(Math.round(sla.minutosPromedioReal))} / meta ${this.formatearDuracion(sla.tiempoEstMinutos)}`;
  }

  anchoCarga(cantidad: number): number {
    const maximo = Math.max(...(this.data()?.pedidosPorArea.map(a => a.cantidad) ?? [0]), 1);
    return cantidad === 0 ? 0 : Math.max(10, Math.round((cantidad / maximo) * 100));
  }

  formatearDuracion(minutos: number): string {
    return fmtDuracion(minutos);
  }

  private claveCierre(): string {
    return `lav.onboarding.cerrado.${this.usuario()?.negocioId ?? 0}`;
  }

  private cargarOnboarding() {
    if (this.usuario()?.rol !== 'ADMIN') return;
    this.onboardingCerrado.set(localStorage.getItem(this.claveCierre()) === '1');

    forkJoin({
      servicios: this.catalogos.servicios(),
      personal: this.personalSvc.listar()
    }).subscribe({
      next: ({ servicios, personal }) => this.pasosOnboarding.set([
        { clave: 'logo', titulo: 'Agregar logo del negocio', hecho: !!this.config.configuracion().logoUrl, ruta: '/ajustes/negocio' },
        { clave: 'servicios', titulo: 'Configurar servicios y precios', hecho: servicios.length > 0, ruta: '/ajustes/servicios' },
        { clave: 'personal', titulo: 'Registrar al equipo', hecho: personal.length > 0, ruta: '/ajustes/personal' }
      ]),
      error: () => undefined
    });
  }
}
