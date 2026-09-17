import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, HostListener, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { DISTRITOS_LIMA_CALLAO } from '../../core/constants/distritos-lima-callao';
import { AreaLavado, Cliente, ModalidadPedido, Pedido, Servicio } from '../../core/models/models';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ClientesService } from '../../core/services/clientes.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { FotosPedidoService } from '../../core/services/fotos-pedido.service';
import { PedidosService } from '../../core/services/pedidos.service';
import { PromocionValida } from '../../core/services/promociones.service';
import { ToastService } from '../../core/services/toast.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { esCelularValido } from '../../core/util/telefono';
import { IconComponent } from '../../shared/icon/icon.component';
import { SoloDigitosDirective } from '../../shared/directives/solo-digitos.directive';
import { TelefonoPaisComponent } from '../../shared/telefono-pais/telefono-pais.component';
import { MapaUbicacionComponent, UbicacionMapa } from '../../shared/mapa-ubicacion/mapa-ubicacion.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface ItemAgregado {
  servicioId: number;
  nombre: string;
  precio: number;
  unidad: string;
  cantidad: number;
  descripcion: string;
  // Solo para servicios cobrados por m² (alfombras): dimensiones para calcular el área.
  // La `cantidad` de estos ítems ES el área en m² (ancho × largo × piezas).
  ancho?: number;
  largo?: number;
  piezas?: number;
}

/** ¿La unidad del servicio se cobra por metro cuadrado? (m2, m², mt2…). */
export function esUnidadM2(unidad: string | null | undefined): boolean {
  return (unidad ?? '').trim().toLowerCase().replace('²', '2').replace(/\s+/g, '') === 'm2';
}

