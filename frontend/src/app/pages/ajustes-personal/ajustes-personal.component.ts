import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ErroresCampo } from '../../core/util/errores-campo';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Empleado, PersonalService } from '../../core/services/personal.service';
import { RolPersonal, RolesPersonalService } from '../../core/services/roles-personal.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { SoloDigitosDirective } from '../../shared/directives/solo-digitos.directive';
import { TelefonoPaisComponent } from '../../shared/telefono-pais/telefono-pais.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { esCelularValido } from '../../core/util/telefono';

@Component({
  selector: 'app-ajustes-personal',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent, SoloDigitosDirective, TelefonoPaisComponent],
  templateUrl: './ajustes-personal.component.html',
  styleUrl: './ajustes-personal.component.scss'
})
export class AjustesPersonalComponent implements OnInit {
  private readonly svc = inject(PersonalService);
  private readonly rolesSvc = inject(RolesPersonalService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly empleados = signal<Empleado[]>([]);
  readonly rolesDisponibles = signal<RolPersonal[]>([]);

  readonly busqueda = signal('');
  readonly empleadosFiltrados = computed(() => {
    const t = this.busqueda().trim().toLowerCase();
    if (!t) return this.empleados();
    return this.empleados().filter(e =>
      (e.nombre ?? '').toLowerCase().includes(t) ||
      (e.dni ?? '').toLowerCase().includes(t) ||
      (e.celular ?? '').toLowerCase().includes(t) ||
      (e.cargo ?? '').toLowerCase().includes(t));
  });
  actualizarBusqueda(v: string) { this.busqueda.set(v); this.pagina.set(1); }

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly empleadosPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.empleadosFiltrados().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly modalAbierto = signal(false);
  readonly editando = signal<Empleado | null>(null);
  readonly confirmarEliminar = signal<Empleado | null>(null);
  readonly confirmarDesactivar = signal<Empleado | null>(null);
  form: Partial<Empleado> = this.formVacio();
  errorForm = signal<string | null>(null);
  readonly err = new ErroresCampo();
  guardando = signal(false);

  ngOnInit() {
    this.cargar();
    this.rolesSvc.listar().subscribe(list => this.rolesDisponibles.set(list.filter(r => r.activo)));
  }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.empleados.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar el personal.'));
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

  abrirEditar(e: Empleado) {
    this.editando.set(e);
    this.form = { ...e };
    this.errorForm.set(null);
    this.err.limpiarTodo();
    this.modalAbierto.set(true);
  }

  cerrar() { this.modalAbierto.set(false); }

  guardar() {
    const dni = (this.form.dni ?? '').toString().trim();
    const celular = (this.form.celular ?? '').toString().trim();
    const errs: Record<string, string> = {};
    if (!this.form.nombre?.trim()) errs['nombre'] = 'Ingresa el nombre.';
    if (dni && !/^\d{8}$/.test(dni)) errs['dni'] = 'El DNI debe tener 8 dígitos (o déjalo vacío).';
    if (celular && !esCelularValido(celular)) errs['celular'] = 'Revisa el celular: 9 dígitos para Perú, o elige el país para un número extranjero (o déjalo vacío).';
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
        this.toast.exito(edit ? 'Empleado actualizado' : 'Empleado registrado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el empleado.';
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(e: Empleado) { this.confirmarEliminar.set(e); }

  eliminar() {
    const e = this.confirmarEliminar();
    if (!e) return;
    this.guardando.set(true);
    this.svc.desactivar(e.id).subscribe({
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

  toggleActivo(e: Empleado) {
    if (e.activo) { this.confirmarDesactivar.set(e); return; }
    this.aplicarCambioEstado(e, true);
  }

  confirmarDesactivarOk() {
    const e = this.confirmarDesactivar();
    if (!e) return;
    this.aplicarCambioEstado(e, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(e: Empleado, activo: boolean) {
    this.svc.cambiarEstado(e.id, activo).subscribe({
      next: () => {
        this.toast.info(activo ? 'Empleado reactivado' : 'Empleado desactivado');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(): Partial<Empleado> {
    return { nombre: '', dni: '', celular: '', cargo: '', fechaIngreso: null, activo: true };
  }
}
