import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ErroresCampo } from '../../core/util/errores-campo';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TipoGastoEditable, TiposGastoAdminService } from '../../core/services/tipos-gasto-admin.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-ajustes-tipos-gasto',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './ajustes-tipos-gasto.component.html',
  styleUrl: './ajustes-tipos-gasto.component.scss'
})
export class AjustesTiposGastoComponent implements OnInit {
  private readonly svc = inject(TiposGastoAdminService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly tipos = signal<TipoGastoEditable[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly busqueda = signal('');
  readonly tiposFiltrados = computed(() => {
    const t = this.busqueda().trim().toLowerCase();
    if (!t) return this.tipos();
    return this.tipos().filter(x => (x.nombre ?? '').toLowerCase().includes(t));
  });
  actualizarBusqueda(v: string) { this.busqueda.set(v); this.pagina.set(1); }

  readonly tiposPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.tiposFiltrados().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  readonly modalAbierto = signal(false);
  readonly editando = signal<TipoGastoEditable | null>(null);
  readonly confirmarEliminar = signal<TipoGastoEditable | null>(null);
  readonly confirmarDesactivar = signal<TipoGastoEditable | null>(null);
  form: Partial<TipoGastoEditable> = this.formVacio();
  errorForm = signal<string | null>(null);
  readonly err = new ErroresCampo();
  guardando = signal(false);

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.tipos.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar tipos de gasto.'));
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

  abrirEditar(t: TipoGastoEditable) {
    this.editando.set(t);
    this.form = { ...t };
    this.errorForm.set(null);
    this.err.limpiarTodo();
    this.modalAbierto.set(true);
  }

  cerrar() { this.modalAbierto.set(false); }

  guardar() {
    if (!this.form.nombre?.trim()) {
      this.err.set({ nombre: 'Ingresa el nombre.' });
      this.errorForm.set(null);
      return;
    }
    this.err.limpiarTodo();
    this.guardando.set(true);
    this.errorForm.set(null);

    const edit = this.editando();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, this.form)
      : this.svc.crear(this.form);

    obs$.subscribe({
      next: () => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.toast.exito(edit ? 'Tipo de gasto actualizado' : 'Tipo de gasto creado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el tipo de gasto.';
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(t: TipoGastoEditable) { this.confirmarEliminar.set(t); }

  eliminar() {
    const t = this.confirmarEliminar();
    if (!t) return;
    this.guardando.set(true);
    this.svc.desactivar(t.id).subscribe({
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

  toggleActivo(t: TipoGastoEditable) {
    if (t.activo) { this.confirmarDesactivar.set(t); return; }
    this.aplicarCambioEstado(t, true);
  }

  confirmarDesactivarOk() {
    const t = this.confirmarDesactivar();
    if (!t) return;
    this.aplicarCambioEstado(t, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(t: TipoGastoEditable, activo: boolean) {
    this.svc.cambiarEstado(t.id, activo).subscribe({
      next: () => {
        this.toast.info(activo ? 'Tipo de gasto reactivado' : 'Tipo de gasto desactivado');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(): Partial<TipoGastoEditable> {
    return { nombre: '', activo: true };
  }
}
