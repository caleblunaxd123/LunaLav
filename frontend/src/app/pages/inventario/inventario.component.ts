import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { CajaService } from '../../core/services/caja.service';
import { AuthService } from '../../core/services/auth.service';
import { ClaseInsumo, Insumo, InsumosService, MovimientoInsumo, RegistrarMovimientoInsumoRequest } from '../../core/services/insumos.service';
import { MiniBarrasComponent, PuntoBarra } from '../../shared/mini-barras/mini-barras.component';
import { ToastService } from '../../core/services/toast.service';
import { TipoGasto } from '../../core/models/models';
import { ErroresCampo } from '../../core/util/errores-campo';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { debounceTime } from 'rxjs';
import { ActivatedRoute } from '@angular/router';
import { ActualizacionDatosService } from '../../core/services/actualizacion-datos.service';
import { ColumnaImport, ImportadorMasivoComponent } from '../../shared/importador-masivo/importador-masivo.component';

@Component({
  selector: 'app-inventario',
  imports: [CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent, PageHeaderComponent, ImportadorMasivoComponent, MiniBarrasComponent],
  templateUrl: './inventario.component.html',
  styleUrl: './inventario.component.scss'
})
export class InventarioComponent implements OnInit, OnDestroy {
  private readonly svc = inject(InsumosService);
  private readonly cajaSvc = inject(CajaService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  /** Solo el ADMIN puede corregir/eliminar movimientos del historial. */
  readonly esAdmin = computed(() => this.auth.usuario()?.rol === 'ADMIN');
  private readonly actualizaciones = inject(ActualizacionDatosService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly tab = signal<'insumos' | 'historial'>('insumos');

  readonly insumos = signal<Insumo[]>([]);
  readonly tendencia = signal<PuntoBarra[] | null>(null);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly tiposGasto = signal<TipoGasto[]>([]);
  readonly insumoAnimadoId = signal<number | null>(null);
  private insumoAnimadoTimer?: ReturnType<typeof setTimeout>;
  private timerActualizacion?: ReturnType<typeof setInterval>;
  private versionCarga = 0;

  // ---------- Paginación ----------
  readonly paginaInsumos = signal(1);
  readonly tamanoPaginaInsumos = signal(15);
  readonly insumosBajoStock = computed(() => this.insumos().filter(i => i.activo && this.bajoStock(i)).length);
  readonly insumosActivos = computed(() => this.insumos().filter(i => i.activo).length);
  readonly busqueda = signal('');
  readonly filtroEstado = signal<'TODOS' | 'ACTIVOS' | 'BAJO_STOCK' | 'INACTIVOS'>('TODOS');
  readonly filtroClase = signal<'TODAS' | ClaseInsumo>('TODAS');

  // Clases de inventario: valor almacenado + etiqueta visible.
  readonly clases: { valor: ClaseInsumo; etiqueta: string }[] = [
    { valor: 'EQUIPO', etiqueta: 'Equipos de trabajo' },
    { valor: 'MATERIAL', etiqueta: 'Materiales y herramientas' },
    { valor: 'INSUMO', etiqueta: 'Insumos consumibles' },
  ];
  claseEtiqueta(c: ClaseInsumo | undefined): string {
    return this.clases.find(x => x.valor === c)?.etiqueta ?? 'Insumos consumibles';
  }

  /** "100 litros en total" cuando el insumo define contenido por unidad; si no, null. */
  contenidoTotalTexto(i: Insumo): string | null {
    if (!i.contenidoValor || i.contenidoValor <= 0 || !i.contenidoUnidad) return null;
    const total = i.stockActual * i.contenidoValor;
    return `${total.toLocaleString('es-PE', { maximumFractionDigits: 2 })} ${i.contenidoUnidad} en total`;
  }

  /** Días que faltan para el vencimiento (negativo = ya venció). NaN si no hay fecha. */
  diasParaVencer(i: Insumo): number {
    if (!i.fechaVencimiento) return NaN;
    const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
    const v = new Date(i.fechaVencimiento + 'T00:00:00');
    if (isNaN(v.getTime())) return NaN;
    return Math.round((v.getTime() - hoy.getTime()) / 86_400_000);
  }

  /** Estado de vencimiento del insumo: 'vencido', 'por-vencer' (≤30 días), 'ok', o null si no tiene fecha. */
  estadoVencimiento(i: Insumo): 'vencido' | 'por-vencer' | 'ok' | null {
    const d = this.diasParaVencer(i);
    if (Number.isNaN(d)) return null;
    if (d < 0) return 'vencido';
    if (d <= 30) return 'por-vencer';
    return 'ok';
  }

  readonly insumosFiltrados = computed(() => {
    const termino = this.normalizar(this.busqueda());
    const estado = this.filtroEstado();
    const clase = this.filtroClase();
    return this.insumos().filter(i => {
      const coincideTexto = !termino || this.normalizar(i.nombre).includes(termino);
      const coincideEstado = estado === 'TODOS'
        || (estado === 'ACTIVOS' && i.activo)
        || (estado === 'BAJO_STOCK' && i.activo && this.bajoStock(i))
        || (estado === 'INACTIVOS' && !i.activo);
      const coincideClase = clase === 'TODAS' || i.clase === clase;
      return coincideTexto && coincideEstado && coincideClase;
    });
  });
  readonly insumosOrdenados = computed(() => [...this.insumosFiltrados()].sort((a, b) => {
    const prioridadA = !a.activo ? 2 : (this.bajoStock(a) ? 0 : 1);
    const prioridadB = !b.activo ? 2 : (this.bajoStock(b) ? 0 : 1);
    return prioridadA - prioridadB || a.nombre.localeCompare(b.nombre, 'es');
  }));
  readonly insumosPaginados = computed(() => {
    const inicio = (this.paginaInsumos() - 1) * this.tamanoPaginaInsumos();
    return this.insumosOrdenados().slice(inicio, inicio + this.tamanoPaginaInsumos());
  });
  cambiarPaginaInsumos(p: number) { this.paginaInsumos.set(p); }
  cambiarTamanoPaginaInsumos(t: number) { this.tamanoPaginaInsumos.set(t); this.paginaInsumos.set(1); }
  cambiarFiltros() { this.paginaInsumos.set(1); }
  limpiarFiltros() { this.busqueda.set(''); this.filtroEstado.set('TODOS'); this.filtroClase.set('TODAS'); this.paginaInsumos.set(1); }

  readonly paginaHistorial = signal(1);
  readonly tamanoPaginaHistorial = signal(15);
  readonly movimientosPaginados = computed(() => {
    const inicio = (this.paginaHistorial() - 1) * this.tamanoPaginaHistorial();
    return this.movimientos().slice(inicio, inicio + this.tamanoPaginaHistorial());
  });
  cambiarPaginaHistorial(p: number) { this.paginaHistorial.set(p); }
  cambiarTamanoPaginaHistorial(t: number) { this.tamanoPaginaHistorial.set(t); this.paginaHistorial.set(1); }

  // ---------- Alta/edición de insumo ----------
  readonly modalInsumo = signal(false);
  readonly editandoInsumo = signal<Insumo | null>(null);
  formInsumo: Partial<Insumo> = this.formInsumoVacio();
  errorFormInsumo = signal<string | null>(null);
  readonly err = new ErroresCampo();
  guardandoInsumo = signal(false);

  readonly confirmarEliminar = signal<Insumo | null>(null);
  readonly confirmarDesactivar = signal<Insumo | null>(null);

  // ---------- Registrar movimiento ----------
  readonly modalMovimiento = signal(false);
  insumoMovimiento: Insumo | null = null;
  // 'MEDICION' es un modo de la UI: se ingresa el peso actual y se convierte a CONSUMO o AJUSTE al guardar.
  movTipo: 'COMPRA' | 'CONSUMO' | 'MEDICION' | 'AJUSTE' = 'COMPRA';
  movCantidad = 0;
  movCosto = 0;
  movMetodoPago: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';
  movTipoGastoId: number | '' = '';
  movDescripcion = '';
  movFecha = '';  // fecha de la compra (opcional, solo COMPRA)
  // Modo Medición por peso:
  movPesoActual = 0;        // peso medido hoy, en la unidad de contenido (Kg/Lt) o en la unidad base
  movEsCorreccion = false;  // true = ajusta el stock sin contarlo como consumo del día
  guardandoMovimiento = signal(false);

  constructor() {
    this.actualizaciones.cambios('inventario', 'foco').pipe(
      debounceTime(180),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => this.refrescarDinamicamente());
  }

  // ---------- Historial ----------
  readonly movimientos = signal<MovimientoInsumo[]>([]);
  readonly cargandoHistorial = signal(false);
  desdeHistorial = this.formatoFecha(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000));
  hastaHistorial = this.formatoFecha(new Date());

  private formatoFecha(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }

  ngOnInit() {
    // Si se llega desde la alerta "insumos bajo stock", abrir directamente filtrado a esos insumos.
    if (this.route.snapshot.queryParamMap.get('ver') === 'bajo-stock') {
      this.filtroEstado.set('BAJO_STOCK');
    }
    this.cargar();
    this.cargarTendencia();
    this.cajaSvc.tiposGasto().subscribe(t => this.tiposGasto.set(t));
    // Sin auto-refresco periódico (incómodo al trabajar); se actualiza por foco/cambios.
  }

  /** Recarga el gráfico "Consumo de insumos por día". */
  private cargarTendencia() {
    this.svc.tendenciaConsumo(14).subscribe({ next: t => this.tendencia.set(t), error: () => {} });
  }

  ngOnDestroy() {
    if (this.insumoAnimadoTimer) clearTimeout(this.insumoAnimadoTimer);
    if (this.timerActualizacion) clearInterval(this.timerActualizacion);
  }

  cambiarTab(t: 'insumos' | 'historial') {
    this.tab.set(t);
    if (t === 'historial' && this.movimientos().length === 0) this.cargarHistorial();
  }

  cargar(silencioso = false) {
    const version = ++this.versionCarga;
    if (!silencioso) {
      this.cargando.set(true);
      this.error.set(null);
      this.paginaInsumos.set(1);
    }
    this.svc.listar().subscribe({
      next: list => {
        if (version !== this.versionCarga) return;
        this.insumos.set(list);
        this.cargando.set(false);
      },
      error: (err: HttpErrorResponse) => {
        if (version !== this.versionCarga) return;
        this.cargando.set(false);
        if (!silencioso) {
          this.error.set(err.status === 0
            ? 'No se pudo conectar con el servidor.'
            : (err.error?.mensaje ?? 'Error al cargar el inventario.'));
        }
      }
    });
  }

  private refrescarDinamicamente() {
    if (typeof document !== 'undefined' && document.visibilityState !== 'visible') return;
    if (this.cargando() || this.guardandoInsumo() || this.guardandoMovimiento()) return;
    // El stock (insumos) y su gráfico se refrescan en segundo plano sin molestar. El
    // Historial NO se refresca solo: el usuario lo controla (filtros, búsqueda, paginación)
    // y recargarlo lo reiniciaría a la página 1 de forma incómoda. Se recarga solo cuando el
    // usuario entra a la pestaña o registra/edita/elimina un movimiento.
    this.cargar(true);
    this.cargarTendencia();
  }

  cargarHistorial() {
    this.cargandoHistorial.set(true);
    this.paginaHistorial.set(1);
    this.svc.movimientos(undefined, this.desdeHistorial, this.hastaHistorial).subscribe({
      next: list => { this.movimientos.set(list); this.cargandoHistorial.set(false); },
      error: () => this.cargandoHistorial.set(false)
    });
  }

  bajoStock(i: Insumo): boolean { return i.stockActual <= i.stockMinimo; }

  nivelStock(i: Insumo): number {
    if (i.stockMinimo <= 0) return i.stockActual > 0 ? 100 : 0;
    return Math.min(100, Math.max(0, (i.stockActual / (i.stockMinimo * 2)) * 100));
  }

  // ---------- Alta/edición ----------
  abrirCrearInsumo() {
    this.editandoInsumo.set(null);
    this.formInsumo = this.formInsumoVacio();
    this.errorFormInsumo.set(null);
    this.err.limpiarTodo();
    this.modalInsumo.set(true);
  }

  // ---------- Importación masiva ----------
  readonly importarAbierto = signal(false);
  readonly importando = signal(false);
  readonly columnasImport: ColumnaImport[] = [
    { clave: 'nombre', etiqueta: 'Nombre', requerido: true, tipo: 'texto' },
    { clave: 'unidadMedida', etiqueta: 'Unidad', requerido: true, tipo: 'texto' },
    { clave: 'stockActual', etiqueta: 'StockActual', tipo: 'numero', min: 0, max: 1000000 },
    { clave: 'stockMinimo', etiqueta: 'StockMinimo', tipo: 'numero', min: 0, max: 1000000 },
    { clave: 'clase', etiqueta: 'Clase', tipo: 'texto' },
  ];

  abrirImportar() { this.importarAbierto.set(true); }
  cerrarImportar() { if (!this.importando()) this.importarAbierto.set(false); }

  importarInsumos(filas: Array<Record<string, string | number | null>>) {
    if (this.importando()) return;
    this.importando.set(true);
    this.svc.importar(filas).subscribe({
      next: res => {
        this.importando.set(false);
        this.importarAbierto.set(false);
        const partes = [`${res.creados} insumo(s) creado(s)`];
        if (res.omitidos) partes.push(`${res.omitidos} omitido(s)`);
        this.toast.exito(partes.join(' · '));
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.importando.set(false);
        this.toast.desdeHttp(err, 'No se pudo importar el archivo.');
      }
    });
  }

  abrirEditarInsumo(i: Insumo) {
    this.editandoInsumo.set(i);
    this.formInsumo = { ...i };
    this.errorFormInsumo.set(null);
    this.err.limpiarTodo();
    this.modalInsumo.set(true);
  }

  cerrarModalInsumo() {
    if (!this.guardandoInsumo()) this.modalInsumo.set(false);
  }

  guardarInsumo() {
    const nombre = this.formInsumo.nombre?.trim() ?? '';
    const unidad = this.formInsumo.unidadMedida?.trim() ?? '';
    const stockMinimo = Number(this.formInsumo.stockMinimo ?? 0);
    const stockActual = Number(this.formInsumo.stockActual ?? 0);
    const errs: Record<string, string> = {};
    if (nombre.length < 2 || nombre.length > 80) errs['nombre'] = 'El nombre debe tener entre 2 y 80 caracteres.';
    else if (this.insumos().some(i => i.id !== this.editandoInsumo()?.id && this.normalizar(i.nombre) === this.normalizar(nombre)))
      errs['nombre'] = 'Ya existe un insumo con ese nombre en esta sede.';
    if (!unidad || unidad.length > 20) errs['unidad'] = 'Indica cómo lo cuentas (ej: bidón, bolsa, kg). Máx. 20 caracteres.';
    if (!Number.isFinite(stockMinimo) || stockMinimo < 0 || stockMinimo > 1_000_000)
      errs['stockMinimo'] = 'Debe ser un número entre 0 y 1,000,000.';
    if (!this.editandoInsumo() && (!Number.isFinite(stockActual) || stockActual < 0 || stockActual > 1_000_000))
      errs['stockActual'] = 'Debe ser un número entre 0 y 1,000,000.';

    this.err.set(errs);
    if (this.err.hay) { this.errorFormInsumo.set('Revisa los campos marcados en rojo.'); return; }
    // Fecha vacía → null (el input date da '' al borrarla, y el servidor espera fecha o null).
    const fechaVencimiento = this.formInsumo.fechaVencimiento || null;
    const fechaIngreso = this.formInsumo.fechaIngreso || null;
    this.formInsumo = { ...this.formInsumo, nombre, unidadMedida: unidad, stockMinimo, stockActual, fechaIngreso, fechaVencimiento };
    this.guardandoInsumo.set(true);
    this.errorFormInsumo.set(null);

    const edit = this.editandoInsumo();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, this.formInsumo)
      : this.svc.crear(this.formInsumo);

    obs$.subscribe({
      next: () => {
        this.guardandoInsumo.set(false);
        this.modalInsumo.set(false);
        this.err.limpiarTodo();
        this.toast.exito(edit ? 'Insumo actualizado' : 'Insumo registrado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoInsumo.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el insumo.';
        const low = msg.toLowerCase();
        const campo = low.includes('unidad') ? 'unidad'
          : low.includes('nombre') || low.includes('existe') ? 'nombre'
          : low.includes('mínimo') || low.includes('minimo') ? 'stockMinimo'
          : low.includes('inicial') ? 'stockActual' : null;
        if (campo) { this.err.marcar(campo, msg); this.errorFormInsumo.set('Revisa los campos marcados en rojo.'); }
        else this.errorFormInsumo.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(i: Insumo) { this.confirmarEliminar.set(i); }

  eliminar() {
    const i = this.confirmarEliminar();
    if (!i) return;
    this.guardandoInsumo.set(true);
    this.svc.desactivar(i.id).subscribe({
      next: res => {
        this.guardandoInsumo.set(false);
        this.confirmarEliminar.set(null);
        this.toast.exito(res.mensaje);
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoInsumo.set(false);
        this.toast.desdeHttp(err, 'No se pudo desactivar.');
      }
    });
  }

  toggleActivo(i: Insumo) {
    if (i.activo) { this.confirmarDesactivar.set(i); return; }
    this.aplicarCambioEstado(i, true);
  }

  confirmarDesactivarOk() {
    const i = this.confirmarDesactivar();
    if (!i) return;
    this.aplicarCambioEstado(i, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(i: Insumo, activo: boolean) {
    this.svc.cambiarEstado(i.id, activo).subscribe({
      next: () => {
        this.toast.info(activo ? 'Insumo reactivado' : 'Insumo desactivado');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  private formInsumoVacio(): Partial<Insumo> {
    return { nombre: '', unidadMedida: '', clase: 'INSUMO', stockActual: 0, stockMinimo: 0, activo: true, fechaIngreso: this.formatoFecha(new Date()) };
  }

  // ---------- Registrar movimiento ----------
  abrirModalMovimiento(i: Insumo) {
    if (!i.activo) {
      this.toast.advertencia('Reactiva el insumo antes de registrar movimientos.');
      return;
    }
    this.insumoMovimiento = i;
    this.movTipo = 'COMPRA';
    this.movCantidad = 0;
    this.movCosto = 0;
    this.movMetodoPago = 'EFECTIVO';
    this.movTipoGastoId = '';
    this.movDescripcion = '';
    this.movPesoActual = 0;
    this.movEsCorreccion = false;
    this.movFecha = this.formatoFecha(new Date());  // por defecto hoy
    this.modalMovimiento.set(true);
  }

  // ===== Medición por peso =====
  /** Unidad en la que se mide el peso: la del contenido (Kg/Lt) o, si no hay, la unidad base. */
  get unidadPeso(): string {
    const i = this.insumoMovimiento;
    return (i?.contenidoValor && i.contenidoValor > 0 && i.contenidoUnidad) ? i.contenidoUnidad : (i?.unidadMedida ?? '');
  }
  /** Cuántas unidades base (ej. Bidón) equivale 1 unidad de peso. Si no hay contenido, 1. */
  private get factorPeso(): number {
    const i = this.insumoMovimiento;
    return (i?.contenidoValor && i.contenidoValor > 0) ? i.contenidoValor : 1;
  }
  /** Stock actual expresado en peso (Kg/Lt). */
  get stockActualPeso(): number {
    return Math.round((this.insumoMovimiento?.stockActual ?? 0) * this.factorPeso * 1000) / 1000;
  }
  /** La medición ingresada, convertida a unidades base (Bidón). Redondeada a 3 decimales (precisión del stock). */
  private get medicionEnBase(): number {
    return Math.round(((Number(this.movPesoActual) || 0) / this.factorPeso) * 1000) / 1000;
  }
  /** Consumo del día en peso (positivo = consumió; negativo = aumentó). */
  get consumoDelDiaPeso(): number {
    return Math.round((this.stockActualPeso - (Number(this.movPesoActual) || 0)) * 1000) / 1000;
  }

  cerrarModalMovimiento() {
    if (!this.guardandoMovimiento()) this.modalMovimiento.set(false);
  }

  /** Explicación breve del tipo de movimiento elegido, para mostrarla dentro del modal. */
  get ayudaMovimiento(): string {
    switch (this.movTipo) {
      case 'COMPRA': return 'Entró material al almacén (sube el stock). Escribe cuánto entró.';
      case 'CONSUMO': return 'Salió material del almacén (baja el stock). Escribe cuánto se usó.';
      case 'AJUSTE': return 'Corrige el stock cuando el conteo real no coincide con el sistema. Usa negativo para restar, positivo para sumar.';
      default: return '';
    }
  }

  get puedeRegistrarMovimiento(): boolean {
    if (this.guardandoMovimiento()) return false;
    if (this.movTipo === 'MEDICION') {
      const peso = Number(this.movPesoActual);
      if (!Number.isFinite(peso) || peso < 0 || peso > 1_000_000) return false;
      // Debe haber un cambio real respecto al stock actual.
      return Math.abs(this.consumoDelDiaPeso) > 0.0001;
    }
    if (!Number.isFinite(this.movCantidad) || !Number.isFinite(this.movCosto)) return false;
    if (this.movCantidad === 0) return false;
    if (this.movTipo !== 'AJUSTE' && this.movCantidad <= 0) return false;
    if (Math.abs(this.movCantidad) > 1_000_000 || this.movCosto < 0 || this.movCosto > 1_000_000) return false;
    if (this.movTipo === 'CONSUMO' && this.movCantidad > (this.insumoMovimiento?.stockActual ?? 0)) return false;
    if (this.movFecha && this.movFecha > this.formatoFecha(new Date())) return false;
    return true;
  }

  confirmarMovimiento() {
    const i = this.insumoMovimiento;
    if (!i || !this.puedeRegistrarMovimiento) return;
    this.guardandoMovimiento.set(true);

    const req = this.movTipo === 'MEDICION'
      ? this.construirRequestMedicion(i)
      : {
          tipo: this.movTipo,
          cantidad: this.movCantidad,
          costoTotal: this.movTipo === 'COMPRA' && this.movCosto > 0 ? this.movCosto : null,
          metodoPago: this.movTipo === 'COMPRA' && this.movCosto > 0 ? this.movMetodoPago : null,
          tipoGastoId: this.movTipoGastoId ? (this.movTipoGastoId as number) : null,
          descripcion: this.movDescripcion.trim() || null,
          fecha: this.movFecha || null
        };

    this.svc.registrarMovimiento(i.id, req).subscribe({
      next: () => {
        this.guardandoMovimiento.set(false);
        this.modalMovimiento.set(false);
        this.toast.exito('Movimiento registrado');
        clearTimeout(this.insumoAnimadoTimer);
        this.insumoAnimadoId.set(i.id);
        this.insumoAnimadoTimer = setTimeout(() => {
          if (this.insumoAnimadoId() === i.id) this.insumoAnimadoId.set(null);
        }, 1200);
        // Refresco completo tras el movimiento: stock (tarjetas), gráfico de consumo e historial.
        this.cargar();
        this.cargarTendencia();
        if (this.tab() === 'historial') this.cargarHistorial();
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoMovimiento.set(false);
        this.toast.desdeHttp(err, 'No se pudo registrar el movimiento.');
      }
    });
  }

  /**
   * Convierte una medición por peso al movimiento correcto:
   * - Corrección o el stock subió → AJUSTE (deja el stock igual al peso medido, sin contar como consumo).
   * - El stock bajó (uso normal del día) → CONSUMO por la diferencia.
   * La cantidad siempre se envía en la unidad base (ej. Bidón).
   */
  private construirRequestMedicion(i: Insumo): RegistrarMovimientoInsumoRequest {
    const factor = (i.contenidoValor && i.contenidoValor > 0) ? i.contenidoValor : 1;
    const medicionBase = Math.round(((Number(this.movPesoActual) || 0) / factor) * 1000) / 1000;
    const deltaBase = Math.round((i.stockActual - medicionBase) * 1000) / 1000; // >0 consumió
    const unidad = this.unidadPeso;
    const peso = Number(this.movPesoActual) || 0;
    const nota = this.movDescripcion.trim();
    const sufijo = nota ? ` — ${nota}` : '';
    const fecha = this.movFecha || null;

    if (this.movEsCorreccion || deltaBase <= 0) {
      // AJUSTE: deja el stock exactamente en la medición (cantidad con signo).
      const cantidad = Math.round((medicionBase - i.stockActual) * 10000) / 10000;
      const motivo = this.movEsCorreccion
        ? `Corrección por medición: quedó en ${peso} ${unidad}`
        : `Medición: subió a ${peso} ${unidad}`;
      return { tipo: 'AJUSTE', cantidad, costoTotal: null, metodoPago: null, tipoGastoId: null, descripcion: `${motivo}${sufijo}`, fecha };
    }
    // CONSUMO del día por la diferencia.
    const consumoPeso = Math.round(deltaBase * factor * 1000) / 1000;
    return {
      tipo: 'CONSUMO',
      cantidad: deltaBase,
      costoTotal: null, metodoPago: null, tipoGastoId: null,
      descripcion: `Medición: pesó ${peso} ${unidad}. Consumo del día: ${consumoPeso} ${unidad}${sufijo}`,
      fecha
    };
  }

  // ---------- Editar / eliminar movimiento del historial (solo ADMIN) ----------
  readonly modalEditarMov = signal(false);
  editMovId: number | null = null;
  editMovInfo = '';
  editMovFecha = '';
  editMovDescripcion = '';
  readonly guardandoEditMov = signal(false);
  readonly confirmarEliminarMov = signal<MovimientoInsumo | null>(null);
  readonly eliminandoMov = signal(false);

  abrirEditarMovimiento(m: MovimientoInsumo) {
    this.editMovId = m.id;
    this.editMovInfo = `${this.etiquetaTipo(m.tipo)} · ${m.insumoNombre ?? ''} · ${m.cantidad}`;
    this.editMovFecha = (m.fecha || '').slice(0, 10);
    this.editMovDescripcion = m.descripcion ?? '';
    this.modalEditarMov.set(true);
  }
  cerrarEditarMov() { if (!this.guardandoEditMov()) this.modalEditarMov.set(false); }

  guardarEditarMovimiento() {
    if (this.editMovId == null || this.guardandoEditMov()) return;
    if (!/^\d{4}-\d{2}-\d{2}$/.test(this.editMovFecha) || this.editMovFecha > this.formatoFecha(new Date())) {
      this.toast.advertencia('Elige una fecha válida que no esté en el futuro.');
      return;
    }
    this.guardandoEditMov.set(true);
    this.svc.editarMovimiento(this.editMovId, { fecha: this.editMovFecha, descripcion: this.editMovDescripcion.trim() || null }).subscribe({
      next: () => {
        this.guardandoEditMov.set(false);
        this.modalEditarMov.set(false);
        this.toast.exito('Movimiento actualizado');
        this.cargarHistorial();
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoEditMov.set(false);
        this.toast.desdeHttp(err, 'No se pudo actualizar el movimiento.');
      }
    });
  }

  pedirEliminarMovimiento(m: MovimientoInsumo) { this.confirmarEliminarMov.set(m); }
  ejecutarEliminarMovimiento() {
    const m = this.confirmarEliminarMov();
    if (!m || this.eliminandoMov()) return;
    this.eliminandoMov.set(true);
    this.svc.eliminarMovimiento(m.id).subscribe({
      next: r => {
        this.eliminandoMov.set(false);
        this.confirmarEliminarMov.set(null);
        this.toast.exito(r?.mensaje ?? 'Movimiento eliminado');
        this.cargar();            // stock de las tarjetas
        this.cargarHistorial();   // lista
        this.cargarTendencia();   // gráfico de consumo
      },
      error: (err: HttpErrorResponse) => {
        this.eliminandoMov.set(false);
        this.confirmarEliminarMov.set(null);
        this.toast.desdeHttp(err, 'No se pudo eliminar el movimiento.');
      }
    });
  }

  etiquetaTipo(tipo: string): string {
    return ({ COMPRA: 'Compra', CONSUMO: 'Consumo', AJUSTE: 'Ajuste' } as Record<string, string>)[tipo] ?? tipo;
  }

  claseTipo(tipo: string): string {
    return ({ COMPRA: 'badge badge--verde', CONSUMO: 'badge badge--gris', AJUSTE: 'badge badge--azul' } as Record<string, string>)[tipo] ?? 'badge badge--gris';
  }

  private normalizar(valor: string): string {
    return valor.normalize('NFD').replace(/[\u0300-\u036f]/g, '').trim().toLocaleLowerCase('es');
  }
}
