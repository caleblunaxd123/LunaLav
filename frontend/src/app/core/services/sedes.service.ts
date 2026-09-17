import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { Sede } from '../models/models';

@Injectable({ providedIn: 'root' })
export class SedesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/sedes`;

  listar() { return this.http.get<Sede[]>(this.base); }
  crear(s: Partial<Sede>) { return this.http.post<Sede>(this.base, s); }
  actualizar(id: number, s: Partial<Sede>) { return this.http.put<void>(`${this.base}/${id}`, s); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }
}