@Component({
  selector: 'app-registrar',
  imports: [CommonModule, FormsModule, IconComponent, MapaUbicacionComponent, PageHeaderComponent, SoloDigitosDirective, TelefonoPaisComponent],
  templateUrl: './registrar.component.html',
  styleUrl: './registrar.component.scss'
})
export class RegistrarComponent implements OnInit, OnDestroy {
  private readonly catalogosSvc = inject(CatalogosService);
  private readonly clientesSvc = inject(ClientesService);
  private readonly pedidosSvc = inject(PedidosService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly whatsapp = inject(WhatsappService);
  private readonly config = inject(ConfiguracionService);
  private readonly fotosSvc = inject(FotosPedidoService);

  // ---------- Botón Registrar arriba/abajo dinámico ----------
  // La barra de arriba está en el flujo normal; cuando el trabajador hace scroll y deja de
  // verse, aparece la barra fija abajo. Así el botón Registrar siempre está a la vista.
  readonly mostrarBarraAbajo = signal(false);
  private observadorBarra?: IntersectionObserver;

  @ViewChild('barraTop') set barraTopRef(el: ElementRef<HTMLElement> | undefined) {
    this.observadorBarra?.disconnect();
    this.observadorBarra = undefined;
    if (el && typeof IntersectionObserver !== 'undefined') {
      this.observadorBarra = new IntersectionObserver(
        entradas => this.mostrarBarraAbajo.set(!entradas[0].isIntersecting),
        { threshold: 0 }
      );
      this.observadorBarra.observe(el.nativeElement);
    } else {
      this.mostrarBarraAbajo.set(false);
    }
  }

  readonly catalogo = signal<Servicio[]>([]);
  readonly areas = signal<AreaLavado[]>([]);
  readonly cargando = signal(false);
  readonly siguienteNumero = signal<number | null>(null);
  readonly itemAnimadoId = signal<number | null>(null);
  private itemAnimadoTimer?: ReturnType<typeof setTimeout>;

  clienteExistente: Cliente | null = null;
  nombre = '';
  direccion = '';
  celular = '';
  documentoIdentidad = '';
  documentoFiscal = '';
  modalidad: ModalidadPedido = 'Tienda';
  readonly distritos = DISTRITOS_LIMA_CALLAO;
  direccionEntrega = '';
  distritoEntrega = '';
  referenciaEntrega = '';
  latitudEntrega: number | null = null;
  longitudEntrega: number | null = null;
  ubicacionEntregaConfirmada = false;
  costoDeliveryPedido = 0;
  observacionesPedido = '';
  buscandoCliente = signal(false);

  campoBusquedaCliente: 'Nombre' | 'Celular' | 'DNI' = 'Nombre';
  textoBusquedaCliente = '';
  readonly resultadosBusquedaCliente = signal<Cliente[]>([]);
  private busquedaClienteTimerId?: ReturnType<typeof setTimeout>;

  servicioSeleccionadoId: number | '' = '';
  // Buscador del catálogo de servicios (Paso 2): filtra la lista al escribir.
  readonly busquedaServicio = signal('');
  readonly comboServicioAbierto = signal(false);
  readonly catalogoFiltrado = computed(() => {
    const q = this.normalizarTexto(this.busquedaServicio());
    const lista = this.catalogo();
    return q ? lista.filter(s => this.normalizarTexto(s.nombre).includes(q)) : lista;
  });
  items = signal<ItemAgregado[]>([]);

  // ---------- Crear al vuelo un producto/servicio que falta en la lista ----------
  // Evita cortar el registro para ir a Ajustes: se crea aquí mismo, se guarda en el
  // catálogo y se agrega de una vez al pedido.
  readonly modalNuevoServicio = signal(false);
  readonly guardandoNuevoServicio = signal(false);
  readonly errorNuevoServicio = signal<string | null>(null);
  readonly unidadesServicio = ['kg', 'prenda', 'pieza', 'und', 'servicio', 'm2'];
  formNuevoServicio: { nombre: string; precio: number | null; unidad: string } = { nombre: '', precio: null, unidad: 'prenda' };

  areaInicialId: number | null = null;
  fechaEntregaValor = signal<string>(this.formatoLocal(this.fechaRecojoPorDefecto()));
  fechaEntregaEstimada = computed(() => new Date(this.fechaEntregaValor()));

  aplicaDescuento = signal(false);
  descuentoPorcentaje = signal(10);
  esUrgente = signal(false);
  recargoUrgentePorcentaje = signal(20);

  // Canje de puntos (fidelización)
  puntosACanjear = signal(0);
  get valorPuntoCanje(): number { return this.config.configuracion().valorPuntoCanje ?? 0; }
  get maxDescuentoPct(): number { return this.config.configuracion().maxDescuentoPct ?? 0; }
  get puntosDisponibles(): number { return this.clienteExistente?.puntos ?? 0; }
  get canjeHabilitado(): boolean { return this.valorPuntoCanje > 0 && this.puntosDisponibles > 0; }
  get negocioConfig() { return this.config.configuracion(); }

  codigoPromo = '';
  readonly validandoPromo = signal(false);
  readonly promoAplicada = signal<PromocionValida | null>(null);
  readonly errorPromo = signal<string | null>(null);

  montoPagado = 0;
  metodoPagoInicial: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';
  notificarWhatsapp = true;

  subtotal = computed(() =>
    this.items().reduce((acc, it) => acc + it.precio * it.cantidad, 0)
  );

  descuentoMonto = computed(() =>
    this.aplicaDescuento() ? this.subtotal() * (this.descuentoPorcentaje() / 100) : 0
  );

  recargoUrgenteMonto = computed(() =>
    this.esUrgente() ? this.subtotal() * (this.recargoUrgentePorcentaje() / 100) : 0
  );

  descuentoPuntosMonto = computed(() => {
    const bruto = this.puntosACanjear() * this.valorPuntoCanje;
    const disponible = Math.max(0, this.subtotal() - this.descuentoMonto());
    return Math.min(bruto, disponible);
  });

  totalNeto = computed(() => this.subtotal() - this.descuentoMonto() - this.descuentoPuntosMonto() + this.recargoUrgenteMonto());

  setPuntosACanjear(v: number) {
    const n = Math.floor(Number(v) || 0);
    const maxPorMonto = this.valorPuntoCanje > 0
      ? Math.floor(Math.max(0, this.subtotal() - this.descuentoMonto()) / this.valorPuntoCanje)
      : 0;
    const max = Math.min(this.puntosDisponibles, maxPorMonto);
    this.puntosACanjear.set(Math.max(0, Math.min(n, max)));
  }

  setDescuentoPct(v: number) {
    let n = Number(v) || 0;
    if (this.maxDescuentoPct > 0) n = Math.min(n, this.maxDescuentoPct);
    this.descuentoPorcentaje.set(Math.max(0, Math.min(100, n)));
  }
  totalFinal = computed(() => Math.round(this.totalNeto() * 10) / 10);
  redondeo = computed(() => Math.round((this.totalFinal() - this.totalNeto()) * 100) / 100);

  registrado = signal(false);
  pedidoCreado = signal<Pedido | null>(null);
  registrando = signal(false);

  /** True si el usuario ya escribió algo del pedido y todavía no lo registró:
   *  usado para avisar antes de perder el trabajo por un F5 / cierre accidental. */
  private hayPedidoEnProgreso(): boolean {
    if (this.registrado() || this.registrando()) return false;
    return this.nombre.trim().length > 0
      || this.celular.trim().length > 0
      || this.items().length > 0;
  }

  // El navegador muestra su diálogo nativo "¿Salir del sitio? Los cambios no se guardarán"
  // solo si hay un pedido a medio llenar. Cubre F5, cerrar pestaña y navegar fuera por URL.
  @HostListener('window:beforeunload', ['$event'])
  avisarSalidaConTrabajo(evento: BeforeUnloadEvent): void {
    if (this.hayPedidoEnProgreso()) {
      evento.preventDefault();
      evento.returnValue = '';
    }
  }

  @HostListener('document:keydown.escape')
  cerrarModalesConEscape(): void {
    if (this.modalNuevoServicio()) this.cerrarNuevoServicio();
  }

  ngOnInit() {
    this.whatsapp.cargar();
    this.config.cargar().subscribe({ error: () => {} }); // asegura valorPuntoCanje / maxDescuentoPct frescos
    this.pedidosSvc.siguienteNumero().subscribe({
      next: n => this.siguienteNumero.set(n),
      error: () => {}
    });
    this.cargando.set(true);
    this.catalogosSvc.servicios().subscribe({
      next: s => this.catalogo.set(s),
      error: () => this.toast.error('No se pudo cargar el catálogo de servicios.')
    });
    this.catalogosSvc.areasLavado().subscribe({
      next: a => {
        this.areas.set(a);
        this.areaInicialId = a[0]?.id ?? null;
        this.cargando.set(false);
      },
      error: () => this.cargando.set(false)
    });
  }

  private formatoLocal(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  /**
   * Fecha/hora sugerida de recojo o entrega: hoy a las 18:30 (cierre típico de la lavandería).
   * Si ya pasaron las 18:30, sugiere el mismo horario del día siguiente para no proponer un pasado.
   */
  private fechaRecojoPorDefecto(): Date {
    const d = new Date();
    d.setHours(18, 30, 0, 0);
    if (d.getTime() <= Date.now()) d.setDate(d.getDate() + 1);
    return d;
  }

  /** Fija la fecha de recojo sumando N horas a la hora de recepción (ahora). */
  presetEntrega(horas: number) {
    this.fechaEntregaValor.set(this.formatoLocal(new Date(Date.now() + horas * 60 * 60 * 1000)));
  }

  onBuscarClienteInput(texto: string) {
    this.textoBusquedaCliente = texto;
    if (this.busquedaClienteTimerId) clearTimeout(this.busquedaClienteTimerId);
    if (texto.trim().length < 2) {
      this.resultadosBusquedaCliente.set([]);
      return;
    }
    this.busquedaClienteTimerId = setTimeout(() => this.ejecutarBusquedaCliente(), 300);
  }

  private ejecutarBusquedaCliente() {
    const texto = this.textoBusquedaCliente.trim();
    if (texto.length < 2) return;
    this.buscandoCliente.set(true);
    this.clientesSvc.buscar(texto, this.campoBusquedaCliente.toLowerCase(), 8).subscribe({
      next: list => { this.resultadosBusquedaCliente.set(list); this.buscandoCliente.set(false); },
      error: () => this.buscandoCliente.set(false)
    });
  }

  cambiarModalidad(m: ModalidadPedido) {
    this.modalidad = m;
    if (m === 'Delivery' && !this.direccionEntrega.trim() && this.direccion.trim()) {
      this.direccionEntrega = this.direccion.trim();
    }
    const idDelivery = this.config.configuracion().servicioDeliveryId;
    if (!idDelivery) return;

    if (this.esDomicilio()) {
      if (this.costoDeliveryPedido <= 0) {
        this.costoDeliveryPedido = Number(this.config.configuracion().costoDelivery) || 0;
      }
      const yaEsta = this.items().some(i => i.servicioId === idDelivery);
      if (!yaEsta) {
        this.items.update(list => [...list, {
          servicioId: idDelivery,
          nombre: 'Servicio a domicilio',
          precio: this.costoDeliveryPedido,
          unidad: 'Unidad',
          cantidad: 1,
          descripcion: ''
        }]);
      } else {
        this.actualizarCostoDelivery(this.costoDeliveryPedido);
      }
    } else {
      this.items.update(list => list.filter(i => i.servicioId !== idDelivery));
    }
  }

  actualizarCostoDelivery(valor: number) {
    const costo = Math.max(0, Math.min(10000, Number(valor) || 0));
    this.costoDeliveryPedido = costo;
    const idDelivery = this.config.configuracion().servicioDeliveryId;
    if (idDelivery) {
      this.items.update(list => list.map(i => i.servicioId === idDelivery ? { ...i, precio: costo } : i));
    }
  }

  esDomicilio() {
    return this.modalidad === 'Recojo' || this.modalidad === 'Delivery';
  }

  tieneServicioLavanderia() {
    const idDelivery = this.config.configuracion().servicioDeliveryId;
    return this.items().some(item => !idDelivery || item.servicioId !== idDelivery);
  }

  actualizarUbicacion(ubicacion: UbicacionMapa | null) {
    this.latitudEntrega = ubicacion?.latitud ?? null;
    this.longitudEntrega = ubicacion?.longitud ?? null;
    this.ubicacionEntregaConfirmada = !!ubicacion?.etiqueta;
    // El mapa manda: al elegir/mover el pin se actualizan dirección y distrito para que
    // no queden inconsistentes con el punto. Solo sobrescribe cuando el mapa resolvió un valor.
    if (ubicacion?.direccion) this.direccionEntrega = ubicacion.direccion;
    if (ubicacion?.distrito) this.distritoEntrega = ubicacion.distrito;
  }

  actualizarDireccionEntrega(valor: string) {
    if (valor !== this.direccionEntrega) this.ubicacionEntregaConfirmada = false;
    this.direccionEntrega = valor;
  }

  actualizarDistritoEntrega(valor: string) {
    if (valor !== this.distritoEntrega) this.ubicacionEntregaConfirmada = false;
    this.distritoEntrega = valor;
  }

  descripcionPaso3() {
    return this.modalidad === 'Delivery' ? '¿Cuándo entregar?'
      : this.modalidad === 'Recojo' ? '¿Cuándo estará listo?'
      : '¿Cuándo estará listo para recojo?';
  }

  etiquetaFechaProgramada() {
    return this.modalidad === 'Delivery' ? 'Fecha y hora de entrega'
      : this.esDomicilio() ? 'Fecha y hora estimada'
      : 'Fecha y hora de recojo';
  }

  seleccionarClienteFrecuente(c: Cliente) {
    this.clienteExistente = c;
    this.nombre = c.nombre;
    this.celular = c.celular ?? '';
    this.direccion = c.direccion ?? '';
    if (this.modalidad === 'Delivery') {
      this.direccionEntrega = c.direccion ?? '';
      this.distritoEntrega = '';
      this.referenciaEntrega = '';
      this.latitudEntrega = null;
      this.longitudEntrega = null;
      this.ubicacionEntregaConfirmada = false;
    }
    this.documentoIdentidad = c.dni ?? '';
    this.documentoFiscal = c.documentoFiscal ?? '';
    this.resultadosBusquedaCliente.set([]);
    this.textoBusquedaCliente = '';
    this.toast.info(`Cliente seleccionado: ${c.nombre}`);
  }

  quitarClienteSeleccionado() {
    this.clienteExistente = null;
    this.nombre = '';
    this.celular = '';
    this.direccion = '';
    this.documentoIdentidad = '';
    this.documentoFiscal = '';
    this.direccionEntrega = '';
    this.distritoEntrega = '';
    this.referenciaEntrega = '';
    this.latitudEntrega = null;
    this.longitudEntrega = null;
    this.ubicacionEntregaConfirmada = false;
    this.puntosACanjear.set(0); // los puntos pertenecen al cliente seleccionado
  }

  agregarItem() {
    if (!this.servicioSeleccionadoId) return;
    const servicio = this.catalogo().find(s => s.id === this.servicioSeleccionadoId);
    if (!servicio) return;

    const existente = this.items().find(i => i.servicioId === servicio.id);
    const esM2 = esUnidadM2(servicio.unidad);
    if (existente) {
      // m²: "agregar" otra vez suma una pieza más del mismo tamaño y recalcula el área.
      this.items.update(list =>
        list.map(i => {
          if (i.servicioId !== servicio.id) return i;
          if (esUnidadM2(i.unidad)) {
            const piezas = (i.piezas ?? 1) + 1;
            return { ...i, piezas, cantidad: this.areaM2(i.ancho, i.largo, piezas) };
          }
          return { ...i, cantidad: i.cantidad + 1 };
        })
      );
    } else {
      this.items.update(list => [...list, {
        servicioId: servicio.id,
        nombre: servicio.nombre,
        precio: servicio.precio,
        unidad: servicio.unidad,
        // m²: arranca en 1.00 × 1.00 = 1 m²; el operario ajusta las medidas reales.
        cantidad: 1,
        descripcion: '',
        ...(esM2 ? { ancho: 1, largo: 1, piezas: 1 } : {})
      }]);
    }
    clearTimeout(this.itemAnimadoTimer);
    this.itemAnimadoId.set(servicio.id);
    this.itemAnimadoTimer = setTimeout(() => {
      if (this.itemAnimadoId() === servicio.id) this.itemAnimadoId.set(null);
    }, 850);
    this.servicioSeleccionadoId = '';
    this.busquedaServicio.set('');
  }

  // ---------- Buscador de servicios (Paso 2) ----------
  onBuscarServicio(valor: string) {
    this.busquedaServicio.set(valor);
    this.comboServicioAbierto.set(true);
    this.servicioSeleccionadoId = ''; // al escribir se "deselecciona" hasta elegir de la lista
  }
  elegirServicio(s: Servicio) {
    this.servicioSeleccionadoId = s.id;
    this.busquedaServicio.set(s.nombre);
    this.comboServicioAbierto.set(false);
  }
  cerrarComboServicio() { this.comboServicioAbierto.set(false); }

  abrirNuevoServicio() {
    this.formNuevoServicio = { nombre: '', precio: null, unidad: 'prenda' };
    this.errorNuevoServicio.set(null);
    this.modalNuevoServicio.set(true);
  }

  cerrarNuevoServicio() {
    if (this.guardandoNuevoServicio()) return;
    this.modalNuevoServicio.set(false);
  }

  guardarNuevoServicio() {
    if (this.guardandoNuevoServicio()) return;
    const nombre = this.formNuevoServicio.nombre.trim();
    const unidad = this.formNuevoServicio.unidad.trim();
    const precio = Number(this.formNuevoServicio.precio ?? 0);

    if (nombre.length < 2 || nombre.length > 120) {
      this.errorNuevoServicio.set('El nombre debe tener entre 2 y 120 caracteres.');
      return;
    }
    if (!unidad) {
      this.errorNuevoServicio.set('Selecciona la unidad de cobro.');
      return;
    }
    if (!Number.isFinite(precio) || precio <= 0 || precio > 10_000) {
      this.errorNuevoServicio.set('Ingresa un precio mayor a S/ 0.00 y hasta S/ 10,000.00.');
      return;
    }

    // Si ya existía en el catálogo (mismo nombre, sin importar tildes/mayúsculas) no lo
    // duplicamos: lo seleccionamos y lo agregamos al pedido.
    const yaExiste = this.catalogo().find(s => this.normalizarTexto(s.nombre) === this.normalizarTexto(nombre));
    if (yaExiste) {
      this.servicioSeleccionadoId = yaExiste.id;
      this.agregarItem();
      this.modalNuevoServicio.set(false);
      this.toast.info(`"${yaExiste.nombre}" ya estaba en el catálogo; lo agregamos al pedido.`);
      return;
    }

    this.guardandoNuevoServicio.set(true);
    this.errorNuevoServicio.set(null);
    this.catalogosSvc.crearServicioRapido(nombre, Math.round(precio * 100) / 100, unidad).subscribe({
      next: creado => {
        this.guardandoNuevoServicio.set(false);
        this.modalNuevoServicio.set(false);
        this.catalogo.update(list => [...list, {
          id: creado.id, nombre: creado.nombre, precio: creado.precio,
          unidad: creado.unidad, categoriaId: creado.categoriaId ?? null
        }]);
        this.servicioSeleccionadoId = creado.id;
        this.agregarItem();
        this.toast.exito(`Producto "${creado.nombre}" creado y agregado al pedido`);
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoNuevoServicio.set(false);
        this.errorNuevoServicio.set(err.error?.mensaje ?? 'No se pudo crear el producto.');
      }
    });
  }

  private normalizarTexto(valor: string): string {
    return valor.normalize('NFD').replace(/\p{Diacritic}/gu, '').trim().toLowerCase();
  }

  validarPromo() {
    const codigo = this.codigoPromo.trim();
    if (!codigo) return;
    this.validandoPromo.set(true);
    this.errorPromo.set(null);
    this.pedidosSvc.validarCodigoPromocion(codigo, this.clienteExistente?.id).subscribe({
      next: promo => {
        this.validandoPromo.set(false);

        const cantidadAplicable = promo.servicioId
          ? this.items().filter(i => i.servicioId === promo.servicioId).reduce((acc, i) => acc + i.cantidad, 0)
          : this.items().reduce((acc, i) => acc + i.cantidad, 0);

        if (cantidadAplicable < promo.cantidadMinima) {
          this.errorPromo.set(`Esta promoción requiere una cantidad mínima de ${promo.cantidadMinima}.`);
          return;
        }

        this.promoAplicada.set(promo);
        this.aplicaDescuento.set(true);
        if (promo.descuentoPct) {
          this.descuentoPorcentaje.set(promo.descuentoPct);
        } else if (promo.descuentoMonto && this.subtotal() > 0) {
          this.descuentoPorcentaje.set(Math.min(100, Math.round((promo.descuentoMonto / this.subtotal()) * 10000) / 100));
        }
        this.toast.exito(`Promoción aplicada: ${promo.descripcion}`);
      },
      error: (err: HttpErrorResponse) => {
        this.validandoPromo.set(false);
        this.errorPromo.set(err.error?.mensaje ?? 'Código no válido.');
      }
    });
  }

  quitarPromo() {
    this.promoAplicada.set(null);
    this.codigoPromo = '';
    this.errorPromo.set(null);
    this.aplicaDescuento.set(false);
    this.descuentoPorcentaje.set(10);
  }

  quitarItem(id: number) {
    this.items.update(list => list.filter(i => i.servicioId !== id));
  }

  cambiarCantidad(id: number, cantidad: number) {
    if (cantidad < 0.1) return;
    this.items.update(list =>
      list.map(i => i.servicioId === id ? { ...i, cantidad } : i)
    );
  }

  cambiarDescripcion(id: number, descripcion: string) {
    this.items.update(list =>
      list.map(i => i.servicioId === id ? { ...i, descripcion } : i)
    );
  }

  /** El precio del catálogo es referencial: el operario puede ajustarlo por ítem. */
  cambiarPrecio(id: number, precio: number) {
    const p = Math.max(0, Math.min(10000, Number(precio) || 0));
    this.items.update(list =>
      list.map(i => i.servicioId === id ? { ...i, precio: p } : i)
    );
  }

  /** El cargo de domicilio no se edita en la tabla: se controla con el "Costo de delivery/recojo". */
  esItemDelivery(id: number): boolean {
    return id === this.config.configuracion().servicioDeliveryId;
  }

  // ---------- Servicios por m² (alfombras) ----------
  /** Área en m² = ancho × largo × piezas, redondeada a 2 decimales (mínimo 0). */
  private areaM2(ancho?: number, largo?: number, piezas?: number): number {
    const a = Math.max(0, Number(ancho) || 0);
    const l = Math.max(0, Number(largo) || 0);
    const n = Math.max(1, Math.floor(Number(piezas) || 1));
    return Math.round(a * l * n * 100) / 100;
  }

  esItemM2(it: ItemAgregado): boolean {
    return esUnidadM2(it.unidad);
  }

  /** Actualiza una medida (ancho/largo/piezas) de un ítem m² y recalcula su área (cantidad). */
  cambiarMedida(id: number, campo: 'ancho' | 'largo' | 'piezas', valor: number) {
    const v = Math.max(0, Number(valor) || 0);
    this.items.update(list =>
      list.map(i => {
        if (i.servicioId !== id) return i;
        const it = { ...i, [campo]: campo === 'piezas' ? Math.max(1, Math.floor(v)) : v };
        return { ...it, cantidad: this.areaM2(it.ancho, it.largo, it.piezas) };
      })
    );
  }

  /** Texto legible de las medidas para el ticket/detalle, ej. "1.20 × 2.00 m · 2 alfombras". */
  medidaTexto(it: ItemAgregado): string {
    const a = (it.ancho ?? 0).toFixed(2);
    const l = (it.largo ?? 0).toFixed(2);
    const n = it.piezas ?? 1;
    return `${a} × ${l} m` + (n > 1 ? ` · ${n} alfombras` : '');
  }

  get puedeRegistrar(): boolean {
    return this.nombre.trim().length > 0
      && esCelularValido(this.celular)
      && (this.modalidad !== 'Recojo' || this.direccion.trim().length > 0)
      && (this.modalidad !== 'Delivery' || (
        this.direccionEntrega.trim().length > 0 && this.distritoEntrega.trim().length > 0
        && this.latitudEntrega !== null && this.longitudEntrega !== null
        && this.ubicacionEntregaConfirmada
      ))
      && this.tieneServicioLavanderia()
      && !this.items().some(i => this.esItemM2(i) && i.cantidad <= 0)
      && this.totalFinal() > 0
      && !this.registrando();
  }

  get validacionPedido(): string | null {
    if (!this.nombre.trim()) return 'Indica el nombre del cliente.';
    if (!esCelularValido(this.celular)) return 'Revisa el celular: solo números (o elige el país para un número extranjero). Puedes dejarlo vacío.';
    if (this.modalidad === 'Recojo' && !this.direccion.trim()) return 'Indica la dirección donde se recogerá el pedido.';
    if (this.modalidad === 'Delivery' && !this.direccionEntrega.trim()) return 'Indica la dirección exacta de entrega.';
    if (this.modalidad === 'Delivery' && !this.distritoEntrega.trim()) return 'Selecciona el distrito de entrega.';
    if (this.modalidad === 'Delivery' && (this.latitudEntrega === null || this.longitudEntrega === null)) return 'Busca la dirección o marca el punto exacto en el mapa.';
    if (this.modalidad === 'Delivery' && !this.ubicacionEntregaConfirmada) return 'Confirma que la dirección coincida con el punto del mapa.';
    if (!this.tieneServicioLavanderia()) return 'Agrega al menos un servicio de lavandería; la tarifa de delivery no cuenta como servicio.';
    const m2SinMedida = this.items().find(i => this.esItemM2(i) && i.cantidad <= 0);
    if (m2SinMedida) return `Indica el ancho y el largo de "${m2SinMedida.nombre}" (el área debe ser mayor a cero).`;
    if (this.totalFinal() <= 0) return 'El total del pedido debe ser mayor a cero.';
    return null;
  }

  registrarOrden() {
    if (!this.puedeRegistrar) return;

    this.registrando.set(true);

    const payload = {
      clienteId: this.clienteExistente?.id,
      clienteNuevo: {
        nombre: this.nombre.trim(),
        celular: this.celular || null,
        dni: this.documentoIdentidad || null,
        documentoFiscal: this.documentoFiscal || null,
        direccion: this.direccion || null,
      },
      modalidad: this.modalidad,
      direccionEntrega: this.modalidad === 'Delivery' ? this.direccionEntrega.trim() : null,
      distritoEntrega: this.modalidad === 'Delivery' ? this.distritoEntrega : null,
      referenciaEntrega: this.modalidad === 'Delivery' ? (this.referenciaEntrega.trim() || null) : null,
      latitudEntrega: this.modalidad === 'Delivery' ? this.latitudEntrega : null,
      longitudEntrega: this.modalidad === 'Delivery' ? this.longitudEntrega : null,
      items: this.items().map(i => {
        // Para alfombras (m²) se antepone la medida a la observación, así queda registrada
        // en el ticket y el detalle (ej. "1.20 × 2.00 m · 2 alfombras · mancha en la esquina").
        const nota = i.descripcion.trim();
        const descripcion = this.esItemM2(i)
          ? [this.medidaTexto(i), nota].filter(Boolean).join(' · ')
          : (nota || null);
        return {
          servicioId: i.servicioId,
          cantidad: i.cantidad,
          precioUnit: i.precio,
          total: i.precio * i.cantidad,
          descripcion: descripcion || null
        };
      }),
      descuentoPct: this.aplicaDescuento() ? this.descuentoPorcentaje() : 0,
      codigoPromocion: this.promoAplicada() ? this.codigoPromo.trim().toUpperCase() : null,
      puntosACanjear: this.puntosACanjear() > 0 ? this.puntosACanjear() : null,
      esUrgente: this.esUrgente(),
      recargoUrgentePct: this.recargoUrgentePorcentaje(),
      costoDelivery: this.esDomicilio() ? this.costoDeliveryPedido : null,
      montoPagado: this.montoPagado,
      metodoPagoInicial: this.metodoPagoInicial,
      // Se envía en hora LOCAL (naive), igual que FechaIngreso, para que la hora de entrega
      // no se desfase ~5h. Antes se mandaba toISOString() (UTC) y salía adelantada en el ticket.
      fechaEntregaEst: this.fechaEntregaValor(),
      observaciones: this.observacionesPedido.trim() || null,
      areaInicialId: this.areaInicialId,
    };

    // Si se enviará la confirmación por WhatsApp, se abre la pestaña AHORA (durante el clic) para
    // que el navegador no la bloquee al abrirla luego, tras la respuesta del servidor.
    const notificar = this.notificarWhatsapp && !!this.celular?.trim();
    const ventanaWa = notificar ? window.open('', '_blank') : null;

    this.pedidosSvc.crear(payload).subscribe({
      next: p => {
        this.registrando.set(false);
        this.registrado.set(true);
        this.pedidoCreado.set(p);
        this.toast.exito(`Pedido #${p.numero} registrado`);
        this.subirFotosStaged(p.id);
        if (notificar && p.clienteCelular) this.enviarWhatsapp(ventanaWa);
        else ventanaWa?.close();
      },
      error: (err: HttpErrorResponse) => {
        this.registrando.set(false);
        ventanaWa?.close();
        this.toast.desdeHttp(err, 'No se pudo registrar el pedido.');
      }
    });
  }

  // ---------- Fotos de evidencia (se toman al registrar, momento "Recepción") ----------
  readonly maxFotos = 15;
  readonly fotosStaged = signal<{ blob: Blob; url: string }[]>([]);
  readonly procesandoFotos = signal(false);
  readonly subiendoFotos = signal(false);

  async onFotosSeleccionadas(event: Event) {
    const input = event.target as HTMLInputElement;
    const archivos = Array.from(input.files ?? []);
    input.value = '';
    if (archivos.length === 0) return;

    this.procesandoFotos.set(true);
    for (const archivo of archivos) {
      if (this.fotosStaged().length >= this.maxFotos) {
        this.toast.info(`Puedes adjuntar hasta ${this.maxFotos} fotos por pedido.`);
        break;
      }
      if (!archivo.type.startsWith('image/')) continue;
      try {
        const blob = await this.fotosSvc.comprimir(archivo);
        const url = URL.createObjectURL(blob);
        this.fotosStaged.update(list => [...list, { blob, url }]);
      } catch {
        this.toast.error('No se pudo procesar una de las fotos.');
      }
    }
    this.procesandoFotos.set(false);
  }

  quitarFoto(indice: number) {
    this.fotosStaged.update(list => {
      const f = list[indice];
      if (f) URL.revokeObjectURL(f.url);
      return list.filter((_, i) => i !== indice);
    });
  }

  /** Sube las fotos adjuntas al pedido recién creado. Si falla, no rompe nada: el pedido ya existe
   *  y las fotos se pueden agregar luego desde el detalle. */
  private subirFotosStaged(pedidoId: number) {
    const fotos = this.fotosStaged();
    if (fotos.length === 0) return;
    this.subiendoFotos.set(true);
    forkJoin(fotos.map(f => this.fotosSvc.subir(pedidoId, f.blob, 'RECEPCION'))).subscribe({
      next: () => {
        this.subiendoFotos.set(false);
        this.toast.exito(`${fotos.length} foto(s) de evidencia guardada(s)`);
        this.limpiarFotos();
      },
      error: () => {
        this.subiendoFotos.set(false);
        this.toast.advertencia('El pedido se registró, pero algunas fotos no se subieron. Puedes agregarlas desde el detalle del pedido.');
        this.limpiarFotos();
      }
    });
  }

  private limpiarFotos() {
    this.fotosStaged().forEach(f => URL.revokeObjectURL(f.url));
    this.fotosStaged.set([]);
  }

  ngOnDestroy() {
    this.fotosStaged().forEach(f => URL.revokeObjectURL(f.url));
    this.observadorBarra?.disconnect();
  }

  irAPedidos() {
    this.router.navigate(['/pedidos']);
  }

  irAServicios() {
    this.router.navigate(['/ajustes/servicios']);
  }

  imprimirTicket(tipo: 'cliente' | 'produccion' = 'cliente') {
    const p = this.pedidoCreado();
    if (!p) return;
    const query = tipo === 'produccion' ? '?tipo=produccion' : '';
    window.open(`/ticket/${p.id}${query}`, '_blank');
  }

  enviarWhatsapp(ventana?: Window | null) {
    const p = this.pedidoCreado();
    if (!p || !p.clienteCelular) { ventana?.close(); return; }

    const enviar = (link?: string) => {
      const mensaje = this.whatsapp.mensajeIngreso(p, this.config.configuracion(), link);
      this.whatsapp.enviar(p.clienteCelular!, mensaje, ventana);
    };

    if (this.esPedidoDomicilio(p)) {
      this.pedidosSvc.linkSeguimiento(p.id).subscribe({
        next: ({ token }) => enviar(`${window.location.origin}/seguimiento/${token}`),
        error: () => enviar()
      });
      return;
    }

    enviar();
  }

  /** Botón verde "Enviar pedido y seguimiento" (solo Delivery): manda el mensaje "en camino"
   * con el link de seguimiento en vivo + saldo + datos de Yape del negocio. */
  enviarEnCamino() {
    const p = this.pedidoCreado();
    if (!p || !p.clienteCelular) return;
    const ventana = window.open('', '_blank');
    const enviar = (link?: string) => {
      const mensaje = this.whatsapp.mensajeEnCamino(p, this.config.configuracion(), link);
      this.whatsapp.enviar(p.clienteCelular!, mensaje, ventana);
    };
    this.pedidosSvc.linkSeguimiento(p.id).subscribe({
      next: ({ token }) => enviar(`${window.location.origin}/seguimiento/${token}`),
      error: () => enviar()
    });
  }

  nuevaOrden() {
    this.registrado.set(false);
    this.pedidoCreado.set(null);
    this.clienteExistente = null;
    this.nombre = '';
    this.direccion = '';
    this.celular = '';
    this.documentoIdentidad = '';
    this.documentoFiscal = '';
    this.observacionesPedido = '';
    this.textoBusquedaCliente = '';
    this.resultadosBusquedaCliente.set([]);
    this.items.set([]);
    this.aplicaDescuento.set(false);
    this.esUrgente.set(false);
    this.puntosACanjear.set(0);
    this.montoPagado = 0;
    this.metodoPagoInicial = 'EFECTIVO';
    this.modalidad = 'Tienda';
    this.direccionEntrega = '';
    this.distritoEntrega = '';
    this.referenciaEntrega = '';
    this.latitudEntrega = null;
    this.longitudEntrega = null;
    this.ubicacionEntregaConfirmada = false;
    this.costoDeliveryPedido = 0;
  }

  private esPedidoDomicilio(p: Pedido) {
    return p.modalidad === 'Recojo' || p.modalidad === 'Delivery';
  }
}
