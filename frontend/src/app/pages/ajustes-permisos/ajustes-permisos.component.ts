import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { PermisoItem, PermisosService } from '../../core/services/permisos.service';
import { RolAcceso, RolesAccesoService } from '../../core/services/roles-acceso.service';
import { ToastService } from '../../core/services/toast.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-ajustes-permisos',
  imports: [PageHeaderComponent, CommonModule, FormsModule, IconComponent],
  templateUrl: './ajustes-permisos.component.html',
  styleUrl: './ajustes-permisos.component.scss'
})
export class AjustesPermisosComponent implements OnInit {
  private readonly svc = inject(PermisosService);
  private readonly rolesSvc = inject(RolesAccesoService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly cargando = signal(false);
  readonly guardando = signal(false);
  readonly roles = signal<RolAcceso[]>([]);
  readonly modulos = signal<string[]>([]);
  readonly matriz = signal<Map<string, boolean>>(new Map());

  // Los roles editables (columnas de la matriz) = todos menos el de sistema (Administrador).
  readonly rolesEditables = computed(() => this.roles().filter(r => !r.esSistema));
  readonly etiquetas = this.svc.modulosEtiquetas;

  // Modal crear / renombrar rol.
  readonly modalAbierto = signal(false);
  readonly editandoRol = signal<RolAcceso | null>(null);
  readonly guardandoRol = signal(false);
  readonly errorRol = signal<string | null>(null);
  nombreRol = '';
  readonly confirmarEliminar = signal<RolAcceso | null>(null);

  ngOnInit() { this.cargar(); }

  private clave(rolId: number, modulo: string) { return `${rolId}::${modulo}`; }

  cargar() {
    this.cargando.set(true);
    this.rolesSvc.listar().subscribe({
      next: roles => {
        this.roles.set(roles);
        this.svc.modulos().subscribe(modulos => {
          this.modulos.set(modulos);
          this.svc.obtenerMatriz().subscribe({
            next: (items: PermisoItem[]) => {
              const map = new Map<string, boolean>();
              for (const it of items) map.set(this.clave(it.rolId, it.modulo), it.puedeAcceder);
              this.matriz.set(map);
              this.cargando.set(false);
            },
            error: (err: HttpErrorResponse) => {
              this.cargando.set(false);
              this.toast.desdeHttp(err, 'No se pudo cargar la matriz de permisos.');
            }
          });
        });
      },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.toast.desdeHttp(err, 'No se pudieron cargar los roles.');
      }
    });
  }

  tienePermiso(rolId: number, modulo: string): boolean {
    return this.matriz().get(this.clave(rolId, modulo)) ?? false;
  }

  toggle(rolId: number, modulo: string) {
    const key = this.clave(rolId, modulo);
    const actual = this.matriz().get(key) ?? false;
    const nuevo = new Map(this.matriz());
    nuevo.set(key, !actual);
    this.matriz.set(nuevo);
  }

  guardar() {
    this.guardando.set(true);
    const permisos: PermisoItem[] = [];
    for (const r of this.rolesEditables()) {
      for (const m of this.modulos()) {
        permisos.push({ rolId: r.id, modulo: m, puedeAcceder: this.tienePermiso(r.id, m) });
      }
    }
    this.svc.guardar(permisos).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.exito('Permisos actualizados. Los usuarios verán los cambios en su próximo inicio de sesión.');
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar la matriz de permisos.');
      }
    });
  }

  // --- Gestión de roles ---
  abrirCrearRol() {
    this.editandoRol.set(null);
    this.nombreRol = '';
    this.errorRol.set(null);
    this.modalAbierto.set(true);
  }

  abrirEditarRol(r: RolAcceso) {
    this.editandoRol.set(r);
    this.nombreRol = r.nombre;
    this.errorRol.set(null);
    this.modalAbierto.set(true);
  }

  cerrarModal() { this.modalAbierto.set(false); }

  guardarRol() {
    const nombre = this.nombreRol.trim();
    if (nombre.length < 2 || nombre.length > 60) {
      this.errorRol.set('El nombre del rol debe tener entre 2 y 60 caracteres.');
      return;
    }
    this.guardandoRol.set(true);
    this.errorRol.set(null);
    const edit = this.editandoRol();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.rolesSvc.renombrar(edit.id, nombre)
      : this.rolesSvc.crear(nombre);
    obs$.subscribe({
      next: () => {
        this.guardandoRol.set(false);
        this.modalAbierto.set(false);
        this.toast.exito(edit ? 'Rol actualizado' : 'Rol creado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoRol.set(false);
        this.errorRol.set(err.error?.mensaje ?? 'No se pudo guardar el rol.');
      }
    });
  }

  pedirEliminar(r: RolAcceso) { this.confirmarEliminar.set(r); }

  eliminarRol() {
    const r = this.confirmarEliminar();
    if (!r) return;
    this.rolesSvc.eliminar(r.id).subscribe({
      next: () => {
        this.confirmarEliminar.set(null);
        this.toast.info('Rol eliminado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.confirmarEliminar.set(null);
        this.toast.desdeHttp(err, err.error?.mensaje ?? 'No se pudo eliminar el rol.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
