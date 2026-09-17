import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface PermisoItem {
  rolId: number;
  modulo: string;
  puedeAcceder: boolean;
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
  obtenerMatriz() { return this.http.get<PermisoItem[]>(this.base); }
  guardar(permisos: PermisoItem[]) { return this.http.put<void>(this.base, { permisos }); }
}
