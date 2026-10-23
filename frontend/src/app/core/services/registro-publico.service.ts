import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of } from 'rxjs';
import { environment } from '../../../environments/environment';

export type PlanComercial = 'BASICO' | 'FACTURA' | 'MULTISEDE';

export interface RegistrarPruebaRequest {
  nombreNegocio: string;
  slug: string;
  nombreResponsable: string;
  email: string;
  celular: string;
  usuario: string;
  password: string;
  plan: PlanComercial;
  sedeNombre: string;
  aceptaTerminos: boolean;
  codigoVerificacion: string;
}

export interface RegistrarPruebaResponse {
  negocioId: number;
  slug: string;
  pruebaHasta: string;
  diasPrueba: number;
}

export type DisponibilidadSlug =
  | { estado: 'disponible' }
  | { estado: 'ocupado'; mensaje: string }
  | { estado: 'desconocido' };

/** Alta autónoma de una lavandería desde la web (POST /api/registro/*). */
@Injectable({ providedIn: 'root' })
export class RegistroPublicoService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/registro`;

  solicitarCodigo(email: string): Observable<{ enviado: boolean; minutosVigencia: number }> {
    return this.http.post<{ enviado: boolean; minutosVigencia: number }>(`${this.base}/codigo`, { email });
  }

  /** Nunca falla: si la consulta no responde, el alta valida el código al final. */
  disponible(slug: string): Observable<DisponibilidadSlug> {
    return this.http.get<{ disponible: boolean; mensaje?: string | null }>(`${this.base}/disponible`, { params: { slug } }).pipe(
      map((r): DisponibilidadSlug => r.disponible
        ? { estado: 'disponible' }
        : { estado: 'ocupado', mensaje: r.mensaje ?? 'Ese código ya está en uso.' }),
      catchError(() => of<DisponibilidadSlug>({ estado: 'desconocido' }))
    );
  }

  registrar(req: RegistrarPruebaRequest): Observable<RegistrarPruebaResponse> {
    return this.http.post<RegistrarPruebaResponse>(`${this.base}/prueba`, req);
  }
}

const PLANES: readonly PlanComercial[] = ['BASICO', 'FACTURA', 'MULTISEDE'];

export function planDesdeQuery(valor: string | null | undefined): PlanComercial {
  const v = (valor ?? '').toUpperCase();
  return (PLANES as readonly string[]).includes(v) ? v as PlanComercial : 'BASICO';
}

/** "Lavandería Doña Memo" -> "lavanderia-dona-memo" (mismo formato que valida el backend). */
export function sugerirSlug(nombre: string): string {
  return nombre
    .normalize('NFD').replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 50)
    .replace(/-+$/g, '');
}
