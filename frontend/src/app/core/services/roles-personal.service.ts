import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface RolPersonal {
  id: number;
  nombre: string;
  activo: boolean;
}

@Injectable({ providedIn: 'root' })
export class RolesPersonalService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/roles-personal`;

  listar() { return this.http.get<RolPersonal[]>(this.base); }
  crear(r: Partial<RolPersonal>) { return this.http.post<RolPersonal>(this.base, r); }
  actualizar(id: number, r: Partial<RolPersonal>) { return this.http.put<void>(`${this.base}/${id}`, r); }
  desactivar(id: number) { return this.http.delete<{ mensaje: string }>(`${this.base}/${id}`); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }
}
