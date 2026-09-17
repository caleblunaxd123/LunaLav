import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AreaLavado, Servicio } from '../models/models';

@Injectable({ providedIn: 'root' })
export class CatalogosService {
  private readonly http = inject(HttpClient);

  servicios() {
    return this.http.get<Servicio[]>(`${environment.apiUrl}/servicios`);
  }

  /** Alta rápida de un servicio al registrar un pedido (accesible al módulo PEDIDOS). */
  crearServicioRapido(nombre: string, precio: number, unidad: string) {
    return this.http.post<Servicio>(`${environment.apiUrl}/servicios`, { nombre, precio, unidad });
  }

  areasLavado() {
    return this.http.get<AreaLavado[]>(`${environment.apiUrl}/areas-lavado`);
  }
}
