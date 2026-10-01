import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface PermisoItem {
  rolId: number;
  modulo: string;
  puedeAcceder: boolean;
}

/** Sub-permiso (permiso fino) del catálogo: una sección o botón dentro de un módulo. */
export interface PermisoFino {
  clave: string;
  modulo: string;
  etiqueta: string;
  descripcion?: string | null;
}

@Injectable({ providedIn: 'root' })
export class PermisosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/permisos`;

  readonly modulosEtiquetas: Record<string, string> = {
    INICIO: 'Inicio',
    PEDIDOS: 'Pedidos',
    REGISTRAR: 'Registrar pedido',
    CAJA: 'Cuadre de Caja',
    CLIENTES: 'Clientes',
    PROMOCIONES: 'Promociones',
    REPORTES: 'Reportes',
    INVENTARIO: 'Inventario',
    AJUSTES: 'Ajustes',
  };

  modulos() { return this.http.get<string[]>(`${this.base}/modulos`); }
  /** Catálogo de sub-permisos (permisos finos) por módulo. */
  finos() { return this.http.get<PermisoFino[]>(`${this.base}/finos`); }
  obtenerMatriz() { return this.http.get<PermisoItem[]>(this.base); }
  guardar(permisos: PermisoItem[]) { return this.http.put<void>(this.base, { permisos }); }
}
