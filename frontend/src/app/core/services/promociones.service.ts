import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface Promocion {
  id: number;
  tipo: 'VOLUMEN' | 'FRECUENCIA' | 'FIJA' | 'CODIGO' | string;
  descripcion: string;
  descuentoPct: number | null;
  descuentoMonto: number | null;
  servicioId: number | null;
  servicioNombre?: string | null;
  cantidadMinima: number;
  fechaInicio: string | null;
  fechaFin: string | null;
  activa: boolean;
  codigo: string | null;
  // Códigos generados
  clienteId?: number | null;
  clienteNombre?: string | null;
  origen?: 'NUEVO' | 'CUMPLE' | 'REFERIDO' | 'PUNTOS' | 'MANUAL' | null;
  maxUsos?: number | null;
  usos?: number;
}

export type OrigenCodigo = 'NUEVO' | 'CUMPLE' | 'REFERIDO' | 'PUNTOS';

export interface GenerarCodigoRequest {
  origen: OrigenCodigo;
  clienteId?: number | null;
  descuentoPct?: number | null;
  puntosACanjear?: number | null;
  diasVigencia?: number | null;
}

export interface CodigoGenerado {
  promocion: Promocion;
  mensajeWhatsapp: string;
  celular: string | null;
}

export interface PromocionValida {
  id: number;
  descripcion: string;
  descuentoPct: number | null;
  descuentoMonto: number | null;
  servicioId: number | null;
  cantidadMinima: number;
}

@Injectable({ providedIn: 'root' })
export class PromocionesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/promociones`;

  listar() { return this.http.get<Promocion[]>(this.base); }
  crear(p: Partial<Promocion>) { return this.http.post<Promocion>(this.base, p); }
  actualizar(id: number, p: Partial<Promocion>) { return this.http.put<void>(`${this.base}/${id}`, p); }
  cambiarEstado(id: number, activa: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activa }); }
  eliminar(id: number) { return this.http.delete<void>(`${this.base}/${id}`); }
  /** Generador automático: crea un código único (de un solo uso) y devuelve el mensaje de WhatsApp. */
  generar(req: GenerarCodigoRequest) { return this.http.post<CodigoGenerado>(`${this.base}/generar`, req); }
}
