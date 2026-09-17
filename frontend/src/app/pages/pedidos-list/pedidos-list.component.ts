import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, Component, DestroyRef, ElementRef, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AreaLavado, Pedido, PedidoAbandonado } from '../../core/models/models';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { PedidosService, TendenciaPedidos } from '../../core/services/pedidos.service';
import { MiniBarrasComponent } from '../../shared/mini-barras/mini-barras.component';
import { ToastService } from '../../core/services/toast.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { SkeletonComponent } from '../../shared/skeleton/skeleton.component';
import { debounceTime } from 'rxjs';
import { ActualizacionDatosService } from '../../core/services/actualizacion-datos.service';

type Filtro = 'pendientes' | 'otros' | 'ultimos' | 'fecha';
type TipoFecha = 'ingreso' | 'entrega';
type VistaPedidos = 'lista' | 'tablero';
// Vistas destacadas que llegan desde las alertas de "Atención operativa" (?ver=...): cada una
// acota la lista al subconjunto exacto que cuenta la alerta, en vez de mostrar todos los pedidos.
type ModoDestacado = 'fuera-de-tiempo' | 'listos' | 'abandonados';
type KanbanTone = 'pendiente' | 'area' | 'listo' | 'entregado' | 'anulado' | 'alerta';

interface KanbanColumn {
  key: string;
  titulo: string;
  subtitulo: string;
  tone: KanbanTone;
  pedidos: Pedido[];
}

/**
 * Lista/tablero de pedidos. El detalle vive en su propia página (/pedidos/:id):
 * aquí solo quedan la búsqueda, los filtros y el avance rápido de etapas por fila.
 */
