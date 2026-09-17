import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { debounceTime } from 'rxjs';
import { DISTRITOS_LIMA_CALLAO } from '../../core/constants/distritos-lima-callao';
import { AreaLavado, Cliente, Pedido, Servicio } from '../../core/models/models';
import { ClientesService } from '../../core/services/clientes.service';
import { esCelularValido } from '../../core/util/telefono';
import { TelefonoPaisComponent } from '../../shared/telefono-pais/telefono-pais.component';
import { ActualizacionDatosService } from '../../core/services/actualizacion-datos.service';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { FacturacionService } from '../../core/services/facturacion.service';
import { FotoPedido, FotosPedidoService, MomentoFoto } from '../../core/services/fotos-pedido.service';
import { Motorizado, MotorizadosService } from '../../core/services/motorizados.service';
import { PagoPedido, PedidoEntrega, PedidoHistorial, PedidosService } from '../../core/services/pedidos.service';
import { ToastService } from '../../core/services/toast.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { MapaUbicacionComponent, UbicacionMapa } from '../../shared/mapa-ubicacion/mapa-ubicacion.component';
import { SkeletonComponent } from '../../shared/skeleton/skeleton.component';
import { TourService } from '../../core/services/tour.service';
import { TOURS } from '../../core/constants/tours';

export type MetodoPagoEntrega = 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS';

/** Una fila del modal de entrega: un ítem del pedido con lo pendiente y cuánto se entrega ahora. */
export interface LineaEntregaItem {
  pedidoItemId: number;
  servicioNombre: string;
  unidad: string;
  pendiente: number;
  cantidad: number; // cuánto se entrega en esta visita
}

/** Una línea de cobro del modal de entrega (pago mixto). */
export interface LineaPagoEntrega {
  metodo: MetodoPagoEntrega;
  monto: number;
}

/**
 * Página dedicada del pedido (/pedidos/:id).
 *
 * Diseñada para el trabajador de mostrador: UNA acción principal según el estado
 * (avanzar / cobrar y entregar), el cobro siempre visible, y todo lo secundario
 * agrupado en "Más acciones" o colapsado (fotos, historial). Reemplaza al antiguo
 * modal sobrecargado de la lista de pedidos.
 */
