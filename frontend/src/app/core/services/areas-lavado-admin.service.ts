import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface AreaLavadoEditable {
  id: number;
  nombre: string;
  orden: number;
  tiempoEstMinutos: number;
  activa: boolean;
  enUso?: boolean;
}

@Injectable({ providedIn: 'root' })
export class AreasLavadoAdminService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/areas-lavado-admin`;

  listar() { return this.http.get<AreaLavadoEditable[]>(this.base); }
  crear(a: Partial<AreaLavadoEditable>) { return this.http.post<AreaLavadoEditable>(this.base, a); }
  actualizar(id: number, a: Partial<AreaLavadoEditable>) { return this.http.put<void>(`${this.base}/${id}`, a); }
  desactivar(id: number) { return this.http.delete<{ mensaje: string }>(`${this.base}/${id}`); }
  cambiarEstado(id: number, activa: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo: activa }); }
}
