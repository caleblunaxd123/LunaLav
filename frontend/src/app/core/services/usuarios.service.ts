import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface UsuarioAdmin {
  id: number;
  usuario: string;
  nombreCompleto: string;
  email?: string | null;
  password?: string | null;
  rolId: number;
  sedeId?: number | null;
  sedeNombre?: string | null;
  rolCodigo?: string;
  rolNombre?: string;
  activo: boolean;
}

export interface Rol {
  id: number;
  codigo: string;
  nombre: string;
}

@Injectable({ providedIn: 'root' })
export class UsuariosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/usuarios`;

  listar() { return this.http.get<UsuarioAdmin[]>(this.base); }
  roles() { return this.http.get<Rol[]>(`${this.base}/roles`); }
  crear(u: Partial<UsuarioAdmin>) { return this.http.post<UsuarioAdmin>(this.base, u); }
  actualizar(id: number, u: Partial<UsuarioAdmin>) { return this.http.put<void>(`${this.base}/${id}`, u); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }
}
