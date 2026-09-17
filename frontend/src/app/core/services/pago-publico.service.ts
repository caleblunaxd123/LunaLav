import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface PasoSeguimiento {
  codigo: string;
  nombre: string;
  alcanzado: boolean;
  actual: boolean;
}

export interface SeguimientoPedido {
  nombreNegocio: string;
  logoUrl?: string | null;
  colorPrimario: string;
  telefonoNegocio?: string | null;
  direccionNegocio?: string | null;
  numeroPedido: number;
  modalidad: string;
  direccionEntrega?: string | null;
  distritoEntrega?: string | null;
  referenciaEntrega?: string | null;
  latitudEntrega?: number | null;
  longitudEntrega?: number | null;
  resumenEstado: string;
  fechaCompromiso?: string | null;
  etiquetaFechaCompromiso: string;
  pasos: PasoSeguimiento[];
  items: Array<{ nombre: string; cantidad: number }>;
  anulado: boolean;
  total: number;
  montoPagado: number;
  saldo: number;
  requierePago: boolean;
  proveedorPagos: string;
  mensajePagoOnline?: string | null;
  motorizadoNombre?: string | null;
  motorizadoCelular?: string | null;
  puedeReprogramar: boolean;
  // Seguimiento en vivo del reparto (tipo Uber)
  estadoRuta: EstadoRuta;
  rutaIniciadaEn?: string | null;
  motorizadoLat?: number | null;
  motorizadoLng?: number | null;
  motorizadoUbicadoEn?: string | null;
  distanciaMetros?: number | null;
  etaMinutos?: number | null;
  // Fotos de evidencia visibles para el cliente
  fotos?: SeguimientoFoto[];
}

export interface SeguimientoFoto {
  id: number;
  momento: string;
  fecha: string;
}

export type EstadoRuta = 'SIN_RUTA' | 'EN_RUTA' | 'CERCA' | 'LLEGO' | 'ENTREGADO';

/** Consumido por la página pública de seguimiento/pago — sin sesión de empleado. */
@Injectable({ providedIn: 'root' })
export class PagoPublicoService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/pago-publico`;

  obtener(token: string) {
    return this.http.get<SeguimientoPedido>(`${this.base}/${token}`);
  }

  reprogramar(token: string, nuevaFecha: string) {
    return this.http.post<void>(`${this.base}/${token}/reprogramar`, { nuevaFecha });
  }
}
