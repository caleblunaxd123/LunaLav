import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface Empleado {
  id: number;
  nombre: string;
  dni: string | null;
  celular: string | null;
  cargo: string | null;
  fechaIngreso: string | null;
  activo: boolean;
}

@Injectable({ providedIn: 'root' })
export class PersonalService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/personal`;

  listar() { return this.http.get<Empleado[]>(this.base); }
  crear(e: Partial<Empleado>) { return this.http.post<Empleado>(this.base, e); }
  actualizar(id: number, e: Partial<Empleado>) { return this.http.put<void>(`${this.base}/${id}`, e); }
  desactivar(id: number) { return this.http.delete<{ mensaje: string }>(`${this.base}/${id}`); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }
}
