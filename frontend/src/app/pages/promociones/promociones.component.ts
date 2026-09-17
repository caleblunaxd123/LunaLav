import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ErroresCampo } from '../../core/util/errores-campo';
import { numeroWhatsapp } from '../../core/util/telefono';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Cliente, Servicio } from '../../core/models/models';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ClientesService } from '../../core/services/clientes.service';
import { CodigoGenerado, OrigenCodigo, Promocion, PromocionesService } from '../../core/services/promociones.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

type FormPromocion = Partial<Promocion> & { servicioId: number | null };

@Component({
  selector: 'app-promociones',
  imports: [CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent, PageHeaderComponent],
  templateUrl: './promociones.component.html',
  styleUrl: './promociones.component.scss'
})
export class PromocionesComponent implements OnInit {
  private readonly svc = inject(PromocionesService);
  private readonly catalogos = inject(CatalogosService);
  private readonly clientes = inject(ClientesService);
  private readonly toast = inject(ToastService);

  readonly promociones = signal<Promocion[]>([]);
  readonly servicios = signal<Servicio[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly promocionesPaginadas = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.promociones().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  readonly modalAbierto = signal(false);
  readonly editando = signal<Promocion | null>(null);
  readonly confirmarEliminar = signal<Promocion | null>(null);
  readonly confirmarDesactivar = signal<Promocion | null>(null);
  form: FormPromocion = this.formVacio();
  errorForm = signal<string | null>(null);
  readonly err = new ErroresCampo();
  guardando = signal(false);

  readonly tipos = [
    { valor: 'VOLUMEN', etiqueta: 'Por volumen (cantidad mínima)' },
    { valor: 'FRECUENCIA', etiqueta: 'Cliente frecuente' },
    { valor: 'FIJA', etiqueta: 'Fecha fija / temporada' },
  ];

  ngOnInit() {
    this.cargar();
    this.catalogos.servicios().subscribe(s => this.servicios.set(s));
  }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.promociones.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar promociones.'));
      }
    });
  }

  abrirCrear() {
    this.editando.set(null);
    this.form = this.formVacio();
    this.errorForm.set(null);
    this.err.limpiarTodo();
    this.modalAbierto.set(true);
  }

  abrirEditar(p: Promocion) {
    this.editando.set(p);
    this.form = { ...p };
    this.errorForm.set(null);
    this.err.limpiarTodo();
    this.modalAbierto.set(true);
  }

  cerrar() { this.modalAbierto.set(false); }

  guardar() {
    const errs: Record<string, string> = {};
    if (!this.form.descripcion?.trim()) errs['descripcion'] = 'Ingresa la descripción de la promoción.';
    if (!this.form.descuentoPct && !this.form.descuentoMonto) {
      errs['descuentoPct'] = 'Indica un descuento en % o en soles.';
    } else if (this.form.descuentoPct && this.form.descuentoMonto) {
      errs['descuentoPct'] = 'Usa solo un tipo: porcentaje o monto fijo.';
      errs['descuentoMonto'] = 'Usa solo un tipo: porcentaje o monto fijo.';
    }
    if (this.form.fechaInicio && this.form.fechaFin && this.form.fechaFin < this.form.fechaInicio)
      errs['fechaFin'] = 'La fecha final no puede ser anterior a la inicial.';

    this.err.set(errs);
    if (this.err.hay) { this.errorForm.set('Revisa los campos marcados en rojo.'); return; }
    this.guardando.set(true);
    this.errorForm.set(null);

    const edit = this.editando();
    const payload: Partial<Promocion> = {
      tipo: this.form.tipo || 'VOLUMEN',
      descripcion: this.form.descripcion!.trim(),
      descuentoPct: this.form.descuentoPct || null,
      descuentoMonto: this.form.descuentoMonto || null,
      servicioId: this.form.servicioId || null,
      cantidadMinima: this.form.cantidadMinima || 1,
      fechaInicio: this.form.fechaInicio || null,
      fechaFin: this.form.fechaFin || null,
      activa: this.form.activa ?? true,
      codigo: this.form.codigo?.trim().toUpperCase() || null,
      maxUsos: this.form.maxUsos ?? null,
    };

    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, payload)
      : this.svc.crear(payload);

    obs$.subscribe({
      next: () => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.toast.exito(edit ? 'Promoción actualizada' : 'Promoción creada');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? (err.error?.errors ? 'Revisa los datos ingresados.' : 'No se pudo guardar la promoción.');
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(p: Promocion) { this.confirmarEliminar.set(p); }

  eliminar() {
    const p = this.confirmarEliminar();
    if (!p) return;
    this.guardando.set(true);
    this.svc.eliminar(p.id).subscribe({
      next: () => {
        this.guardando.set(false);
        this.confirmarEliminar.set(null);
        this.toast.exito('Promoción eliminada');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo eliminar.');
      }
    });
  }

  toggleActiva(p: Promocion) {
    if (p.activa) { this.confirmarDesactivar.set(p); return; }
    this.aplicarCambioEstado(p, true);
  }

  confirmarDesactivarOk() {
    const p = this.confirmarDesactivar();
    if (!p) return;
    this.aplicarCambioEstado(p, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(p: Promocion, activa: boolean) {
    this.svc.cambiarEstado(p.id, activa).subscribe({
      next: () => {
        this.toast.info(activa ? 'Promoción activada' : 'Promoción desactivada');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  servicioNombre(id: number | null | undefined): string {
    if (!id) return 'Todos los servicios';
    return this.servicios().find(s => s.id === id)?.nombre ?? '—';
  }

  descuentoTexto(p: Promocion): string {
    if (p.descuentoPct) return `${p.descuentoPct}%`;
    if (p.descuentoMonto) return `S/ ${p.descuentoMonto.toFixed(2)}`;
    return '—';
  }

  vigenciaTexto(p: Promocion): string {
    if (!p.fechaInicio && !p.fechaFin) return 'Sin fecha límite';
    const ini = p.fechaInicio ? new Date(p.fechaInicio).toLocaleDateString('es-PE', { day: '2-digit', month: '2-digit' }) : '…';
    const fin = p.fechaFin ? new Date(p.fechaFin).toLocaleDateString('es-PE', { day: '2-digit', month: '2-digit' }) : '…';
    return `${ini} - ${fin}`;
  }

  // ====================== Generador automático de códigos ======================
  readonly origenes = [
    { valor: 'NUEVO' as OrigenCodigo, etiqueta: 'Cliente nuevo', icono: '🎉', ayuda: 'Descuento de bienvenida para captar a un cliente nuevo.', requiereCliente: false, pidePuntos: false, pct: 10, dias: 30 },
    { valor: 'CUMPLE' as OrigenCodigo, etiqueta: 'Cumpleaños', icono: '🎂', ayuda: 'Regalo de cumpleaños para un cliente.', requiereCliente: true, pidePuntos: false, pct: 15, dias: 15 },
    { valor: 'REFERIDO' as OrigenCodigo, etiqueta: 'Recomendación', icono: '🤝', ayuda: 'Código que un cliente comparte para traer clientes nuevos.', requiereCliente: false, pidePuntos: false, pct: 10, dias: 30 },
    { valor: 'PUNTOS' as OrigenCodigo, etiqueta: 'Puntos ganados', icono: '⭐', ayuda: 'Convierte los puntos acumulados de un cliente en un descuento.', requiereCliente: true, pidePuntos: true, pct: 0, dias: 30 },
  ];

  readonly generadorAbierto = signal(false);
  readonly genOrigen = signal<OrigenCodigo>('NUEVO');
  readonly genCliente = signal<Cliente | null>(null);
  readonly genResultados = signal<Cliente[]>([]);
  readonly generando = signal(false);
  readonly genError = signal<string | null>(null);
  readonly codigoGenerado = signal<CodigoGenerado | null>(null);
  genBusqueda = '';
  genPct = 10;
  genPuntos = 0;
  genDias = 30;

  readonly origenActual = computed(() => this.origenes.find(o => o.valor === this.genOrigen())!);

  abrirGenerador() {
    this.genOrigen.set('NUEVO');
    this.aplicarDefaultsOrigen();
    this.genCliente.set(null);
    this.genResultados.set([]);
    this.genBusqueda = '';
    this.genError.set(null);
    this.codigoGenerado.set(null);
    this.generadorAbierto.set(true);
  }

  cerrarGenerador() { this.generadorAbierto.set(false); }

  setGenOrigen(o: OrigenCodigo) {
    this.genOrigen.set(o);
    this.aplicarDefaultsOrigen();
    this.genError.set(null);
  }

  private aplicarDefaultsOrigen() {
    const meta = this.origenActual();
    this.genPct = meta.pct;
    this.genDias = meta.dias;
    this.genPuntos = 0;
  }

  buscarClienteGen(texto: string) {
    const t = texto.trim();
    if (t.length < 2) { this.genResultados.set([]); return; }
    this.clientes.buscar(t, undefined, 8).subscribe({
      next: list => this.genResultados.set(list),
      error: () => this.genResultados.set([])
    });
  }

  elegirClienteGen(c: Cliente) {
    this.genCliente.set(c);
    this.genResultados.set([]);
    this.genBusqueda = c.nombre;
    if (this.genOrigen() === 'PUNTOS') this.genPuntos = c.puntos ?? 0;
  }

  quitarClienteGen() {
    this.genCliente.set(null);
    this.genBusqueda = '';
    this.genResultados.set([]);
  }

  generarCodigo() {
    const meta = this.origenActual();
    const cliente = this.genCliente();
    if (meta.requiereCliente && !cliente) {
      this.genError.set('Elige un cliente para este tipo de código.');
      return;
    }
    if (meta.pidePuntos) {
      if (!this.genPuntos || this.genPuntos < 1) { this.genError.set('Indica cuántos puntos convertir.'); return; }
      if (cliente && this.genPuntos > (cliente.puntos ?? 0)) { this.genError.set(`El cliente solo tiene ${cliente.puntos ?? 0} puntos.`); return; }
    } else if (!this.genPct || this.genPct < 1) {
      this.genError.set('El descuento debe ser mayor a cero.');
      return;
    }

    this.generando.set(true);
    this.genError.set(null);
    this.svc.generar({
      origen: meta.valor,
      clienteId: cliente?.id ?? null,
      descuentoPct: meta.pidePuntos ? null : this.genPct,
      puntosACanjear: meta.pidePuntos ? this.genPuntos : null,
      diasVigencia: this.genDias
    }).subscribe({
      next: res => {
        this.generando.set(false);
        this.codigoGenerado.set(res);
        this.toast.exito('Código generado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.generando.set(false);
        this.genError.set(err.error?.mensaje ?? 'No se pudo generar el código.');
      }
    });
  }

  enviarWhatsappCodigo() {
    const res = this.codigoGenerado();
    if (!res) return;
    const numero = numeroWhatsapp(res.celular);
    const base = numero ? `https://wa.me/${numero}` : 'https://wa.me/';
    window.open(`${base}?text=${encodeURIComponent(res.mensajeWhatsapp)}`, '_blank');
  }

  copiarCodigo() {
    const cod = this.codigoGenerado()?.promocion.codigo;
    if (!cod) return;
    navigator.clipboard?.writeText(cod).then(
      () => this.toast.info('Código copiado'),
      () => {}
    );
  }

  volverAGenerar() {
    this.codigoGenerado.set(null);
    this.genError.set(null);
  }

  private formVacio(): FormPromocion {
    return {
      tipo: 'VOLUMEN', descripcion: '', descuentoPct: null, descuentoMonto: null,
      servicioId: null, cantidadMinima: 1, fechaInicio: null, fechaFin: null, activa: true, codigo: null,
      maxUsos: 1  // por defecto, de un solo uso (se desactiva al aplicarse su código)
    };
  }
}
