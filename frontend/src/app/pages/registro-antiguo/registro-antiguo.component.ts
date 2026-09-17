import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AreaLavado, Cliente, Servicio } from '../../core/models/models';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ClientesService } from '../../core/services/clientes.service';
import { PedidosService } from '../../core/services/pedidos.service';
import { ToastService } from '../../core/services/toast.service';
import { esCelularValido } from '../../core/util/telefono';
import { IconComponent } from '../../shared/icon/icon.component';
import { SoloDigitosDirective } from '../../shared/directives/solo-digitos.directive';
import { TelefonoPaisComponent } from '../../shared/telefono-pais/telefono-pais.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface ItemAgregado {
  servicioId: number;
  nombre: string;
  precio: number;
  unidad: string;
  cantidad: number;
  descripcion: string;
}

@Component({
  selector: 'app-registro-antiguo',
  imports: [CommonModule, FormsModule, IconComponent, PageHeaderComponent, SoloDigitosDirective, TelefonoPaisComponent],
  templateUrl: './registro-antiguo.component.html',
  styleUrl: './registro-antiguo.component.scss'
})
export class RegistroAntiguoComponent implements OnInit {
  private readonly catalogosSvc = inject(CatalogosService);
  private readonly clientesSvc = inject(ClientesService);
  private readonly pedidosSvc = inject(PedidosService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly catalogo = signal<Servicio[]>([]);
  readonly areas = signal<AreaLavado[]>([]);
  readonly cargando = signal(false);

  // Cliente
  clienteExistente: Cliente | null = null;
  documentoIdentidad = '';
  codigoAntiguo = '';
  nombre = '';
  celular = '';
  direccion = '';
  buscandoCliente = signal(false);

  // Buscar cliente frecuente (por nombre, celular o DNI)
  campoBusquedaCliente: 'Nombre' | 'Celular' | 'DNI' = 'Nombre';
  textoBusquedaCliente = '';
  readonly resultadosBusquedaCliente = signal<Cliente[]>([]);
  private busquedaClienteTimerId?: ReturnType<typeof setTimeout>;

  // Ingreso
  fechaIngresoValor = signal<string>(this.formatoLocal(new Date()));
  modalidad: 'Tienda' | 'Delivery' = 'Tienda';

  // Items
  servicioSeleccionadoId: number | '' = '';
  items = signal<ItemAgregado[]>([]);

  // Pago
  montoPagado = 0;
  metodoPagoInicial: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';

  registrando = signal(false);

  subtotal = computed(() => this.items().reduce((acc, it) => acc + it.precio * it.cantidad, 0));

  private formatoLocal(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  ngOnInit() {
    this.cargando.set(true);
    this.catalogosSvc.servicios().subscribe({
      next: s => this.catalogo.set(s),
      error: () => this.toast.error('No se pudo cargar el catálogo de servicios.')
    });
    this.catalogosSvc.areasLavado().subscribe({
      next: a => { this.areas.set(a); this.cargando.set(false); },
      error: () => this.cargando.set(false)
    });
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

  seleccionarClienteFrecuente(c: Cliente) {
    this.clienteExistente = c;
    this.nombre = c.nombre;
    this.celular = c.celular ?? '';
    this.direccion = c.direccion ?? '';
    this.documentoIdentidad = c.dni ?? '';
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
  }

  agregarItem() {
    if (!this.servicioSeleccionadoId) return;
    const servicio = this.catalogo().find(s => s.id === this.servicioSeleccionadoId);
    if (!servicio) return;

    const existente = this.items().find(i => i.servicioId === servicio.id);
    if (existente) {
      this.items.update(list => list.map(i => i.servicioId === servicio.id ? { ...i, cantidad: i.cantidad + 1 } : i));
    } else {
      this.items.update(list => [...list, {
        servicioId: servicio.id, nombre: servicio.nombre, precio: servicio.precio,
        unidad: servicio.unidad, cantidad: 1, descripcion: ''
      }]);
    }
    this.servicioSeleccionadoId = '';
  }

  quitarItem(id: number) {
    this.items.update(list => list.filter(i => i.servicioId !== id));
  }

  cambiarCantidad(id: number, cantidad: number) {
    if (cantidad < 0.1) return;
    this.items.update(list => list.map(i => i.servicioId === id ? { ...i, cantidad } : i));
  }

  get puedeRegistrar(): boolean {
    return this.nombre.trim().length > 0
      && esCelularValido(this.celular)
      && this.items().length > 0
      && this.subtotal() > 0
      && !this.registrando();
  }

  registrarOrden() {
    if (!this.puedeRegistrar) return;
    this.registrando.set(true);

    // Hora LOCAL (naive), no toISOString() (UTC), para que no se desfase ~5h.
    const fechaIso = this.fechaIngresoValor();

    const payload = {
      clienteId: this.clienteExistente?.id,
      // Se envía siempre (igual que en Registrar): si el cliente existente no tenía
      // celular, lo que el operador escriba aquí actualiza su ficha.
      clienteNuevo: {
        nombre: this.nombre.trim(),
        celular: this.celular || null,
        dni: this.documentoIdentidad || null,
        direccion: this.direccion || null,
      },
      modalidad: this.modalidad,
      items: this.items().map(i => ({
        servicioId: i.servicioId,
        cantidad: i.cantidad,
        precioUnit: i.precio,
        total: i.precio * i.cantidad,
        descripcion: i.descripcion.trim() || null
      })),
      descuentoPct: 0,
      esUrgente: false,
      recargoUrgentePct: 0,
      montoPagado: this.montoPagado,
      metodoPagoInicial: this.metodoPagoInicial,
      fechaEntregaEst: fechaIso,
      fechaIngreso: fechaIso,
      codigoAntiguo: this.codigoAntiguo.trim() || null,
      observaciones: null,
      areaInicialId: this.areas()[0]?.id ?? null,
    };

    this.pedidosSvc.crear(payload).subscribe({
      next: p => {
        this.registrando.set(false);
        this.toast.exito(`Pedido antiguo #${p.numero} registrado`);
        this.router.navigate(['/pedidos']);
      },
      error: (err: HttpErrorResponse) => {
        this.registrando.set(false);
        this.toast.desdeHttp(err, 'No se pudo registrar el pedido.');
      }
    });
  }

  volver() {
    this.router.navigate(['/pedidos']);
  }
}
