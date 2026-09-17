import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface InteresadoDemo { id: number; nombre: string; negocio: string; celular: string; email?: string | null; planInteres: string; fechaCreacion: string; }
export interface CrearInteresadoDemo { nombre: string; negocio: string; celular: string; email?: string; planInteres: string; consentimiento: boolean; }

@Injectable({ providedIn: 'root' })
export class InteresadosDemoService {
  private readonly http = inject(HttpClient);
  crear(data: CrearInteresadoDemo) { return this.http.post<{ mensaje: string }>(`${environment.apiUrl}/interesados-demo`, data); }
  listar() { return this.http.get<InteresadoDemo[]>(`${environment.apiUrl}/plataforma/interesados`); }
}
