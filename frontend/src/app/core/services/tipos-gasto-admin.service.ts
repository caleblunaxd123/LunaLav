import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface TipoGastoEditable {
  id: number;
  nombre: string;
  activo: boolean;
  enUso?: boolean;
}

@Injectable({ providedIn: 'root' })
export class TiposGastoAdminService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/tipos-gasto-admin`;

  listar() { return this.http.get<TipoGastoEditable[]>(this.base); }
  crear(t: Partial<TipoGastoEditable>) { return this.http.post<TipoGastoEditable>(this.base, t); }
  actualizar(id: number, t: Partial<TipoGastoEditable>) { return this.http.put<void>(`${this.base}/${id}`, t); }
  desactivar(id: number) { return this.http.delete<{ mensaje: string }>(`${this.base}/${id}`); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }
}
