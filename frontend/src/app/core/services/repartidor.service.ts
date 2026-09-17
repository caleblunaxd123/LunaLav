import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { EstadoRuta } from './pago-publico.service';

export interface RepartidorPedido {
  nombreNegocio: string;
  colorPrimario: string;
  numeroPedido: number;
  clienteNombre: string;
  clienteCelular?: string | null;
  direccionEntrega?: string | null;
  distritoEntrega?: string | null;
  referenciaEntrega?: string | null;
  latitudEntrega?: number | null;
  longitudEntrega?: number | null;
  saldo: number;
  anulado: boolean;
  entregado: boolean;
  estadoRuta: EstadoRuta;
  rutaIniciadaEn?: string | null;
  // Cobro por Yape/Plin del negocio: el repartidor muestra el QR al cliente en la puerta.
  yapeNumero?: string | null;
  yapeTitular?: string | null;
  yapeQrUrl?: string | null;
}

export interface UbicacionResultado {
  estadoRuta: EstadoRuta;
  distanciaMetros?: number | null;
  etaMinutos?: number | null;
}

/** Pantalla pública que el repartidor abre en su celular (sin sesión). */
@Injectable({ providedIn: 'root' })
export class RepartidorService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/repartidor`;

  obtener(token: string) {
    return this.http.get<RepartidorPedido>(`${this.base}/${token}`);
  }

  iniciarRuta(token: string) {
    return this.http.post<void>(`${this.base}/${token}/iniciar-ruta`, {});
  }

  enviarUbicacion(token: string, lat: number, lng: number) {
    return this.http.post<UbicacionResultado>(`${this.base}/${token}/ubicacion`, { lat, lng });
  }

  marcarEntregado(token: string) {
    return this.http.post<void>(`${this.base}/${token}/entregado`, {});
  }
}
