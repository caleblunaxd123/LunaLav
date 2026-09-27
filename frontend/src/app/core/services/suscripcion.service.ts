import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MiSuscripcion } from '../models/models';

export interface PagoSuscripcionResumen {
  id: number;
  fecha: string;
  monto: number;
  metodo: string;
  periodoDesde?: string | null;
  periodoHasta?: string | null;
}

/** Estado de la suscripción y del pago automático con tarjeta (GET /api/suscripcion/pago, solo ADMIN). */
export interface EstadoPagoSuscripcion {
  configurado: boolean;
  publicKey: string | null;
  modo: 'TEST' | 'LIVE' | string;
  empresa: string;
  plan: string;
  montoMensual: number;
  estadoSuscripcion: string;
  proximoPago?: string | null;
  pagoAutomatico: { estado: 'SIN_TARJETA' | 'ACTIVA' | 'CANCELADA' | 'FALLIDA' | string; tarjeta?: string | null; ultimoError?: string | null };
  titular: { nombre?: string | null; email?: string | null; celular?: string | null };
  pagos: PagoSuscripcionResumen[];
}

export interface Parametros3DS {
  eci: string; xid: string; cavv: string; protocolVersion: string; directoryServerTransactionId: string;
}

export interface ActivarPagoRequest {
  tokenId: string;
  nombre: string;
  apellido: string;
  email: string;
  telefono: string;
  direccion: string;
  ciudad: string;
  aceptaCobroRecurrente: boolean;
  parametros3DS?: Parametros3DS | null;
}

/** Suscripción de la propia empresa. Backend: SuscripcionController y SuscripcionPagoController. */
@Injectable({ providedIn: 'root' })
export class SuscripcionService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/suscripcion`;

  /** Aviso de vencimiento que ve cualquier usuario autenticado del tenant en su dashboard. */
  mia(): Observable<MiSuscripcion> {
    return this.http.get<MiSuscripcion>(`${this.base}/mia`);
  }

  estadoPago(): Observable<EstadoPagoSuscripcion> {
    return this.http.get<EstadoPagoSuscripcion>(`${this.base}/pago`);
  }

  activarPago(req: ActivarPagoRequest): Observable<{ activado: boolean; requiere3DS: boolean; pagosRegistrados: number }> {
    return this.http.post<{ activado: boolean; requiere3DS: boolean; pagosRegistrados: number }>(`${this.base}/pago/activar`, req);
  }

  cancelarPago(): Observable<{ cancelado: boolean }> {
    return this.http.post<{ cancelado: boolean }>(`${this.base}/pago/cancelar`, {});
  }

  /** Página de pago abierta desde el enlace del correo o WhatsApp (sin iniciar sesión). */
  estadoEnlace(token: string): Observable<EstadoPagoSuscripcion> {
    return this.http.get<EstadoPagoSuscripcion>(`${environment.apiUrl}/pago-suscripcion/${encodeURIComponent(token)}`);
  }

  activarEnlace(token: string, req: ActivarPagoRequest): Observable<{ activado: boolean; requiere3DS: boolean; pagosRegistrados: number }> {
    return this.http.post<{ activado: boolean; requiere3DS: boolean; pagosRegistrados: number }>(
      `${environment.apiUrl}/pago-suscripcion/${encodeURIComponent(token)}/activar`, req);
  }

  sincronizar(): Observable<{ pagosRegistrados: number }> {
    return this.http.post<{ pagosRegistrados: number }>(`${this.base}/pago/sincronizar`, {});
  }
}
