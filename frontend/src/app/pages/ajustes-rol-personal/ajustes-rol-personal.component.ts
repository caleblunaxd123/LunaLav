import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ErroresCampo } from '../../core/util/errores-campo';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { RolPersonal, RolesPersonalService } from '../../core/services/roles-personal.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-ajustes-rol-personal',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './ajustes-rol-personal.component.html',
  styleUrl: './ajustes-rol-personal.component.scss'
})
export class AjustesRolPersonalComponent implements OnInit {
  private readonly svc = inject(RolesPersonalService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly roles = signal<RolPersonal[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly busqueda = signal('');
  readonly rolesFiltrados = computed(() => {
    const t = this.busqueda().trim().toLowerCase();
    if (!t) return this.roles();
    return this.roles().filter(r => (r.nombre ?? '').toLowerCase().includes(t));
  });
  actualizarBusqueda(v: string) { this.busqueda.set(v); this.pagina.set(1); }

  readonly rolesPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.rolesFiltrados().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  readonly modalAbierto = signal(false);
  readonly editando = signal<RolPersonal | null>(null);
  readonly confirmarEliminar = signal<RolPersonal | null>(null);
  readonly confirmarDesactivar = signal<RolPersonal | null>(null);
  form: Partial<RolPersonal> = this.formVacio();
  errorForm = signal<string | null>(null);
  readonly err = new ErroresCampo();
  guardando = signal(false);

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.roles.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar los roles.'));
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

  abrirEditar(r: RolPersonal) {
    this.editando.set(r);
    this.form = { ...r };
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
        this.toast.exito(edit ? 'Cargo actualizado' : 'Cargo creado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el cargo.';
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(r: RolPersonal) { this.confirmarEliminar.set(r); }

  eliminar() {
    const r = this.confirmarEliminar();
    if (!r) return;
    this.guardando.set(true);
    this.svc.desactivar(r.id).subscribe({
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

  toggleActivo(r: RolPersonal) {
    if (r.activo) { this.confirmarDesactivar.set(r); return; }
    this.aplicarCambioEstado(r, true);
  }

  confirmarDesactivarOk() {
    const r = this.confirmarDesactivar();
    if (!r) return;
    this.aplicarCambioEstado(r, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(r: RolPersonal, activo: boolean) {
    this.svc.cambiarEstado(r.id, activo).subscribe({
      next: () => {
        this.toast.info(activo ? 'Rol reactivado' : 'Rol desactivado');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(): Partial<RolPersonal> {
    return { nombre: '', activo: true };
  }
}