@Component({
  selector: 'app-pedidos-list',
  imports: [CommonModule, FormsModule, RouterLink, EmptyStateComponent, PaginacionComponent, IconComponent, SkeletonComponent, PageHeaderComponent, MiniBarrasComponent],
  templateUrl: './pedidos-list.component.html',
  styleUrl: './pedidos-list.component.scss'
})
export class PedidosListComponent implements OnInit, OnDestroy, AfterViewInit {
  private readonly service = inject(PedidosService);
  private readonly catalogos = inject(CatalogosService);
  private readonly toast = inject(ToastService);
  private readonly whatsapp = inject(WhatsappService);
  private readonly config = inject(ConfiguracionService);
  private readonly actualizaciones = inject(ActualizacionDatosService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  // Vista destacada activa (llega desde una alerta con ?ver=...). null = lista normal.
  readonly modoDestacado = signal<ModoDestacado | null>(null);
  readonly destacadoActivo = computed(() => this.modoDestacado() !== null);
  // IDs exactos del subconjunto que cuenta la alerta (estancados o abandonados, según el
  // dashboard). Así la lista muestra EXACTAMENTE esos pedidos y el número de la alerta calza.
  // null = aún no cargados o modo que se filtra por estado (fallback en esDestacado).
  readonly destacadosIds = signal<Set<number> | null>(null);

  readonly pedidos = signal<Pedido[]>([]);
  readonly tendencia = signal<TendenciaPedidos | null>(null);
  readonly areas = signal<AreaLavado[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly filtro = signal<Filtro>('pendientes');
  readonly vista = signal<VistaPedidos>(this.vistaInicial());
  readonly busqueda = signal('');
  private busquedaTimerId?: ReturnType<typeof setTimeout>;

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly total = signal(0);

  readonly abandonados = signal<PedidoAbandonado[]>([]);
  readonly mostrarAbandonados = signal(true);

  readonly pedidosDelMes = signal(0);
  readonly metaMensual = computed(() => this.config.configuracion().metaMensual);
  readonly progresoMetaPct = computed(() => {
    const meta = this.metaMensual();
    if (!meta || meta <= 0) return 0;
    return Math.min(100, Math.round((this.pedidosDelMes() / meta) * 100));
  });

  /** Números de la escala del termómetro (0, ¼, ½, ¾ y la meta), como las marcas de un termómetro real. */
  readonly escalaMeta = computed(() => {
    const meta = this.metaMensual();
    if (meta <= 0) return [] as { valor: number; pct: number }[];
    return [0, 0.25, 0.5, 0.75, 1].map(f => ({ valor: Math.round(meta * f), pct: f * 100 }));
  });

  // Contadores por tab
  readonly totalPendientesTab = signal(0);
  readonly totalOtrosTab = signal(0);
  readonly totalUltimosTab = signal(0);

  readonly leyendaAbierta = signal(false);

  // Filtro por Fecha (modal Tipo de consulta)
  readonly modalTipoConsulta = signal(false);
  readonly tipoFechaConsulta = signal<TipoFecha>('ingreso');
  fechaFiltroDesde = '';
  fechaFiltroHasta = '';

  readonly resumenFiltroFecha = computed(() => {
    if (this.filtro() !== 'fecha') return null;
    const etiquetaCampo = this.tipoFechaConsulta() === 'entrega' ? 'entrega' : 'ingreso';
    const desde = this.fechaFiltroDesde || 'inicio';
    const hasta = this.fechaFiltroHasta || 'hoy';
    return `Filtrando por fecha de ${etiquetaCampo}: ${desde} a ${hasta}`;
  });

  readonly descripcionFiltro = computed<string | null>(() => {
    if (this.busqueda().trim()) return null;
    switch (this.filtro()) {
      case 'ultimos': return 'Los pedidos más recientes, de todos los estados.';
      case 'pendientes': return 'Pedidos aún en proceso: recibidos, en producción o listos sin entregar.';
      case 'otros': return 'Pedidos ya entregados o anulados (fuera del flujo activo).';
      case 'fecha': return null;
      default: return null;
    }
  });

  claseFila(p: Pedido): string {
    if (p.anulado) return 'fila--anulado';
    if (p.estadoProceso === 'ENTREGADO') return 'fila--entregado';
    return '';
  }

  // Filtros por columna (client-side sobre los pedidos ya cargados)
  readonly filtrosColumna = signal({
    numero: '', cliente: '', dni: '', items: '',
    area: '', estado: '', pago: ''
  });

  actualizarFiltroCol(campo: keyof ReturnType<typeof this.filtrosColumna>, valor: string) {
    this.filtrosColumna.update(f => ({ ...f, [campo]: valor }));
  }

  readonly pedidosFiltrados = computed(() => {
    const f = this.filtrosColumna();
    const norm = (s: string) => (s || '').toLowerCase();
    return this.pedidos().filter(p =>
      (!this.destacadoActivo() || this.esDestacado(p)) &&
      (!f.numero || String(p.numero).includes(f.numero.trim())) &&
      (!f.cliente || norm(p.clienteNombre ?? '').includes(norm(f.cliente))) &&
      (!f.dni || norm(p.clienteDni ?? '').includes(norm(f.dni))) &&
      (!f.items || p.items.some(i => norm(i.servicioNombre ?? '').includes(norm(f.items)))) &&
      (!f.area || norm(p.areaActualNombre ?? '').includes(norm(f.area))) &&
      (!f.estado || norm(p.estadoProceso).includes(norm(f.estado))) &&
      (!f.pago || norm(p.estadoPago).includes(norm(f.pago)))
    );
  });

  readonly resumenDestacado = computed(() => {
    const lista = this.pedidosFiltrados();
    return {
      cantidad: lista.length,
      totalDinero: lista.reduce((acc, p) => acc + (p.total ?? 0), 0),
      saldoPorCobrar: lista.reduce((acc, p) => acc + this.saldoPedido(p), 0),
    };
  });

  /** Título y descripción del banner de la vista destacada, según de qué alerta se llegó. */
  readonly bannerDestacado = computed(() => {
    switch (this.modoDestacado()) {
      case 'fuera-de-tiempo':
        return { titulo: 'Fuera de tiempo', desc: 'Exceden el tiempo esperado de su etapa' };
      case 'listos':
        return { titulo: 'Listos para entregar', desc: 'Pedidos terminados, pendientes de entrega y cobro' };
      case 'abandonados':
        return { titulo: 'Esperando recojo', desc: 'Listos sin recoger desde hace varios días' };
      default:
        return null;
    }
  });

  readonly kanbanColumns = computed<KanbanColumn[]>(() => {
    const pedidos = this.pedidosFiltrados();
    const areas = [...this.areas()].sort((a, b) => a.orden - b.orden);
    const idsArea = new Set(areas.map(a => a.id));
    const flujoCompleto = this.filtro() === 'pendientes' && !this.busqueda().trim();

    const columnas: KanbanColumn[] = [
      {
        key: 'pendiente',
        titulo: 'Sin iniciar',
        subtitulo: 'En recepción',
        tone: 'pendiente',
        pedidos: pedidos.filter(p => !p.anulado && p.estadoProceso === 'PENDIENTE' && p.areaActualId == null)
      },
      ...areas.map(area => ({
        key: `area-${area.id}`,
        titulo: area.nombre,
        subtitulo: `Meta ${area.tiempoEstMinutos} min`,
        tone: 'area' as const,
        pedidos: pedidos.filter(p =>
          !p.anulado &&
          (p.estadoProceso === 'PENDIENTE' || p.estadoProceso === 'EN_PROCESO') &&
          p.areaActualId === area.id
        )
      })),
      {
        key: 'listo',
        titulo: 'Listos',
        subtitulo: 'Para entregar',
        tone: 'listo',
        pedidos: pedidos.filter(p => !p.anulado && p.estadoProceso === 'LISTO')
      }
    ];

    const inconsistentes = pedidos.filter(p => {
      if (p.anulado || !['PENDIENTE', 'EN_PROCESO'].includes(p.estadoProceso)) return false;
      if (p.estadoProceso === 'EN_PROCESO' && p.areaActualId == null) return true;
      return p.areaActualId != null && !idsArea.has(p.areaActualId);
    });
    if (inconsistentes.length > 0) {
      columnas.push({
        key: 'revisar',
        titulo: 'Revisar',
        subtitulo: 'Flujo inconsistente',
        tone: 'alerta',
        pedidos: inconsistentes
      });
    }

    const entregados = pedidos.filter(p => !p.anulado && p.estadoProceso === 'ENTREGADO');
    if (entregados.length > 0) {
      columnas.push({ key: 'entregado', titulo: 'Entregados', subtitulo: 'Finalizados', tone: 'entregado', pedidos: entregados });
    }

    const anulados = pedidos.filter(p => p.anulado || p.estadoProceso === 'ANULADO');
    if (anulados.length > 0) {
      columnas.push({ key: 'anulado', titulo: 'Anulados', subtitulo: 'Sin actividad', tone: 'anulado', pedidos: anulados });
    }

    return flujoCompleto ? columnas : columnas.filter(c => c.pedidos.length > 0);
  });

  @ViewChild('buscadorInput') buscadorInputRef?: ElementRef<HTMLInputElement>;

  /** Al presionar Enter con un texto puramente numérico, abrir directo el pedido. */
  buscarPorNumeroYAbrir() {
    const texto = this.busqueda().trim();
    if (!texto) return;
    if (/^\d+$/.test(texto)) {
      const match = this.pedidos().find(p => String(p.numero) === texto);
      if (match) {
        this.abrirDetalle(match);
        return;
      }
    }
    if (this.pedidos().length === 1) {
      this.abrirDetalle(this.pedidos()[0]);
    }
  }

  readonly avanzando = signal(false);
  readonly avanzandoId = signal<number | null>(null);
  readonly pedidoAnimadoId = signal<number | null>(null);
  private pedidoAnimadoTimer?: ReturnType<typeof setTimeout>;

  private timerId?: ReturnType<typeof setInterval>;
  private versionRecarga = 0;

  constructor() {
    this.actualizaciones.cambios('pedidos', 'foco').pipe(
      debounceTime(180),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => this.refrescarDinamicamente());
  }

  ngOnInit() {
    this.catalogos.areasLavado().subscribe(a => this.areas.set(a));
    this.whatsapp.cargar();
    this.cargarAbandonados();
    this.cargarMetaMensual();
    this.service.tendencia(15).subscribe({ next: t => this.tendencia.set(t), error: () => {} });
    // Sin auto-refresco periódico: resultaba incómodo mientras se trabaja en la lista. La
    // lista se actualiza al volver a la pestaña o tras un cambio (suscripción a 'foco' arriba)
    // y con el botón de recargar.

    let primera = true;
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const ver = params.get('ver');
      const modo: ModoDestacado | null =
        ver === 'fuera-de-tiempo' || ver === 'listos' || ver === 'abandonados' ? ver : null;
      if (modo) {
        this.modoDestacado.set(modo);
        this.filtro.set('pendientes');   // los 3 subconjuntos viven dentro de "pendientes"
        this.busqueda.set('');
        this.vista.set('lista');
        this.tamanoPagina.set(100);
        this.pagina.set(1);
        this.destacadosIds.set(null);
        // "listos" se filtra por estado; los otros dos usan los IDs exactos del dashboard.
        if (modo === 'fuera-de-tiempo' || modo === 'abandonados') this.cargarDestacados(modo);
        this.recargar();
      } else {
        const estaba = this.destacadoActivo();
        this.modoDestacado.set(null);
        this.destacadosIds.set(null);
        if (estaba) this.tamanoPagina.set(15);
        if (primera || estaba) this.recargar();
      }
      primera = false;
    });
  }

