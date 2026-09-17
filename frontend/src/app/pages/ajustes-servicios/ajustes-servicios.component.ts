import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ErroresCampo } from '../../core/util/errores-campo';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ServicioEditable, ServiciosAdminService } from '../../core/services/servicios-admin.service';
import { Categoria, CategoriasService } from '../../core/services/categorias.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { ColumnaImport, ImportadorMasivoComponent } from '../../shared/importador-masivo/importador-masivo.component';

type FiltroEstadoServicio = 'todos' | 'activos' | 'inactivos';

@Component({
  selector: 'app-ajustes-servicios',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent, ImportadorMasivoComponent],
  templateUrl: './ajustes-servicios.component.html',
  styleUrl: './ajustes-servicios.component.scss'
})
export class AjustesServiciosComponent implements OnInit {
  private readonly svc = inject(ServiciosAdminService);
  private readonly categoriasSvc = inject(CategoriasService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly servicios = signal<ServicioEditable[]>([]);
  readonly categorias = signal<Categoria[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly busqueda = signal('');
  readonly filtroEstado = signal<FiltroEstadoServicio>('todos');
  readonly filtroCategoria = signal<number | 'todas'>('todas');

  readonly serviciosFiltrados = computed(() => {
    const texto = this.normalizar(this.busqueda());
    const estado = this.filtroEstado();
    const categoria = this.filtroCategoria();
    return [...this.servicios()]
      .filter(s =>
        // El texto libre busca por NOMBRE (y unidad). La categoría NO entra aquí a propósito:
        // como casi todos los servicios comparten categoría (ej. "Lavado al agua"), incluirla
        // hacía que escribir esa palabra mostrara todo. Para filtrar por categoría está el
        // desplegable de al lado.
        (!texto || this.normalizar(`${s.nombre} ${s.unidad}`).includes(texto)) &&
        (estado === 'todos' || (estado === 'activos' ? s.activo : !s.activo)) &&
        (categoria === 'todas' || s.categoriaId === categoria)
      )
      .sort((a, b) => Number(b.activo) - Number(a.activo) || a.nombre.localeCompare(b.nombre, 'es'));
  });
  readonly totalActivos = computed(() => this.servicios().filter(s => s.activo).length);
  readonly totalInactivos = computed(() => this.servicios().length - this.totalActivos());
  readonly precioPromedio = computed(() => {
    const activos = this.servicios().filter(s => s.activo);
    return activos.length ? activos.reduce((suma, s) => suma + s.precio, 0) / activos.length : 0;
  });

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly serviciosPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.serviciosFiltrados().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }
  actualizarBusqueda(valor: string) { this.busqueda.set(valor); this.pagina.set(1); }
  actualizarEstado(valor: FiltroEstadoServicio) { this.filtroEstado.set(valor); this.pagina.set(1); }
  actualizarCategoria(valor: number | 'todas') { this.filtroCategoria.set(valor); this.pagina.set(1); }
  limpiarFiltros() {
    this.busqueda.set('');
    this.filtroEstado.set('todos');
    this.filtroCategoria.set('todas');
    this.pagina.set(1);
  }

  readonly modalAbierto = signal(false);
  readonly editando = signal<ServicioEditable | null>(null);
  readonly confirmarEliminar = signal<ServicioEditable | null>(null);
  readonly confirmarDesactivar = signal<ServicioEditable | null>(null);
  form: Partial<ServicioEditable> = this.formVacio();
  errorForm = signal<string | null>(null);
  readonly err = new ErroresCampo();
  guardando = signal(false);

  unidades = ['kg', 'prenda', 'pieza', 'und', 'servicio', 'm2'];

  ngOnInit() {
    this.cargar();
    this.categoriasSvc.listar().subscribe(list => this.categorias.set(list));
  }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.servicios.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar servicios.'));
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

  abrirEditar(s: ServicioEditable) {
    this.editando.set(s);
    this.form = { ...s };
    this.errorForm.set(null);
    this.err.limpiarTodo();
    this.modalAbierto.set(true);
  }

  cerrar() {
    if (this.guardando()) return;
    this.modalAbierto.set(false);
  }

  guardar() {
    if (this.guardando()) return;
    const nombre = this.form.nombre?.trim() ?? '';
    const unidad = this.form.unidad?.trim() ?? '';
    const precio = Number(this.form.precio ?? 0);

    const costo = Number(this.form.costo ?? 0);
    const editandoId = this.editando()?.id;
    const errs: Record<string, string> = {};

    if (nombre.length < 2 || nombre.length > 120) errs['nombre'] = 'El nombre debe tener entre 2 y 120 caracteres.';
    else if (this.servicios().some(s => s.id !== editandoId && this.normalizar(s.nombre) === this.normalizar(nombre)))
      errs['nombre'] = `Ya existe un servicio llamado “${nombre}”. Usa un nombre diferente.`;
    if (!unidad) errs['unidad'] = 'Selecciona la unidad de cobro del servicio.';
    if (!Number.isFinite(precio) || precio <= 0 || precio > 10_000)
      errs['precio'] = 'Ingresa un precio mayor a S/ 0.00 y hasta S/ 10,000.00.';
    if (!Number.isFinite(costo) || costo < 0 || costo > 10_000)
      errs['costo'] = 'El costo debe estar entre S/ 0.00 y S/ 10,000.00.';

    this.err.set(errs);
    if (this.err.hay) { this.errorForm.set('Revisa los campos marcados en rojo.'); return; }
    this.form = { ...this.form, nombre, unidad, precio: Math.round(precio * 100) / 100, costo: Math.round(costo * 100) / 100 };
    this.guardando.set(true);
    this.errorForm.set(null);

    const edit = this.editando();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, { ...edit, ...this.form } as ServicioEditable)
      : this.svc.crear(this.form);

    obs$.subscribe({
      next: () => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.err.limpiarTodo();
        this.toast.exito(edit ? 'Servicio actualizado' : 'Servicio creado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el servicio.';
        const low = msg.toLowerCase();
        const campo = low.includes('precio') ? 'precio' : low.includes('costo') ? 'costo'
          : low.includes('unidad') ? 'unidad' : (low.includes('nombre') || low.includes('existe')) ? 'nombre' : null;
        if (campo) { this.err.marcar(campo, msg); this.errorForm.set('Revisa los campos marcados en rojo.'); }
        else this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(s: ServicioEditable) { this.confirmarEliminar.set(s); }

  eliminar() {
    const s = this.confirmarEliminar();
    if (!s) return;
    this.guardando.set(true);
    this.svc.desactivar(s.id).subscribe({
      next: res => {
        this.guardando.set(false);
        this.confirmarEliminar.set(null);
        this.toast.exito(res.mensaje);
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo desactivar.');
      }
    });
  }

  toggleActivo(s: ServicioEditable) {
    if (s.activo) { this.confirmarDesactivar.set(s); return; }
    this.aplicarCambioEstado(s, true);
  }

  confirmarDesactivarOk() {
    const s = this.confirmarDesactivar();
    if (!s) return;
    this.aplicarCambioEstado(s, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(s: ServicioEditable, activo: boolean) {
    if (this.guardando()) return;
    this.guardando.set(true);
    const actualizado = { ...s, activo };
    this.svc.actualizar(s.id, actualizado).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.info(activo ? 'Servicio reactivado' : 'Servicio desactivado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo cambiar el estado.');
      }
    });
  }

  // ---------- Importación masiva (usa el componente reutilizable app-importador-masivo) ----------
  readonly importarAbierto = signal(false);
  readonly importando = signal(false);
  readonly crearCategorias = signal(true);
  readonly columnasImport: ColumnaImport[] = [
    { clave: 'nombre', etiqueta: 'Nombre', requerido: true, tipo: 'texto' },
    { clave: 'precio', etiqueta: 'Precio', requerido: true, tipo: 'numero', min: 0.01, max: 10000 },
    { clave: 'unidad', etiqueta: 'Unidad', tipo: 'texto' },
    { clave: 'categoria', etiqueta: 'Categoria', tipo: 'texto' },
  ];
  readonly nombresExistentes = computed(() =>
    new Set(this.servicios().map(s => this.normalizar(s.nombre))));

  abrirImportar() { this.crearCategorias.set(true); this.importarAbierto.set(true); }
  cerrarImportar() { if (!this.importando()) this.importarAbierto.set(false); }

  importarServicios(filas: Array<Record<string, string | number | null>>) {
    if (this.importando()) return;
    this.importando.set(true);
    const payload = filas.map(f => ({
      nombre: String(f['nombre'] ?? ''),
      precio: Number(f['precio'] ?? 0),
      unidad: (f['unidad'] != null ? String(f['unidad']).trim() : '') || 'und',
      categoria: f['categoria'] != null ? String(f['categoria']).trim() || null : null
    }));
    this.svc.importar(payload, this.crearCategorias()).subscribe({
      next: res => {
        this.importando.set(false);
        this.importarAbierto.set(false);
        const partes = [`${res.creados} servicio(s) creado(s)`];
        if (res.categoriasCreadas.length) partes.push(`${res.categoriasCreadas.length} categoría(s) nueva(s)`);
        if (res.omitidos) partes.push(`${res.omitidos} omitido(s)`);
        this.toast.exito(partes.join(' · '));
        this.categoriasSvc.listar().subscribe(list => this.categorias.set(list));
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.importando.set(false);
        this.toast.desdeHttp(err, 'No se pudo importar el archivo.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(): Partial<ServicioEditable> {
    return { nombre: '', precio: 0, costo: 0, unidad: 'prenda', categoriaId: null, activo: true };
  }

  private normalizar(valor: string): string {
    return valor.normalize('NFD').replace(/[\u0300-\u036f]/g, '').trim().toLowerCase();
  }
}
