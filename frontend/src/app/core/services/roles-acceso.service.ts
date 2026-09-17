import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface RolAcceso {
  id: number;
  nombre: string;
  esSistema: boolean;   // ADMIN: fijo, no editable ni eliminable
  enUso: boolean;       // tiene usuarios asignados (no se puede eliminar)
}

/** CRUD de los roles de acceso PROPIOS del negocio (flexibles). */
@Injectable({ providedIn: 'root' })
export class RolesAccesoService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/roles-acceso`;

  listar() { return this.http.get<RolAcceso[]>(this.base); }
  crear(nombre: string) { return this.http.post<RolAcceso>(this.base, { nombre }); }
  renombrar(id: number, nombre: string) { return this.http.put<void>(`${this.base}/${id}`, { nombre }); }
  eliminar(id: number) { return this.http.delete<void>(`${this.base}/${id}`); }
}