  salirDestacado() {
    this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  /** Trae del dashboard los IDs exactos del subconjunto que reporta la alerta (estancados o abandonados). */
  private cargarDestacados(modo: ModoDestacado) {
    this.service.dashboard().subscribe({
      next: d => {
        const ids = modo === 'fuera-de-tiempo'
          ? d.pedidosEstancados.map(e => e.pedidoId)
          : d.pedidosAbandonados.map(a => a.pedidoId);
        this.destacadosIds.set(new Set(ids));
      },
      error: () => this.destacadosIds.set(null)
    });
  }

  ngAfterViewInit() {
    setTimeout(() => this.buscadorInputRef?.nativeElement.focus(), 100);
  }

  ngOnDestroy() {
    if (this.timerId) clearInterval(this.timerId);
    if (this.busquedaTimerId) clearTimeout(this.busquedaTimerId);
    if (this.pedidoAnimadoTimer) clearTimeout(this.pedidoAnimadoTimer);
  }

  cargarMetaMensual() {
    this.service.contadores().subscribe({
      next: d => {
        this.pedidosDelMes.set(d.pedidosDelMes);
        this.totalPendientesTab.set(d.totalPendientes);
        this.totalOtrosTab.set(d.totalOtros);
        this.totalUltimosTab.set(d.totalUltimos);
      },
      error: () => {}
    });
  }

  cargarAbandonados() {
    this.service.abandonados(3).subscribe({
      next: list => this.abandonados.set(list),
      error: () => {}
    });
  }

  /** El detalle vive en su propia página. */
  abrirDetalle(p: Pedido) {
    this.router.navigate(['/pedidos', p.id]);
  }

  abrirDetallePorId(pedidoId: number) {
    this.router.navigate(['/pedidos', pedidoId]);
  }

  avisarAbandonoWhatsapp(a: PedidoAbandonado) {
    if (!a.clienteCelular) {
      this.toast.advertencia('Este cliente no tiene celular registrado.');
      return;
    }
    const mensaje = this.whatsapp.mensaje('LISTO', {
      cliente: a.clienteNombre,
      numero: String(a.numero),
      negocio: this.config.configuracion().nombreNegocio,
    }, `Hola ${a.clienteNombre}, tu pedido #${a.numero} está listo para recoger hace ${a.diasEsperando} día${a.diasEsperando === 1 ? '' : 's'}. ¡Te esperamos!`);
    this.whatsapp.enviar(a.clienteCelular, mensaje);
  }

  cambiarVista(vista: VistaPedidos) {
    if (this.vista() === vista) return;
    this.vista.set(vista);
    this.pagina.set(1);
    try { localStorage.setItem('lavanderia.pedidos.vista', vista); } catch {}
    this.recargar();
  }

  cambiarFiltro(f: Filtro) {
    if (this.destacadoActivo()) {
      this.modoDestacado.set(null);
      this.destacadosIds.set(null);
      this.tamanoPagina.set(15);
      this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    }
    this.filtro.set(f);
    this.busqueda.set('');
    this.pagina.set(1);
    this.recargar();
  }

  onBuscarInput(texto: string) {
    this.busqueda.set(texto);
    this.pagina.set(1);
    if (this.busquedaTimerId) clearTimeout(this.busquedaTimerId);
    this.busquedaTimerId = setTimeout(() => this.recargar(), 300);
  }

  limpiarBusqueda() {
    this.busqueda.set('');
    this.pagina.set(1);
    this.recargar();
  }

  cambiarPagina(p: number) {
    this.pagina.set(p);
    this.recargar();
  }

  cambiarTamanoPagina(t: number) {
    this.tamanoPagina.set(t);
    this.recargar();
  }

  recargar(silencioso = false) {
    const version = ++this.versionRecarga;
    if (!silencioso) this.cargando.set(true);
    if (silencioso) this.cargarMetaMensual();
    this.error.set(null);
    const texto = this.busqueda().trim();
    const usandoFiltroFecha = !texto && this.filtro() === 'fecha';
    this.service.listar(
      texto ? undefined : this.filtro(),
      this.vista() === 'tablero' ? 1 : this.pagina(),
      this.vista() === 'tablero' ? 100 : this.tamanoPagina(),
      texto || undefined,
      usandoFiltroFecha && this.fechaFiltroDesde ? this.fechaFiltroDesde : undefined,
      usandoFiltroFecha && this.fechaFiltroHasta ? this.fechaFiltroHasta : undefined,
      usandoFiltroFecha ? this.tipoFechaConsulta() : undefined
    ).subscribe({
      next: res => {
        if (version !== this.versionRecarga) return;
        this.pedidos.set(res.items);
        this.total.set(res.total);
        this.cargando.set(false);
      },
      error: (err: HttpErrorResponse) => {
        if (version !== this.versionRecarga) return;
        this.cargando.set(false);
        if (!silencioso) {
          this.error.set(err.status === 0
            ? 'No se pudo conectar con el servidor. Verifica que el backend esté corriendo.'
            : (err.error?.mensaje ?? 'Error al cargar los pedidos.'));
        }
      }
    });
  }

  limpiarFiltroFecha() {
    this.fechaFiltroDesde = '';
    this.fechaFiltroHasta = '';
    this.tipoFechaConsulta.set('ingreso');
    this.cambiarFiltro('pendientes');
  }

  /**
   * Avance rápido desde la fila. Entregar (LISTO) es un momento de cobro,
   * así que va a la página del pedido donde está el flujo de cobro + entrega.
   */
  avanzarEtapa(p: Pedido) {
    if (this.flujoInconsistente(p)) {
      this.toast.advertencia(
        'El pedido está EN PROCESO pero no tiene un área actual. No se reinició el flujo; revisa su historial.'
      );
      return;
    }
    if (p.estadoProceso === 'LISTO' || p.estadoProceso === 'ENTREGA_PARCIAL') {
      this.router.navigate(['/pedidos', p.id]);
      return;
    }
    this.ejecutarAvance(p);
  }

  private ejecutarAvance(p: Pedido) {
    this.avanzando.set(true);
    this.avanzandoId.set(p.id);
    this.service.siguienteArea(p.id).subscribe({
      next: () => {
        this.avanzando.set(false);
        this.avanzandoId.set(null);
        this.destacarPedido(p.id);
        this.toast.exito('Etapa actualizada');
        this.recargar(true);
        // Si acaba de quedar LISTO, avisa automáticamente al cliente por WhatsApp.
        this.service.obtener(p.id).subscribe(actualizado => {
          if (!actualizado) return;
          this.pedidos.update(lista =>
            lista.map(item => item.id === actualizado.id ? actualizado : item)
          );
          if (actualizado.estadoProceso === 'LISTO' && actualizado.clienteCelular) {
            this.avisarListoAuto(actualizado);
          }
        });
      },
      error: (err: HttpErrorResponse) => {
        this.avanzando.set(false);
        this.avanzandoId.set(null);
        this.toast.desdeHttp(err, 'No se pudo avanzar la etapa.');
      }
    });
  }

  private refrescarDinamicamente() {
    if (typeof document !== 'undefined' && document.visibilityState !== 'visible') return;
    if (this.cargando() || this.avanzando()) return;
    this.recargar(true);
    this.cargarAbandonados();
  }

  private destacarPedido(id: number) {
    clearTimeout(this.pedidoAnimadoTimer);
    this.pedidoAnimadoId.set(id);
    this.pedidoAnimadoTimer = setTimeout(() => {
      if (this.pedidoAnimadoId() === id) this.pedidoAnimadoId.set(null);
    }, 1200);
  }

  /** Abre WhatsApp con el mensaje de "pedido listo" cuando el pedido acaba de terminar producción. */
  private avisarListoAuto(p: Pedido) {
    const cliente = (p.clienteNombre ?? '').trim().split(' ')[0] || 'cliente';
    const esDomicilio = p.modalidad === 'Recojo' || p.modalidad === 'Delivery';
    const fallback = esDomicilio
      ? `Hola ${cliente}! Tu pedido #${p.numero} ya está listo y saldrá a ruta.`
      : `Hola ${cliente}! Tu pedido #${p.numero} ya está listo para recoger en ${this.config.configuracion().nombreNegocio}. Te esperamos!`;
    const mensaje = this.whatsapp.mensaje('LISTO', {
      cliente, numero: String(p.numero), negocio: this.config.configuracion().nombreNegocio
    }, fallback);
    this.whatsapp.enviar(p.clienteCelular!, mensaje);
    this.toast.info(`Pedido #${p.numero} listo — se abrió WhatsApp para avisar a ${cliente}.`);
  }

  private tiempoRestanteMinutos(p: Pedido): number {
    const lista = this.areas();
    const idx = lista.findIndex(a => a.id === p.areaActualId);
    if (idx === -1) return 0;
    return lista.slice(idx).reduce((acc, a) => acc + a.tiempoEstMinutos, 0);
  }

  claseEstado(estado: string): string {
    return ({
      'PENDIENTE': 'badge badge--gris',
      'EN_PROCESO': 'badge badge--azul',
      'LISTO': 'badge badge--verde',
      'ENTREGA_PARCIAL': 'badge badge--ambar',
      'ENTREGADO': 'badge badge--verde-oscuro',
      'ANULADO': 'badge badge--rojo'
    } as Record<string, string>)[estado] ?? 'badge badge--gris';
  }

  etiquetaEstado(estado: string): string {
    return ({
      'PENDIENTE': 'Pendiente',
      'EN_PROCESO': 'En proceso',
      'LISTO': 'Listo para recojo',
      'ENTREGA_PARCIAL': 'Entrega parcial',
      'ENTREGADO': 'Entregado',
      'ANULADO': 'Anulado'
    } as Record<string, string>)[estado] ?? estado;
  }

  diasEnProceso(p: Pedido): number {
    const ms = Date.now() - new Date(p.fechaIngreso).getTime();
    return Math.max(0, Math.floor(ms / (24 * 60 * 60 * 1000)));
  }

  saldoPedido(p: Pedido): number {
    return Math.max(0, p.total - p.montoPagado);
  }

  puedeAvanzar(p: Pedido): boolean {
    return !['ENTREGADO', 'ANULADO', 'DONADO'].includes(p.estadoProceso) && !this.flujoInconsistente(p);
  }

  flujoInconsistente(p: Pedido): boolean {
    if (!['PENDIENTE', 'EN_PROCESO'].includes(p.estadoProceso)) return false;
    if (p.estadoProceso === 'EN_PROCESO' && p.areaActualId == null) return true;
    return p.areaActualId != null && !this.areas().some(a => a.id === p.areaActualId);
  }

  botonAvanzarLabel(p: Pedido): string {
    if (p.estadoProceso === 'ENTREGA_PARCIAL') return 'Entregar resto';
    if (p.estadoProceso === 'LISTO') return 'Marcar entregado';
    if (p.estadoProceso === 'PENDIENTE' && p.areaActualId == null) return 'Iniciar proceso';
    const areasList = this.areas();
    const idx = areasList.findIndex(a => a.id === p.areaActualId);
    if (idx === -1 || idx === areasList.length - 1) return 'Marcar listo';
    return `Pasar a "${areasList[idx + 1].nombre}"`;
  }

  kanbanActionLabel(p: Pedido): string {
    if (p.estadoProceso === 'ENTREGA_PARCIAL') return 'Entregar resto';
    if (p.estadoProceso === 'LISTO') return 'Entregar';
    if (p.estadoProceso === 'PENDIENTE' && p.areaActualId == null) return 'Iniciar';
    const areasList = this.areas();
    const idx = areasList.findIndex(a => a.id === p.areaActualId);
    if (idx === -1 || idx === areasList.length - 1) return 'Marcar listo';
    return `Mover a ${areasList[idx + 1].nombre}`;
  }

  entregaVencida(p: Pedido): boolean {
    if (!p.fechaEntregaEst || ['ENTREGADO', 'ANULADO'].includes(p.estadoProceso)) return false;
    return new Date(p.fechaEntregaEst).getTime() < Date.now();
  }

  /**
   * ¿Este pedido pertenece al subconjunto de la vista destacada activa? Para "fuera de tiempo" y
   * "esperando recojo" usamos los IDs exactos del dashboard (para que calce con el número de la
   * alerta); "listos" se resuelve por estado. Con respaldos razonables si los IDs aún no llegaron.
   */
  esDestacado(p: Pedido): boolean {
    switch (this.modoDestacado()) {
      case 'fuera-de-tiempo': {
        const ids = this.destacadosIds();
        return ids ? ids.has(p.id) : this.entregaVencida(p);
      }
      case 'abandonados': {
        const ids = this.destacadosIds();
        return ids ? ids.has(p.id) : (p.estadoProceso === 'LISTO' && !p.anulado);
      }
      case 'listos':
        return p.estadoProceso === 'LISTO' && !p.anulado;
      default:
        return true;
    }
  }

  private vistaInicial(): VistaPedidos {
    // Por defecto se abre en vista Lista. Si el usuario cambia a Tablero, se recuerda su elección.
    try {
      const guardada = localStorage.getItem('lavanderia.pedidos.vista');
      if (guardada === 'lista' || guardada === 'tablero') return guardada;
    } catch {}
    return 'lista';
  }

  abrirWhatsapp(p: Pedido) {
    if (!p.clienteCelular) {
      this.toast.advertencia('Este cliente no tiene celular registrado.');
      return;
    }
    const cliente = p.clienteNombre ?? '';
    const numero = String(p.numero);
    const fallback = `Hola ${cliente}, tu pedido #${p.numero} ${
      p.estadoProceso === 'LISTO' ? 'está listo para recoger' : `está en la etapa: ${p.areaActualNombre ?? 'Pendiente'}`
    }. ¡Gracias por tu preferencia!`;

    let mensaje: string;
    if (p.estadoProceso === 'ENTREGADO') {
      mensaje = this.whatsapp.mensaje('ENTREGADO', { cliente, numero, total: p.total.toFixed(2) }, fallback);
    } else if (p.estadoProceso === 'LISTO') {
      mensaje = this.whatsapp.mensaje('LISTO', { cliente, numero, negocio: this.config.configuracion().nombreNegocio }, fallback);
    } else {
      mensaje = this.whatsapp.mensaje('CAMBIO_AREA', {
        cliente, numero,
        area: p.areaActualNombre ?? 'Pendiente',
        tiempoRestante: `${this.tiempoRestanteMinutos(p)} min`,
      }, fallback);
    }
    this.whatsapp.enviar(p.clienteCelular, mensaje);
  }
}
