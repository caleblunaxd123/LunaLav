import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface Motorizado {
  id: number;
  nombre: string;
  celular: string | null;
  activo: boolean;
}

@Injectable({ providedIn: 'root' })
export class MotorizadosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/motorizados`;

  listarActivos() { return this.http.get<Motorizado[]>(this.base); }
  listarTodos() { return this.http.get<Motorizado[]>(`${this.base}/todos`); }
  crear(m: Partial<Motorizado>) { return this.http.post<Motorizado>(this.base, m); }
  actualizar(id: number, m: Partial<Motorizado>) { return this.http.put<void>(`${this.base}/${id}`, m); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }
}
