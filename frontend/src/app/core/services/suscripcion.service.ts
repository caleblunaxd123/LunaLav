import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MiSuscripcion } from '../models/models';

/** Aviso de vencimiento de la suscripción de la propia empresa (lo ve cualquier
 * usuario autenticado del tenant en su dashboard). Backend: GET /api/suscripcion/mia. */
@Injectable({ providedIn: 'root' })
export class SuscripcionService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/suscripcion`;

  mia(): Observable<MiSuscripcion> {
    return this.http.get<MiSuscripcion>(`${this.base}/mia`);
  }
}
