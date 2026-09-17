import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ErroresCampo } from '../../core/util/errores-campo';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AreaLavadoEditable, AreasLavadoAdminService } from '../../core/services/areas-lavado-admin.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-ajustes-areas',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './ajustes-areas.component.html',
  styleUrl: './ajustes-areas.component.scss'
})
export class AjustesAreasComponent implements OnInit {
  private readonly svc = inject(AreasLavadoAdminService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly areas = signal<AreaLavadoEditable[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly busqueda = signal('');
  readonly areasFiltradas = computed(() => {
    const t = this.busqueda().trim().toLowerCase();
    if (!t) return this.areas();
    return this.areas().filter(a => (a.nombre ?? '').toLowerCase().includes(t));
  });
  actualizarBusqueda(v: string) { this.busqueda.set(v); this.pagina.set(1); }

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly areasPaginadas = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.areasFiltradas().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  readonly modalAbierto = signal(false);
  readonly editando = signal<AreaLavadoEditable | null>(null);
  readonly confirmarEliminar = signal<AreaLavadoEditable | null>(null);
  readonly confirmarDesactivar = signal<AreaLavadoEditable | null>(null);
  form: Partial<AreaLavadoEditable> = this.formVacio();
  errorForm = signal<string | null>(null);
  readonly err = new ErroresCampo();
  guardando = signal(false);

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.areas.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar áreas de lavado.'));
      }
    });
  }

  abrirCrear() {
    this.editando.set(null);
    const maxOrden = this.areas().reduce((max, a) => Math.max(max, a.orden), 0);
    this.form = this.formVacio(maxOrden + 1);
    this.errorForm.set(null);
    this.err.limpiarTodo();
    this.modalAbierto.set(true);
  }

  abrirEditar(a: AreaLavadoEditable) {
    this.editando.set(a);
    this.form = { ...a };
    this.errorForm.set(null);
    this.err.limpiarTodo();
    this.modalAbierto.set(true);
  }

  cerrar() { this.modalAbierto.set(false); }

  guardar() {
    const errs: Record<string, string> = {};
    if (!this.form.nombre?.trim()) errs['nombre'] = 'Ingresa el nombre del área.';
    if (!this.form.orden || this.form.orden <= 0) errs['orden'] = 'El orden debe ser un número mayor a 0.';
    this.err.set(errs);
    if (this.err.hay) { this.errorForm.set('Revisa los campos marcados en rojo.'); return; }
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
        this.toast.exito(edit ? 'Área actualizada' : 'Área creada');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el área.';
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(a: AreaLavadoEditable) { this.confirmarEliminar.set(a); }

  eliminar() {
    const a = this.confirmarEliminar();
    if (!a) return;
    this.guardando.set(true);
    this.svc.desactivar(a.id).subscribe({
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

  toggleActiva(a: AreaLavadoEditable) {
    if (a.activa) { this.confirmarDesactivar.set(a); return; }
    this.aplicarCambioEstado(a, true);
  }

  confirmarDesactivarOk() {
    const a = this.confirmarDesactivar();
    if (!a) return;
    this.aplicarCambioEstado(a, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(a: AreaLavadoEditable, activa: boolean) {
    this.svc.cambiarEstado(a.id, activa).subscribe({
      next: () => {
        this.toast.info(activa ? 'Área reactivada' : 'Área desactivada');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(orden = 1): Partial<AreaLavadoEditable> {
    return { nombre: '', orden, tiempoEstMinutos: 30, activa: true };
  }
}