@Component({
  selector: 'app-pedido-detalle',
  imports: [CommonModule, FormsModule, IconComponent, MapaUbicacionComponent, SkeletonComponent, TelefonoPaisComponent],
  templateUrl: './pedido-detalle.component.html',
  styleUrl: './pedido-detalle.component.scss'
})
export class PedidoDetalleComponent implements OnInit, OnDestroy {
  private readonly service = inject(PedidosService);
  private readonly clientesSvc = inject(ClientesService);
  private readonly catalogos = inject(CatalogosService);
  private readonly toast = inject(ToastService);
  private readonly whatsapp = inject(WhatsappService);
  private readonly config = inject(ConfiguracionService);
  private readonly facturacionSvc = inject(FacturacionService);
  private readonly motorizadosSvc = inject(MotorizadosService);
  private readonly actualizaciones = inject(ActualizacionDatosService);
  private readonly fotosSvc = inject(FotosPedidoService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly pedido = signal<Pedido | null>(null);
  readonly cargando = signal(true);
  readonly error = signal<string | null>(null);
  readonly areas = signal<AreaLavado[]>([]);
  readonly servicios = signal<Servicio[]>([]);
  readonly historial = signal<PedidoHistorial[]>([]);
  readonly cargandoHistorial = signal(false);
  readonly motorizadosActivos = signal<Motorizado[]>([]);

  readonly menuAcciones = signal(false);

  // Modales de acción
  readonly modalPago = signal(false);
  readonly modalEntrega = signal(false);
  readonly modalItem = signal(false);
  readonly modalFecha = signal(false);
  readonly modalAnular = signal(false);
  readonly modalDestinoDelivery = signal(false);

  // Formularios
  pagoMonto = 0;
  pagoMetodo: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';
  // Comprobante a emitir junto con el cobro (solo si el pago completa el total).
  pagoComprobante: 'NINGUNO' | 'BOLETA' | 'FACTURA' = 'BOLETA';
  recibidoPor = '';

  // --- Entrega (parcial o final) + cobro mixto ---
  readonly metodosPago: Array<{ v: MetodoPagoEntrega; nombre: string; icono: string }> = [
    { v: 'EFECTIVO', nombre: 'Efectivo', icono: 'cash' },
    { v: 'YAPE', nombre: 'Yape', icono: 'smartphone' },
    { v: 'PLIN', nombre: 'Plin', icono: 'smartphone' },
    { v: 'TRANSFERENCIA', nombre: 'Transferencia', icono: 'bank' },
    { v: 'POS', nombre: 'POS/Tarjeta', icono: 'credit-card' }
  ];
  /** Filas de ítems a entregar en esta visita (una por ítem con cantidad pendiente). */
  entregaItems = signal<LineaEntregaItem[]>([]);
  /** Líneas de cobro de esta visita (pago mixto). */
  entregaPagos = signal<LineaPagoEntrega[]>([]);
  entregaNota = '';
  /** Entregas ya registradas del pedido (parciales + final). */
  readonly entregas = signal<PedidoEntrega[]>([]);
  itemServicioId: number | '' = '';
  itemCantidad = 1;
  itemDescripcion = '';
  motivoAnulacion = '';
  fechaEntregaNueva = '';
  motivoCambioFecha = '';
  readonly procesando = signal(false);
  readonly avanzando = signal(false);
  readonly emitiendoComprobante = signal(false);
  // Régimen del negocio: si es "solo boletas" (RUS/NRUS), se oculta la opción de Factura.
  readonly soloBoletas = signal(false);

  // Delivery
  readonly convirtiendoDelivery = signal(false);
  readonly enviandoLinkPago = signal(false);
  readonly asignandoMotorizado = signal(false);
  readonly generandoLinkRepartidor = signal(false);
  readonly distritos = DISTRITOS_LIMA_CALLAO;
  direccionEntregaConversion = '';
  distritoEntregaConversion = '';
  referenciaEntregaConversion = '';
  latitudEntregaConversion: number | null = null;
  longitudEntregaConversion: number | null = null;
  ubicacionConversionConfirmada = false;

  // Fotos de evidencia
  readonly fotosPedido = signal<Array<FotoPedido & { url: string | null }>>([]);
  readonly cargandoFotos = signal(false);
  readonly subiendoFoto = signal(false);
  readonly momentoFotoNueva = signal<MomentoFoto>('RECEPCION');
  readonly fotoAmpliada = signal<string | null>(null);

  private pedidoId = 0;

  constructor() {
    // Cambios hechos por otros usuarios/pestañas: refresca este pedido en silencio.
    this.actualizaciones.cambios('pedidos', 'foco').pipe(
      debounceTime(200),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => {
      if (!this.procesando() && !this.avanzando()) this.refrescar(true);
    });
  }

  ngOnInit() {
    this.catalogos.areasLavado().subscribe(a => this.areas.set(a));
    this.catalogos.servicios().subscribe(s => this.servicios.set(s));
    this.whatsapp.cargar();
    this.motorizadosSvc.listarActivos().subscribe(m => this.motorizadosActivos.set(m));
    this.facturacionSvc.estado().subscribe({ next: e => this.soloBoletas.set(e.soloBoletas), error: () => {} });

    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const id = Number(params.get('id'));
      if (!Number.isInteger(id) || id <= 0) { this.volver(); return; }
      this.pedidoId = id;
      this.cargar();
    });
  }

  ngOnDestroy() { this.liberarUrlsFotos(); }

  volver() { this.router.navigate(['/pedidos']); }

  private readonly tour = inject(TourService);
  iniciarTour() { this.tour.iniciar(TOURS['pedido-detalle']); }

  /** Cobros del pedido (adelantos y saldos) con su método de pago. */
  readonly pagos = signal<PagoPedido[]>([]);

  readonly etiquetasMetodoPago: Record<string, string> = {
    EFECTIVO: 'Efectivo', YAPE: 'Yape', PLIN: 'Plin',
    TRANSFERENCIA: 'Transferencia', POS: 'POS/Tarjeta', TARJETA: 'Tarjeta'
  };

  etiquetaMetodo(metodo: string): string {
    return this.etiquetasMetodoPago[metodo] ?? metodo;
  }

  private cargarPagos() {
    this.service.pagos(this.pedidoId).subscribe({
      next: list => this.pagos.set(list),
      error: () => this.pagos.set([])
    });
  }

  private cargarEntregas() {
    this.service.entregas(this.pedidoId).subscribe({
      next: list => this.entregas.set(list),
      error: () => this.entregas.set([])
    });
  }

  private cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.service.obtener(this.pedidoId).subscribe({
      next: p => { this.pedido.set(p); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 404 ? 'Este pedido no existe o pertenece a otra sede.' : 'No se pudo cargar el pedido.');
      }
    });
    this.cargandoHistorial.set(true);
    this.service.historial(this.pedidoId).subscribe({
      next: h => { this.historial.set(h); this.cargandoHistorial.set(false); },
      error: () => this.cargandoHistorial.set(false)
    });
    this.cargarPagos();
    this.cargarEntregas();
    this.cargarFotos();
  }

  private refrescar(silencioso = false) {
    if (!this.pedidoId) return;
    this.service.obtener(this.pedidoId).subscribe({
      next: p => this.pedido.set(p),
      error: () => { if (!silencioso) this.toast.error('No se pudo actualizar el pedido.'); }
    });
    this.service.historial(this.pedidoId).subscribe(h => this.historial.set(h));
    this.cargarPagos();
    this.cargarEntregas();
  }

  // ---------- Derivados ----------
  readonly saldoPendiente = computed(() => {
    const p = this.pedido();
    return p ? Math.max(0, p.total - p.montoPagado) : 0;
  });

  readonly esDomicilio = computed(() => {
    const m = this.pedido()?.modalidad;
    return m === 'Recojo' || m === 'Delivery';
  });

  progresoAreas(p: Pedido): { nombre: string; alcanzada: boolean; actual: boolean; id: string }[] {
    const areasList = this.areas();
    if (areasList.length === 0) return [];
    const idxActual = areasList.findIndex(a => a.id === p.areaActualId);
    const estaEntregado = p.estadoProceso === 'ENTREGADO';
    const estaListo = p.estadoProceso === 'LISTO';

    const pasos = areasList.map((a, i) => ({
      id: `a-${a.id}`,
      nombre: a.nombre,
      alcanzada: estaListo || estaEntregado || (idxActual >= 0 && i <= idxActual),
      actual: !estaListo && !estaEntregado && a.id === p.areaActualId
    }));
    pasos.push({
      id: 'entregado',
      nombre: estaEntregado ? 'Entregado' : 'Entrega',
      alcanzada: estaEntregado,
      actual: estaListo
    });
    return pasos;
  }

  flujoInconsistente(p: Pedido): boolean {
    if (!['PENDIENTE', 'EN_PROCESO'].includes(p.estadoProceso)) return false;
    if (p.estadoProceso === 'EN_PROCESO' && p.areaActualId == null) return true;
    return p.areaActualId != null && !this.areas().some(a => a.id === p.areaActualId);
  }

  puedeAvanzar(p: Pedido): boolean {
    return !p.anulado && !['ENTREGADO', 'ANULADO', 'DONADO'].includes(p.estadoProceso) && !this.flujoInconsistente(p);
  }

  /** Texto de LA acción principal: el sistema le dice al trabajador qué toca hacer ahora. */
  accionPrincipalLabel(p: Pedido): string {
    if (p.estadoProceso === 'LISTO' || p.estadoProceso === 'ENTREGA_PARCIAL') {
      const verbo = p.estadoProceso === 'ENTREGA_PARCIAL' ? 'Entregar resto' : 'Entregar';
      return this.saldoPendiente() > 0.01
        ? `${verbo} / cobrar (falta S/ ${this.saldoPendiente().toFixed(2)})`
        : `${verbo} pedido`;
    }
    if (p.estadoProceso === 'PENDIENTE' && p.areaActualId == null) return 'Iniciar proceso';
    const areasList = this.areas();
    const idx = areasList.findIndex(a => a.id === p.areaActualId);
    if (idx === -1 || idx === areasList.length - 1) return 'Marcar listo';
    return `Avanzar a ${areasList[idx + 1].nombre}`;
  }

  accionPrincipal(p: Pedido) {
    if (this.flujoInconsistente(p)) {
      this.toast.advertencia('El pedido está EN PROCESO pero no tiene un área actual. Revisa su historial.');
      return;
    }
    if (p.estadoProceso === 'LISTO' || p.estadoProceso === 'ENTREGA_PARCIAL') {
      this.abrirModalEntrega(p);
      return;
    }
    this.ejecutarAvance(p);
  }

  /** Prepara el modal de entrega: una fila por ítem con lo pendiente y una línea de cobro con el saldo. */
  abrirModalEntrega(p: Pedido) {
    const filas: LineaEntregaItem[] = (p.items ?? [])
      .map(it => {
        const pendiente = Math.round((it.cantidad - (it.cantidadEntregada ?? 0)) * 100) / 100;
        return {
          pedidoItemId: it.id!,
          servicioNombre: it.servicioNombre ?? 'Servicio',
          unidad: it.servicioUnidad ?? '',
          pendiente,
          cantidad: pendiente // por defecto se entrega todo lo pendiente
        };
      })
      .filter(f => f.pendiente > 0.001);

    const saldo = this.saldoPendiente();
    this.entregaItems.set(filas);
    this.entregaPagos.set(saldo > 0.01 ? [{ metodo: 'EFECTIVO', monto: Math.round(saldo * 100) / 100 }] : []);
    this.entregaNota = '';
    this.recibidoPor = '';
    this.modalEntrega.set(true);
  }

  // --- Derivados del modal de entrega ---
  /** Total a cobrar en esta visita (suma de las líneas de pago). */
  totalCobroEntrega(): number {
    return Math.round(this.entregaPagos().reduce((s, l) => s + (Number(l.monto) || 0), 0) * 100) / 100;
  }
  /** Saldo que quedaría por cobrar después de este cobro. */
  saldoRestanteEntrega(): number {
    return Math.round((this.saldoPendiente() - this.totalCobroEntrega()) * 100) / 100;
  }
  /** ¿Se están entregando prendas en esta visita? */
  hayItemsEntrega(): boolean {
    return this.entregaItems().some(f => (Number(f.cantidad) || 0) > 0.001);
  }
  /** Tras esta entrega, ¿queda todo el pedido entregado? (define si es entrega final). */
  esEntregaFinal(): boolean {
    const filas = this.entregaItems();
    if (filas.length === 0) return true; // ya no queda nada pendiente por entregar
    return filas.every(f => (Number(f.cantidad) || 0) >= f.pendiente - 0.001);
  }
  agregarLineaPago() {
    // Sugiere el método menos usado aún y el saldo restante como monto.
    const usados = new Set(this.entregaPagos().map(l => l.metodo));
    const libre = this.metodosPago.find(m => !usados.has(m.v))?.v ?? 'EFECTIVO';
    const restante = Math.max(0, this.saldoRestanteEntrega());
    this.entregaPagos.update(ls => [...ls, { metodo: libre, monto: Math.round(restante * 100) / 100 }]);
  }
  quitarLineaPago(i: number) {
    this.entregaPagos.update(ls => ls.filter((_, idx) => idx !== i));
  }
  setMetodoPago(i: number, metodo: MetodoPagoEntrega) {
    this.entregaPagos.update(ls => ls.map((l, idx) => idx === i ? { ...l, metodo } : l));
  }
  setMontoPago(i: number, monto: number) {
    this.entregaPagos.update(ls => ls.map((l, idx) => idx === i ? { ...l, monto } : l));
  }
  setCantidadItem(i: number, cantidad: number) {
    this.entregaItems.update(fs => fs.map((f, idx) => idx === i ? { ...f, cantidad } : f));
  }
  entregarTodoItem(i: number) {
    this.entregaItems.update(fs => fs.map((f, idx) => idx === i ? { ...f, cantidad: f.pendiente } : f));
  }

  cerrarModalEntrega() {
    this.modalEntrega.set(false);
    this.entregaItems.set([]);
    this.entregaPagos.set([]);
    this.entregaNota = '';
  }

  // ---------- Editar datos del cliente ----------
  readonly modalCliente = signal(false);
  readonly cargandoCliente = signal(false);
  readonly guardandoCliente = signal(false);
  clienteForm: Partial<Cliente> = {};

  abrirEditarCliente() {
    const p = this.pedido();
    if (!p) return;
    // Base inmediata desde el pedido (por si falla la carga completa).
    this.clienteForm = { id: p.clienteId, nombre: p.clienteNombre ?? '', celular: p.clienteCelular ?? '', dni: p.clienteDni ?? '' };
    this.modalCliente.set(true);
    this.cargandoCliente.set(true);
    this.clientesSvc.obtener(p.clienteId).subscribe({
      next: c => { this.clienteForm = { ...c }; this.cargandoCliente.set(false); },
      error: () => { this.cargandoCliente.set(false); } // se queda con la base del pedido
    });
  }

  cerrarModalCliente() { if (!this.guardandoCliente()) this.modalCliente.set(false); }

  guardarCliente() {
    const p = this.pedido();
    if (!p || this.guardandoCliente()) return;
    const nombre = (this.clienteForm.nombre ?? '').toString().trim();
    if (!nombre) { this.toast.advertencia('Ingresa el nombre del cliente.'); return; }
    if (!esCelularValido(this.clienteForm.celular)) {
      this.toast.advertencia('El celular no es válido. Usa 9 dígitos (Perú) o el código de país para el extranjero.');
      return;
    }
    const aNull = (v: unknown) => { const t = (v ?? '').toString().trim(); return t ? t : null; };
    const payload: Partial<Cliente> = {
      ...this.clienteForm,
      nombre,
      celular: aNull(this.clienteForm.celular),
      dni: aNull(this.clienteForm.dni),
      documentoFiscal: aNull(this.clienteForm.documentoFiscal),
      direccion: aNull(this.clienteForm.direccion),
      fechaNacimiento: aNull(this.clienteForm.fechaNacimiento),
    };
    this.guardandoCliente.set(true);
    this.clientesSvc.actualizar(p.clienteId, payload).subscribe({
      next: () => {
        this.guardandoCliente.set(false);
        this.modalCliente.set(false);
        this.toast.exito('Datos del cliente actualizados');
        this.refrescar(); // el nombre/celular del pedido se recargan desde el servidor
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoCliente.set(false);
        this.toast.desdeHttp(err, 'No se pudo actualizar el cliente.');
      }
    });
  }

  confirmarEntregaNueva() {
    const p = this.pedido();
    if (!p || this.procesando()) return;

    const items = this.entregaItems()
      .filter(f => (Number(f.cantidad) || 0) > 0.001)
      .map(f => ({ pedidoItemId: f.pedidoItemId, cantidad: Math.round(Number(f.cantidad) * 100) / 100 }));

    // Validación de cantidades contra lo pendiente.
    for (const f of this.entregaItems()) {
      const c = Number(f.cantidad) || 0;
      if (c < 0) { this.toast.advertencia('Las cantidades no pueden ser negativas.'); return; }
      if (c > f.pendiente + 0.01) {
        this.toast.advertencia(`De "${f.servicioNombre}" solo quedan ${f.pendiente} por entregar.`);
        return;
      }
    }

    const pagos = this.entregaPagos()
      .filter(l => (Number(l.monto) || 0) > 0)
      .map(l => ({ metodo: l.metodo, monto: Math.round(Number(l.monto) * 100) / 100 }));

    const totalCobro = this.totalCobroEntrega();
    if (totalCobro > this.saldoPendiente() + 0.01) {
      this.toast.advertencia(`El total a cobrar excede el saldo pendiente (S/ ${this.saldoPendiente().toFixed(2)}).`);
      return;
    }
    if (items.length === 0 && pagos.length === 0) {
      this.toast.advertencia('Indica qué prendas se entregan y/o registra un cobro.');
      return;
    }
    if (this.recibidoPor.trim().length > 120) {
      this.toast.advertencia('El nombre de quien recibe no puede superar 120 caracteres.');
      return;
    }

    const nombreTercero = this.recibidoPor.trim();
    const titular = (p.clienteNombre ?? '').trim();
    const recibidoPor = nombreTercero && nombreTercero.toLowerCase() !== titular.toLowerCase() ? nombreTercero : null;

    this.procesando.set(true);
    this.service.entregar(p.id, { items, pagos, recibidoPor, nota: this.entregaNota.trim() || null }).subscribe({
      next: res => {
        this.procesando.set(false);
        this.cerrarModalEntrega();
        const restante = Math.max(0, this.saldoPendiente() - totalCobro);
        if (res.estadoProceso === 'ENTREGADO') {
          this.toast.exito(restante > 0.01
            ? `Pedido #${p.numero} entregado. Queda S/ ${restante.toFixed(2)} por cobrar.`
            : `Pedido #${p.numero} entregado`);
        } else {
          this.toast.exito(`Entrega parcial registrada${restante > 0.01 ? ` — queda S/ ${restante.toFixed(2)} por cobrar` : ''}`);
        }
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.desdeHttp(err, 'No se pudo registrar la entrega.');
      }
    });
  }

  private ejecutarAvance(p: Pedido) {
    this.avanzando.set(true);
    this.service.siguienteArea(p.id).subscribe({
      next: () => {
        this.avanzando.set(false);
        this.toast.exito('Etapa actualizada');
        this.service.obtener(p.id).subscribe(actualizado => {
          this.pedido.set(actualizado);
          this.service.historial(p.id).subscribe(h => this.historial.set(h));
          if (actualizado.estadoProceso === 'LISTO' && actualizado.clienteCelular) {
            this.avisarListoAuto(actualizado);
          }
        });
      },
      error: (err: HttpErrorResponse) => {
        this.avanzando.set(false);
        this.toast.desdeHttp(err, 'No se pudo avanzar la etapa.');
      }
    });
  }

  private avisarListoAuto(p: Pedido) {
    const cliente = (p.clienteNombre ?? '').trim().split(' ')[0] || 'cliente';
    const fallback = this.esDomicilio()
      ? `Hola ${cliente}! Tu pedido #${p.numero} ya está listo y saldrá a ruta.`
      : `Hola ${cliente}! Tu pedido #${p.numero} ya está listo para recoger en ${this.config.configuracion().nombreNegocio}. Te esperamos!`;
    const mensaje = this.whatsapp.mensaje('LISTO', {
      cliente, numero: String(p.numero), negocio: this.config.configuracion().nombreNegocio
    }, fallback);
    this.whatsapp.enviar(p.clienteCelular!, mensaje);
    this.toast.info(`Pedido #${p.numero} listo — se abrió WhatsApp para avisar a ${cliente}.`);
  }

  // ---------- WhatsApp contextual ----------
  abrirWhatsapp(p: Pedido) {
    if (!p.clienteCelular) { this.toast.advertencia('Este cliente no tiene celular registrado.'); return; }
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
        cliente, numero, area: p.areaActualNombre ?? 'Pendiente', tiempoRestante: `${this.tiempoRestanteMinutos(p)} min`,
      }, fallback);
    }
    this.whatsapp.enviar(p.clienteCelular, mensaje);
  }

  private tiempoRestanteMinutos(p: Pedido): number {
    const lista = this.areas();
    const idx = lista.findIndex(a => a.id === p.areaActualId);
    if (idx === -1) return 0;
    return lista.slice(idx).reduce((acc, a) => acc + a.tiempoEstMinutos, 0);
  }

  enviarLinkPago(p: Pedido) {
    if (this.enviandoLinkPago() || !p.clienteCelular) {
      if (!p.clienteCelular) this.toast.advertencia('Este cliente no tiene celular registrado.');
      return;
    }
    this.enviandoLinkPago.set(true);
    this.service.linkSeguimiento(p.id).subscribe({
      next: ({ token }) => {
        this.enviandoLinkPago.set(false);
        const url = `${window.location.origin}/seguimiento/${token}`;
        const mensaje = this.whatsapp.mensajeIngreso(p, this.config.configuracion(), url);
        this.whatsapp.enviar(p.clienteCelular!, mensaje);
      },
      error: (err: HttpErrorResponse) => {
        this.enviandoLinkPago.set(false);
        this.toast.desdeHttp(err, 'No se pudo generar el enlace de seguimiento.');
      }
    });
  }

  avisarEnCaminoCliente(p: Pedido) {
    if (!p.clienteCelular) { this.toast.advertencia('Este cliente no tiene celular registrado.'); return; }
    if (this.enviandoLinkPago()) return;
    this.enviandoLinkPago.set(true);
    this.service.linkSeguimiento(p.id).subscribe({
      next: ({ token }) => {
        this.enviandoLinkPago.set(false);
        const url = `${window.location.origin}/seguimiento/${token}`;
        const cliente = (p.clienteNombre || 'cliente').trim();
        const negocio = this.config.configuracion().nombreNegocio || 'la lavandería';
        const fallback = `Hola ${cliente}! Tu pedido #${p.numero} de ${negocio} ya va en camino a tu dirección. Sigue al repartidor en tiempo real aquí:\n${url}`;
        const mensaje = this.whatsapp.mensaje('EN_RUTA', { cliente, numero: String(p.numero), negocio, seguimiento: url }, fallback);
        this.whatsapp.enviar(p.clienteCelular!, mensaje.includes(url) ? mensaje : `${mensaje}\n${url}`);
      },
      error: (err: HttpErrorResponse) => {
        this.enviandoLinkPago.set(false);
        this.toast.desdeHttp(err, 'No se pudo generar el enlace de seguimiento.');
      }
    });
  }

  // ---------- Repartidor ----------
  asignarMotorizado(p: Pedido, motorizadoIdTexto: string) {
    if (this.asignandoMotorizado()) return;
    const motorizadoId = motorizadoIdTexto ? Number(motorizadoIdTexto) : null;
    this.asignandoMotorizado.set(true);
    this.service.asignarMotorizado(p.id, motorizadoId).subscribe({
      next: () => {
        this.asignandoMotorizado.set(false);
        this.toast.exito(motorizadoId ? 'Repartidor asignado' : 'Repartidor quitado del pedido');
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.asignandoMotorizado.set(false);
        this.toast.desdeHttp(err, 'No se pudo asignar el repartidor.');
      }
    });
  }

  private conLinkRepartidor(p: Pedido, accion: (url: string) => void) {
    if (this.generandoLinkRepartidor()) return;
    this.generandoLinkRepartidor.set(true);
    this.service.linkRepartidor(p.id).subscribe({
      next: ({ token }) => {
        this.generandoLinkRepartidor.set(false);
        accion(`${window.location.origin}/repartidor/${token}`);
      },
      error: (err: HttpErrorResponse) => {
        this.generandoLinkRepartidor.set(false);
        this.toast.desdeHttp(err, 'No se pudo generar el enlace del repartidor.');
      }
    });
  }

  enviarLinkRepartidor(p: Pedido) {
    if (!p.motorizadoId) { this.toast.advertencia('Primero asigna un repartidor al pedido.'); return; }
    this.conLinkRepartidor(p, url => {
      const cliente = (p.clienteNombre || 'el cliente').trim();
      const destino = [p.direccionEntrega, p.distritoEntrega].filter(Boolean).join(', ');
      const mensaje = `🛵 *Reparto — Pedido #${p.numero}*\n\nCliente: ${cliente}\nDirección: ${destino || 'ver en el mapa'}\n\nAbre este enlace en tu celular para compartir tu ubicación en vivo y marcar la entrega:\n${url}`;
      if (p.motorizadoCelular) this.whatsapp.enviar(p.motorizadoCelular, mensaje);
      else this.copiarTexto(url, 'El repartidor no tiene celular. Copié el enlace para que se lo pases.');
    });
  }

  copiarLinkRepartidor(p: Pedido) {
    this.conLinkRepartidor(p, url => this.copiarTexto(url, 'Enlace del repartidor copiado.'));
  }

  private copiarTexto(texto: string, ok: string) {
    navigator.clipboard?.writeText(texto)
      .then(() => this.toast.exito(ok))
      .catch(() => this.toast.advertencia('No se pudo copiar. Enlace: ' + texto));
  }

  urlMapaPedido(p: Pedido): string | null {
    if (p.latitudEntrega == null || p.longitudEntrega == null) return null;
    return `https://www.openstreetmap.org/?mlat=${p.latitudEntrega}&mlon=${p.longitudEntrega}#map=18/${p.latitudEntrega}/${p.longitudEntrega}`;
  }

  // ---------- Pago ----------
  abrirModalPago() {
    const p = this.pedido();
    if (!p) return;
    this.pagoMonto = Math.max(0, p.total - p.montoPagado);
    this.pagoMetodo = 'EFECTIVO';
    this.pagoComprobante = 'BOLETA';
    this.modalPago.set(true);
  }

  confirmarPago() {
    const p = this.pedido();
    const saldo = this.saldoPendiente();
    if (!p || this.procesando()) return;
    if (!Number.isFinite(this.pagoMonto) || this.pagoMonto <= 0 || this.pagoMonto > saldo + 0.01) {
      this.toast.advertencia(`Ingresa un monto válido de hasta S/ ${saldo.toFixed(2)}.`);
      return;
    }
    this.pagoMonto = Math.round(this.pagoMonto * 100) / 100;
    // El comprobante solo se emite si este pago deja el pedido totalmente pagado.
    const completaPago = this.pagoMonto >= saldo - 0.01;
    const emitirTipo = completaPago ? this.pagoComprobante : 'NINGUNO';
    this.procesando.set(true);
    this.service.registrarPago(p.id, this.pagoMonto, this.pagoMetodo).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalPago.set(false);
        this.toast.exito(`Pago de S/ ${this.pagoMonto.toFixed(2)} registrado`);
        if (emitirTipo === 'BOLETA' || emitirTipo === 'FACTURA') this.emitirComprobante(emitirTipo);
        else this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.desdeHttp(err, 'No se pudo registrar el pago.');
      }
    });
  }

  /** El monto ingresado deja el pedido totalmente pagado. */
  pagoCompletaTotal(): boolean { return this.pagoMonto >= this.saldoPendiente() - 0.01; }
  /** Texto del botón de cobro según si además se emitirá comprobante. */
  textoBotonPago(): string {
    if (this.procesando()) return 'Procesando…';
    if (!this.pagoCompletaTotal() || this.pagoComprobante === 'NINGUNO') return 'Confirmar pago';
    return this.pagoComprobante === 'BOLETA' ? 'Cobrar y emitir boleta' : 'Cobrar y emitir factura';
  }

  // ---------- Ítems ----------
  abrirModalItem() {
    this.itemServicioId = '';
    this.itemCantidad = 1;
    this.itemDescripcion = '';
    this.modalItem.set(true);
  }

  confirmarAgregarItem() {
    const p = this.pedido();
    if (!p || this.procesando() || !this.itemServicioId) return;
    if (!Number.isFinite(this.itemCantidad) || this.itemCantidad <= 0 || this.itemCantidad > 10_000) {
      this.toast.advertencia('La cantidad debe ser mayor a 0 y no superar 10,000.');
      return;
    }
    if (this.itemDescripcion.trim().length > 200) {
      this.toast.advertencia('La observación no puede superar 200 caracteres.');
      return;
    }
    this.procesando.set(true);
    this.service.agregarItem(p.id, this.itemServicioId as number, this.itemCantidad, this.itemDescripcion.trim() || undefined).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalItem.set(false);
        this.toast.exito('Ítem agregado');
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.desdeHttp(err, 'No se pudo agregar el ítem.');
      }
    });
  }

  // ---------- Cambiar fecha ----------
  abrirModalFecha() {
    const p = this.pedido();
    if (!p) return;
    const base = p.fechaEntregaEst ? new Date(p.fechaEntregaEst) : new Date(Date.now() + 2 * 60 * 60 * 1000);
    this.fechaEntregaNueva = this.formatoLocal(base);
    this.motivoCambioFecha = '';
    this.menuAcciones.set(false);
    this.modalFecha.set(true);
  }

  presetFechaEntrega(horas: number) {
    this.fechaEntregaNueva = this.formatoLocal(new Date(Date.now() + horas * 60 * 60 * 1000));
  }

  presetFechaMananaHora(hora: number) {
    const d = new Date();
    d.setDate(d.getDate() + 1);
    d.setHours(hora, 0, 0, 0);
    this.fechaEntregaNueva = this.formatoLocal(d);
  }

  private formatoLocal(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  get fechaEntregaMinima(): string {
    return this.formatoLocal(new Date(Date.now() + 5 * 60 * 1000));
  }

  confirmarCambioFecha() {
    const p = this.pedido();
    if (!p || this.procesando() || !this.fechaEntregaNueva) return;
    const nuevaFecha = new Date(this.fechaEntregaNueva);
    if (Number.isNaN(nuevaFecha.getTime()) || nuevaFecha.getTime() < Date.now() + 4 * 60 * 1000) {
      this.toast.advertencia('La nueva fecha de entrega debe ser posterior al momento actual.');
      return;
    }
    if (this.motivoCambioFecha.trim().length > 200) {
      this.toast.advertencia('El motivo no puede superar 200 caracteres.');
      return;
    }
    this.procesando.set(true);
    // Se envía en hora LOCAL (naive), no toISOString() (UTC), para que la nueva hora no se
    // desfase ~5h respecto a lo que se ve en el ticket/detalle (mismo criterio que FechaIngreso).
    this.service.cambiarFechaEntrega(p.id, this.fechaEntregaNueva, this.motivoCambioFecha.trim() || undefined).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalFecha.set(false);
        this.toast.exito('Fecha de entrega actualizada');
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.desdeHttp(err, 'No se pudo actualizar la fecha.');
      }
    });
  }

  avisarCambioFechaWhatsapp(p: Pedido) {
    if (!p.clienteCelular) { this.toast.advertencia('Este cliente no tiene celular registrado.'); return; }
    const fecha = p.fechaEntregaEst
      ? new Date(p.fechaEntregaEst).toLocaleString('es-PE', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })
      : 'próximamente';
    const mensaje = this.whatsapp.mensaje('DEMORA', {
      cliente: p.clienteNombre ?? '', numero: String(p.numero), entrega: fecha,
    }, `Hola ${p.clienteNombre}, hemos actualizado la hora ${p.modalidad === 'Delivery' ? 'de entrega' : 'de recojo'} de tu pedido #${p.numero}. Nueva hora: ${fecha}. Disculpa las molestias.`);
    this.whatsapp.enviar(p.clienteCelular, mensaje);
  }

  // ---------- Delivery (convertir / editar destino) ----------
  abrirDestinoDelivery(p: Pedido) {
    this.direccionEntregaConversion = p.direccionEntrega ?? '';
    this.distritoEntregaConversion = p.distritoEntrega ?? '';
    this.referenciaEntregaConversion = p.referenciaEntrega ?? '';
    this.latitudEntregaConversion = p.latitudEntrega ?? null;
    this.longitudEntregaConversion = p.longitudEntrega ?? null;
    this.ubicacionConversionConfirmada = p.latitudEntrega != null && p.longitudEntrega != null;
    this.menuAcciones.set(false);
    this.modalDestinoDelivery.set(true);
  }

  cerrarDestinoDelivery() {
    if (!this.convirtiendoDelivery()) this.modalDestinoDelivery.set(false);
  }

  actualizarUbicacionConversion(ubicacion: UbicacionMapa | null) {
    this.latitudEntregaConversion = ubicacion?.latitud ?? null;
    this.longitudEntregaConversion = ubicacion?.longitud ?? null;
    this.ubicacionConversionConfirmada = !!ubicacion?.etiqueta;
    if (ubicacion?.direccion) this.direccionEntregaConversion = ubicacion.direccion;
    if (ubicacion?.distrito) this.distritoEntregaConversion = ubicacion.distrito;
  }

  actualizarDireccionConversion(valor: string) {
    if (valor !== this.direccionEntregaConversion) this.ubicacionConversionConfirmada = false;
    this.direccionEntregaConversion = valor;
  }

  actualizarDistritoConversion(valor: string) {
    if (valor !== this.distritoEntregaConversion) this.ubicacionConversionConfirmada = false;
    this.distritoEntregaConversion = valor;
  }

  confirmarConversionDelivery() {
    const p = this.pedido();
    if (!p || this.convirtiendoDelivery()) return;
    if (!this.direccionEntregaConversion.trim() || !this.distritoEntregaConversion) {
      this.toast.advertencia('Completa la dirección exacta y el distrito de entrega.');
      return;
    }
    if (this.latitudEntregaConversion === null || this.longitudEntregaConversion === null || !this.ubicacionConversionConfirmada) {
      this.toast.advertencia('Confirma la dirección y el punto exacto en el mapa.');
      return;
    }
    this.convirtiendoDelivery.set(true);
    this.service.convertirDelivery(p.id, {
      direccionEntrega: this.direccionEntregaConversion.trim(),
      distritoEntrega: this.distritoEntregaConversion,
      referenciaEntrega: this.referenciaEntregaConversion.trim() || null,
      latitudEntrega: this.latitudEntregaConversion,
      longitudEntrega: this.longitudEntregaConversion
    }).subscribe({
      next: () => {
        const fueConversion = p.modalidad !== 'Delivery';
        this.convirtiendoDelivery.set(false);
        this.modalDestinoDelivery.set(false);
        this.toast.exito(fueConversion
          ? `Pedido #${p.numero} convertido a Delivery`
          : `Destino del pedido #${p.numero} actualizado`);
        this.refrescar();
        // Al convertir se agregó la tarifa de domicilio, así que el total y el ticket cambiaron:
        // se ofrece reimprimir el ticket actualizado.
        if (fueConversion && confirm('Se agregó la tarifa de domicilio y el total cambió. ¿Reimprimir el ticket actualizado?')) {
          this.imprimirTicket(p);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.convirtiendoDelivery.set(false);
        this.toast.desdeHttp(err, 'No se pudo convertir el pedido a Delivery.');
      }
    });
  }

  // ---------- Anular ----------
  abrirModalAnular() {
    this.motivoAnulacion = '';
    this.menuAcciones.set(false);
    this.modalAnular.set(true);
  }

  confirmarAnular() {
    const p = this.pedido();
    if (!p || this.motivoAnulacion.trim().length < 3) return;
    this.procesando.set(true);
    this.service.anular(p.id, this.motivoAnulacion.trim()).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalAnular.set(false);
        this.toast.advertencia(`Pedido #${p.numero} anulado`);
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.desdeHttp(err, 'No se pudo anular el pedido.');
      }
    });
  }

  // ---------- Comprobantes / ticket ----------
  emitirComprobante(tipo: 'BOLETA' | 'FACTURA') {
    const p = this.pedido();
    if (!p || this.emitiendoComprobante()) return;
    this.menuAcciones.set(false);
    this.emitiendoComprobante.set(true);
    this.facturacionSvc.emitirComprobante(p.id, tipo).subscribe({
      next: c => {
        this.emitiendoComprobante.set(false);
        // PENDIENTE es el estado normal de APISUNAT: el comprobante se encoló y SUNAT lo acepta en
        // segundos (el listado sincroniza solo). Solo RECHAZADO/ERROR son fallas reales.
        if (c.estado === 'ACEPTADO') this.toast.exito(`${c.numeroCompleto} emitido y aceptado por SUNAT.`);
        else if (c.estado === 'PENDIENTE') this.toast.info(`${c.numeroCompleto} enviado a SUNAT. Se confirmará en unos segundos (revisa Comprobantes).`);
        else if (c.estado === 'SIMULADO') this.toast.exito(`${c.numeroCompleto} generado (documento de prueba).`);
        else this.toast.error(`${c.numeroCompleto}: ${c.descripcionRespuestaSunat ?? c.estado}`);
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.emitiendoComprobante.set(false);
        this.toast.desdeHttp(err, 'No se pudo emitir el comprobante.');
      }
    });
  }

  imprimirTicket(p: Pedido) {
    this.menuAcciones.set(false);
    window.open(`/ticket/${p.id}`, '_blank');
  }

  // ---------- Fotos ----------
  private liberarUrlsFotos() {
    for (const f of this.fotosPedido()) { if (f.url) URL.revokeObjectURL(f.url); }
  }

  private cargarFotos() {
    this.liberarUrlsFotos();
    this.fotosPedido.set([]);
    this.cargandoFotos.set(true);
    this.fotosSvc.listar(this.pedidoId).subscribe({
      next: fotos => {
        this.fotosPedido.set(fotos.map(f => ({ ...f, url: null })));
        this.cargandoFotos.set(false);
        for (const f of fotos) {
          this.fotosSvc.urlArchivo(this.pedidoId, f.id).subscribe({
            next: url => this.fotosPedido.update(lista => lista.map(x => x.id === f.id ? { ...x, url } : x)),
            error: () => {}
          });
        }
      },
      error: () => this.cargandoFotos.set(false)
    });
  }

  async onFotoSeleccionada(evento: Event) {
    const input = evento.target as HTMLInputElement;
    const archivo = input.files?.[0];
    const p = this.pedido();
    if (!archivo || !p) return;
    if (!archivo.type.startsWith('image/')) {
      this.toast.advertencia('Selecciona una imagen.');
      input.value = '';
      return;
    }
    this.subiendoFoto.set(true);
    try {
      const comprimida = await this.fotosSvc.comprimir(archivo);
      this.fotosSvc.subir(p.id, comprimida, this.momentoFotoNueva()).subscribe({
        next: () => { this.subiendoFoto.set(false); this.toast.exito('Foto agregada'); this.cargarFotos(); },
        error: (err: HttpErrorResponse) => { this.subiendoFoto.set(false); this.toast.desdeHttp(err, 'No se pudo subir la foto.'); }
      });
    } catch {
      this.subiendoFoto.set(false);
      this.toast.error('No se pudo procesar la imagen.');
    }
    input.value = '';
  }

  eliminarFoto(fotoId: number) {
    const p = this.pedido();
    if (!p) return;
    this.fotosSvc.eliminar(p.id, fotoId).subscribe({
      next: () => {
        const f = this.fotosPedido().find(x => x.id === fotoId);
        if (f?.url) URL.revokeObjectURL(f.url);
        this.fotosPedido.update(lista => lista.filter(x => x.id !== fotoId));
        this.toast.info('Foto eliminada');
      },
      error: () => this.toast.error('No se pudo eliminar la foto.')
    });
  }

  etiquetaMomento(m: MomentoFoto): string {
    return m === 'RECEPCION' ? 'Recepción' : m === 'ENTREGA' ? 'Entrega' : 'Otro';
  }

  // ---------- Presentación ----------
  tituloHistorial(h: PedidoHistorial): string {
    switch (h.estadoProceso) {
      case 'LISTO': return '✓ Listo para recojo';
      case 'ENTREGA_PARCIAL': return '📦 Entrega parcial';
      case 'ENTREGADO': return '📦 Entregado al cliente';
      case 'ANULADO': return '🚫 Pedido anulado';
      case 'PENDIENTE': return h.areaNombre ? `Ingreso · ${h.areaNombre}` : 'Ingreso de pedido';
      case 'EN_PROCESO':
      default: return h.areaNombre ?? 'Actualización';
    }
  }

  etiquetaEstado(estado: string): string {
    return ({
      'PENDIENTE': 'Pendiente',
      'EN_PROCESO': 'En proceso',
      'LISTO': 'Listo para entregar',
      'ENTREGA_PARCIAL': 'Entrega parcial',
      'ENTREGADO': 'Entregado',
      'ANULADO': 'Anulado'
    } as Record<string, string>)[estado] ?? estado;
  }

  cerrarModales() {
    if (this.procesando()) return;
    this.modalPago.set(false);
    this.modalEntrega.set(false);
    this.modalCliente.set(false);
    this.modalItem.set(false);
    this.modalFecha.set(false);
    this.modalAnular.set(false);
    this.cerrarDestinoDelivery();
  }
}
