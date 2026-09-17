import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface PlantillaWhatsappEditable {
  id: number;
  evento: string;
  mensaje: string;
  activa: boolean;
}

@Injectable({ providedIn: 'root' })
export class PlantillasWhatsappAdminService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/plantillas-whatsapp-admin`;

  listar() { return this.http.get<PlantillaWhatsappEditable[]>(this.base); }
  actualizar(id: number, p: Partial<PlantillaWhatsappEditable>) { return this.http.put<void>(`${this.base}/${id}`, p); }
}
