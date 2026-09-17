import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, Subject } from 'rxjs';
import { Cliente, Pedido } from '../../core/models/models';
import { ClienteFrecuente, ClientesService, MovimientoPuntos } from '../../core/services/clientes.service';
import { MiniBarrasComponent, PuntoBarra } from '../../shared/mini-barras/mini-barras.component';
import { PedidosService } from '../../core/services/pedidos.service';
import { CodigoGenerado, PromocionesService } from '../../core/services/promociones.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { ToastService } from '../../core/services/toast.service';
import { esCelularValido } from '../../core/util/telefono';
import { ErroresCampo } from '../../core/util/errores-campo';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { SoloDigitosDirective } from '../../shared/directives/solo-digitos.directive';
import { TelefonoPaisComponent } from '../../shared/telefono-pais/telefono-pais.component';
import { ActualizacionDatosService } from '../../core/services/actualizacion-datos.service';
import { ColumnaImport, ImportadorMasivoComponent } from '../../shared/importador-masivo/importador-masivo.component';

@Component({
  selector: 'app-clientes',
  imports: [CommonModule, FormsModule, RouterLink, EmptyStateComponent, PaginacionComponent, IconComponent, PageHeaderComponent, SoloDigitosDirective, ImportadorMasivoComponent, MiniBarrasComponent, TelefonoPaisComponent],
  templateUrl: './clientes.component.html',
  styleUrl: './clientes.component.scss'
})
export class ClientesComponent implements OnInit, OnDestroy {
  private readonly service = inject(ClientesService);
  private readonly pedidosSvc = inject(PedidosService);
  private readonly promociones = inject(PromocionesService);
  private readonly whatsapp = inject(WhatsappService);
  private readonly toast = inject(ToastService);
  private readonly actualizaciones = inject(ActualizacionDatosService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly buscar$ = new Subject<void>();
  private timerActualizacion?: ReturnType<typeof setInterval>;
  private versionRecarga = 0;

  readonly clientes = signal<Cliente[]>([]);
  readonly tendencia = signal<PuntoBarra[] | null>(null);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly modalAbierto = signal(false);

  // ---------- Paginación (client-side sobre el resultado de búsqueda) ----------
  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly clientesPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.clientes().slice(inicio, inicio + this.tamanoPagina());
  });

  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  campoBusqueda: 'Nombre' | 'Celular' | 'DNI' = 'Nombre';
  textoBusqueda = '';

  nuevoCliente: Partial<Cliente> = { nombre: '', celular: '', dni: '', direccion: '' };
  editando = signal<Cliente | null>(null);
  confirmarEliminar = signal<Cliente | null>(null);
  guardando = signal(false);
  errorNuevo = signal<string | null>(null);
  readonly err = new ErroresCampo();

  // ---------- Pestañas ----------
  readonly tab = signal<'buscar' | 'frecuentes' | 'unir'>('buscar');

  // ---------- Unir duplicados ----------
  readonly todosClientes = signal<Cliente[]>([]);
  readonly cargandoUnir = signal(false);
  readonly fusionando = signal(false);
  origenId: number | '' = '';
  destinoId: number | '' = '';

  cargarTodosClientes() {
    this.cargandoUnir.set(true);
    this.service.buscar(undefined, undefined, 500).subscribe({
      next: list => { this.todosClientes.set(list); this.cargandoUnir.set(false); },
      error: () => this.cargandoUnir.set(false)
    });
  }

  get puedeFusionar(): boolean {
    return !!this.origenId && !!this.destinoId && this.origenId !== this.destinoId && !this.fusionando();
  }

  fusionarClientes() {
    if (!this.puedeFusionar) return;
    this.fusionando.set(true);
    this.service.fusionar(this.origenId as number, this.destinoId as number).subscribe({
      next: res => {
        this.fusionando.set(false);
        this.toast.exito(res.mensaje);
        this.origenId = '';
        this.destinoId = '';
        this.cargarTodosClientes();
        this.recargar();
      },
      error: (err: HttpErrorResponse) => {
        this.fusionando.set(false);
        this.toast.desdeHttp(err, 'No se pudo fusionar.');
      }
    });
  }

  // ---------- Frecuentes ----------
  readonly frecuentes = signal<ClienteFrecuente[]>([]);
  readonly cargandoFrecuentes = signal(false);
  readonly errorFrecuentes = signal<string | null>(null);
  desdeFrecuentes = this.formatoFecha(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000));
  hastaFrecuentes = this.formatoFecha(new Date());

  private formatoFecha(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }

  cambiarTab(t: 'buscar' | 'frecuentes' | 'unir') {
    this.tab.set(t);
    if (t === 'frecuentes' && this.frecuentes().length === 0) this.cargarFrecuentes();
    if (t === 'unir' && this.todosClientes().length === 0) this.cargarTodosClientes();
  }

  cargarFrecuentes() {
    this.cargandoFrecuentes.set(true);
    this.errorFrecuentes.set(null);
    this.service.frecuentes(this.desdeFrecuentes, this.hastaFrecuentes).subscribe({
      next: list => { this.frecuentes.set(list); this.cargandoFrecuentes.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargandoFrecuentes.set(false);
        this.errorFrecuentes.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar el informe.'));
      }
    });
  }

  maxVisitas(): number {
    return Math.max(1, ...this.frecuentes().map(f => f.visitas));
  }

  exportarFrecuentesCsv() {
    const list = this.frecuentes();
    if (list.length === 0) {
      this.toast.advertencia('No hay datos para exportar.');
      return;
    }
    const filas = [
      'Nombre,Celular,Visitas',
      ...list.map(f => `"${f.nombre.replace(/"/g, '""')}","${f.celular ?? ''}",${f.visitas}`)
    ];
    const blob = new Blob(['﻿' + filas.join('\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `clientes-frecuentes-${this.desdeFrecuentes}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  ngOnInit() {
    this.service.tendencia(6).subscribe({ next: t => this.tendencia.set(t), error: () => {} });
    this.buscar$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.recargar());
    this.actualizaciones.cambios('clientes', 'pedidos', 'foco').pipe(
      debounceTime(180),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => this.refrescarDinamicamente());
    this.recargar();
    // Sin auto-refresco periódico (incómodo al trabajar); se actualiza por foco/cambios.
  }

  ngOnDestroy() {
    if (this.timerActualizacion) clearInterval(this.timerActualizacion);
  }

  onBuscarChange() {
    this.pagina.set(1);
    this.buscar$.next();
  }

  recargar(silencioso = false) {
    const version = ++this.versionRecarga;
    if (!silencioso) {
      this.cargando.set(true);
      this.error.set(null);
    }
    const campo = this.campoBusqueda.toLowerCase();
    this.service.buscar(this.textoBusqueda || undefined, campo, 500).subscribe({
      next: list => {
        if (version !== this.versionRecarga) return;
        this.clientes.set(list);
        this.cargando.set(false);
      },
      error: (err: HttpErrorResponse) => {
        if (version !== this.versionRecarga) return;
        this.cargando.set(false);
        if (!silencioso) {
          this.error.set(err.status === 0
            ? 'No se pudo conectar con el servidor. Verifica que el backend esté corriendo.'
            : (err.error?.mensaje ?? 'Error al cargar clientes.'));
        }
      }
    });
  }

  private refrescarDinamicamente() {
    if (typeof document !== 'undefined' && document.visibilityState !== 'visible') return;
    if (this.cargando() || this.guardando() || this.fusionando() || this.guardandoPunto()) return;
    this.recargar(true);
    const detalle = this.clienteDetalle();
    if (detalle) {
      this.service.obtener(detalle.id).subscribe(actualizado => this.clienteDetalle.set(actualizado));
      if (this.subTabDetalle() === 'puntos') this.cargarPuntos();
      if (this.subTabDetalle() === 'ordenes') this.cargarOrdenes();
    }
    if (this.tab() === 'frecuentes') this.cargarFrecuentes();
    if (this.tab() === 'unir') this.cargarTodosClientes();
  }

  abrirModal() {
    this.nuevoCliente = { nombre: '', celular: '', dni: '', direccion: '', puntos: 0 };
    this.errorNuevo.set(null);
    this.err.limpiarTodo();
    this.modalAbierto.set(true);
  }

  cerrarModal() { this.modalAbierto.set(false); }

  // ---------- Importación masiva ----------
  readonly importarAbierto = signal(false);
  readonly importando = signal(false);
  readonly columnasImport: ColumnaImport[] = [
    { clave: 'nombre', etiqueta: 'Nombre', requerido: true, tipo: 'texto' },
    { clave: 'celular', etiqueta: 'Celular', tipo: 'telefono' },
    { clave: 'dni', etiqueta: 'DNI', tipo: 'dni' },
    { clave: 'direccion', etiqueta: 'Dirección', tipo: 'texto' },
  ];

  abrirImportar() { this.importarAbierto.set(true); }
  cerrarImportar() { if (!this.importando()) this.importarAbierto.set(false); }

  importarClientes(filas: Array<Record<string, string | number | null>>) {
    if (this.importando()) return;
    this.importando.set(true);
    this.service.importar(filas).subscribe({
      next: res => {
        this.importando.set(false);
        this.importarAbierto.set(false);
        const partes = [`${res.creados} cliente(s) creado(s)`];
        if (res.omitidos) partes.push(`${res.omitidos} omitido(s)`);
        this.toast.exito(partes.join(' · '));
        this.recargar();
      },
      error: (err: HttpErrorResponse) => {
        this.importando.set(false);
        this.toast.desdeHttp(err, 'No se pudo importar el archivo.');
      }
    });
  }

  guardar() {
    const errs: Record<string, string> = {};
    const dni = (this.nuevoCliente.dni ?? '').toString().trim();
    const ruc = (this.nuevoCliente.documentoFiscal ?? '').toString().trim();

    if (!this.nuevoCliente.nombre?.trim()) errs['nombre'] = 'Ingresa el nombre del cliente.';
    if (!esCelularValido(this.nuevoCliente.celular))
      errs['celular'] = 'Revisa el celular: 9 dígitos para Perú, o elige el país para un número extranjero (o déjalo vacío).';
    if (dni && !/^\d{8}$/.test(dni)) errs['dni'] = 'El DNI debe tener 8 dígitos (o déjalo vacío).';
    if (ruc && !/^\d{11}$/.test(ruc)) errs['ruc'] = 'El RUC debe tener 11 dígitos (o déjalo vacío).';

    this.err.set(errs);
    if (this.err.hay) { this.errorNuevo.set('Revisa los campos marcados en rojo.'); return; }
    this.guardando.set(true);
    this.errorNuevo.set(null);

    // Los campos opcionales vacíos deben ir como null, no como "" — si no, las validaciones
    // de formato del backend (DNI 8 dígitos, celular 9 dígitos) rechazan la cadena vacía.
    const aNull = (v: unknown) => { const t = (v ?? '').toString().trim(); return t ? t : null; };
    const payload: Partial<Cliente> = {
      ...this.nuevoCliente,
      nombre: this.nuevoCliente.nombre?.trim(),
      celular: aNull(this.nuevoCliente.celular),
      dni: aNull(this.nuevoCliente.dni),
      documentoFiscal: aNull(this.nuevoCliente.documentoFiscal),
      direccion: aNull(this.nuevoCliente.direccion),
      fechaNacimiento: aNull(this.nuevoCliente.fechaNacimiento),
    };

    const edit = this.editando();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.service.actualizar(edit.id, payload)
      : this.service.crear(payload);

    obs$.subscribe({
      next: (res: any) => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.editando.set(null);
        this.err.limpiarTodo();
        this.toast.exito(edit
          ? `Cliente "${this.nuevoCliente.nombre}" actualizado`
          : `Cliente "${res?.nombre ?? this.nuevoCliente.nombre}" registrado`);
        this.recargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el cliente.';
        const low = msg.toLowerCase();
        const campo = low.includes('celular') ? 'celular'
          : low.includes('dni') ? 'dni'
          : low.includes('ruc') || low.includes('fiscal') ? 'ruc'
          : low.includes('nombre') ? 'nombre' : null;
        if (campo) { this.err.marcar(campo, msg); this.errorNuevo.set('Revisa los campos marcados en rojo.'); }
        else this.errorNuevo.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  editar(c: Cliente) {
    this.editando.set(c);
    this.nuevoCliente = { ...c };
    this.errorNuevo.set(null);
    this.err.limpiarTodo();
    this.modalAbierto.set(true);
  }

  pedirEliminar(c: Cliente) {
    this.confirmarEliminar.set(c);
  }

  eliminar() {
    const c = this.confirmarEliminar();
    if (!c) return;
    this.guardando.set(true);
    this.service.desactivar(c.id).subscribe({
      next: res => {
        this.guardando.set(false);
        this.confirmarEliminar.set(null);
        this.toast.exito(res.mensaje);
        this.recargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo eliminar.');
      }
    });
  }

  // ---------- Detalle 360° del cliente (Puntos + Órdenes) ----------
  readonly clienteDetalle = signal<Cliente | null>(null);
  readonly subTabDetalle = signal<'puntos' | 'ordenes'>('puntos');

  readonly movimientosPuntos = signal<MovimientoPuntos[]>([]);
  readonly cargandoPuntos = signal(false);
  readonly modalPuntoAbierto = signal(false);
  nuevoPuntoMotivo = '';
  nuevoPuntoCantidad = 0;
  nuevoPuntoTipo: 'SUMA' | 'RESTA' = 'SUMA';
  readonly guardandoPunto = signal(false);

  readonly ordenesCliente = signal<Pedido[]>([]);
  readonly cargandoOrdenes = signal(false);
  readonly ordenesFiltro = signal<'en-proceso' | 'con-deuda' | 'entregados' | 'todos'>('en-proceso');
  readonly ordenesPagina = signal(1);
  readonly ordenesTotal = signal(0);
  readonly ordenesTamanoPagina = 10;

  abrirDetalle(c: Cliente) {
    this.clienteDetalle.set(c);
    this.subTabDetalle.set('puntos');
    this.cargarPuntos();
  }

  cerrarDetalle() {
    this.clienteDetalle.set(null);
  }

  cambiarSubTabDetalle(t: 'puntos' | 'ordenes') {
    this.subTabDetalle.set(t);
    if (t === 'puntos' && this.movimientosPuntos().length === 0) this.cargarPuntos();
    if (t === 'ordenes' && this.ordenesCliente().length === 0) this.cargarOrdenes();
  }

  cargarPuntos() {
    const c = this.clienteDetalle();
    if (!c) return;
    this.cargandoPuntos.set(true);
    this.service.listarPuntos(c.id).subscribe({
      next: list => { this.movimientosPuntos.set(list); this.cargandoPuntos.set(false); },
      error: () => this.cargandoPuntos.set(false)
    });
  }

  abrirModalPunto() {
    this.nuevoPuntoMotivo = '';
    this.nuevoPuntoCantidad = 0;
    this.nuevoPuntoTipo = 'SUMA';
    this.modalPuntoAbierto.set(true);
  }

  confirmarAgregarPunto() {
    const c = this.clienteDetalle();
    if (!c || !this.nuevoPuntoMotivo.trim() || this.nuevoPuntoCantidad <= 0) return;
    this.guardandoPunto.set(true);
    this.service.agregarPuntos(c.id, this.nuevoPuntoMotivo.trim(), this.nuevoPuntoCantidad, this.nuevoPuntoTipo).subscribe({
      next: () => {
        this.guardandoPunto.set(false);
        this.modalPuntoAbierto.set(false);
        this.toast.exito('Registro de puntos agregado');
        this.cargarPuntos();
        this.recargar();
        this.service.obtener(c.id).subscribe(actualizado => this.clienteDetalle.set(actualizado));
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoPunto.set(false);
        this.toast.desdeHttp(err, 'No se pudo agregar el registro.');
      }
    });
  }

  // ---------- Canje de puntos → código de descuento ----------
  readonly modalCanjeAbierto = signal(false);
  canjePuntos = 0;
  canjeDias = 30;
  readonly generandoCanje = signal(false);
  readonly codigoCanje = signal<CodigoGenerado | null>(null);

  abrirModalCanje() {
    const c = this.clienteDetalle();
    if (!c) return;
    this.canjePuntos = c.puntos ?? 0;
    this.canjeDias = 30;
    this.codigoCanje.set(null);
    this.modalCanjeAbierto.set(true);
  }

  cerrarModalCanje() { this.modalCanjeAbierto.set(false); }

  generarCodigoCanje() {
    const c = this.clienteDetalle();
    if (!c) return;
    if (this.canjePuntos < 1) { this.toast.advertencia('Indica cuántos puntos convertir.'); return; }
    if (this.canjePuntos > (c.puntos ?? 0)) { this.toast.advertencia(`El cliente solo tiene ${c.puntos ?? 0} puntos.`); return; }
    this.generandoCanje.set(true);
    this.promociones.generar({ origen: 'PUNTOS', clienteId: c.id, puntosACanjear: this.canjePuntos, diasVigencia: this.canjeDias }).subscribe({
      next: res => {
        this.generandoCanje.set(false);
        this.codigoCanje.set(res);
        this.toast.exito(`Código ${res.promocion.codigo} generado`);
        this.cargarPuntos();
        this.service.obtener(c.id).subscribe(a => this.clienteDetalle.set(a));
        this.recargar();
      },
      error: (err: HttpErrorResponse) => {
        this.generandoCanje.set(false);
        this.toast.desdeHttp(err, 'No se pudo generar el código.');
      }
    });
  }

  enviarWhatsappCanje() {
    const res = this.codigoCanje();
    const c = this.clienteDetalle();
    if (!res) return;
    if (c?.celular) this.whatsapp.enviar(c.celular, res.mensajeWhatsapp);
    else this.toast.advertencia('El cliente no tiene celular; copia el código y envíalo manualmente.');
  }

  copiarCodigoCanje() {
    const cod = this.codigoCanje()?.promocion.codigo;
    if (cod) navigator.clipboard?.writeText(cod).then(() => this.toast.info('Código copiado'), () => {});
  }

  cambiarOrdenesFiltro(f: 'en-proceso' | 'con-deuda' | 'entregados' | 'todos') {
    this.ordenesFiltro.set(f);
    this.ordenesPagina.set(1);
    this.cargarOrdenes();
  }

  cambiarOrdenesPagina(p: number) {
    this.ordenesPagina.set(p);
    this.cargarOrdenes();
  }

  cargarOrdenes() {
    const c = this.clienteDetalle();
    if (!c) return;
    this.cargandoOrdenes.set(true);
    this.pedidosSvc.listarPorCliente(c.id, this.ordenesFiltro(), this.ordenesPagina(), this.ordenesTamanoPagina).subscribe({
      next: res => {
        this.ordenesCliente.set(res.items);
        this.ordenesTotal.set(res.total);
        this.cargandoOrdenes.set(false);
      },
      error: () => this.cargandoOrdenes.set(false)
    });
  }

  saldoPendiente(p: Pedido): number {
    return Math.max(0, p.total - p.montoPagado);
  }

  etiquetaEstadoPago(p: Pedido): string {
    return p.estadoPago === 'PAGADO' ? 'Pagado' : p.estadoPago === 'PARCIAL' ? 'Parcial' : 'Pendiente';
  }
}
