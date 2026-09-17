import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface Categoria {
  id: number;
  nombre: string;
  activa: boolean;
  enUso?: boolean;
}

@Injectable({ providedIn: 'root' })
export class CategoriasService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/categorias`;

  listar() { return this.http.get<Categoria[]>(this.base); }
  crear(c: Partial<Categoria>) { return this.http.post<Categoria>(this.base, c); }
  actualizar(id: number, c: Partial<Categoria>) { return this.http.put<void>(`${this.base}/${id}`, c); }
  desactivar(id: number) { return this.http.delete<{ mensaje: string }>(`${this.base}/${id}`); }
  cambiarEstado(id: number, activa: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo: activa }); }
}
