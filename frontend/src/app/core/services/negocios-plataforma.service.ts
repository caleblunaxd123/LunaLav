import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import {
  ActividadPlataforma, CambiarSuscripcionRequest, ConfiguracionPlataforma, CrearNegocioRequest, EditarNegocioRequest,
  NegocioDetalle, NegocioResumen, PagoSuscripcion, PlataformaResumen, RegistrarPagoSuscripcionRequest
} from '../models/models';

@Injectable({ providedIn: 'root' })
export class NegociosPlataformaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/negocios`;

  listar() { return this.http.get<NegocioResumen[]>(this.base); }
  resumen() { return this.http.get<PlataformaResumen>(`${this.base}/resumen`); }
  actividad(limite = 12) { return this.http.get<ActividadPlataforma[]>(`${this.base}/actividad`, { params: { limite } }); }
  detalle(id: number) { return this.http.get<NegocioDetalle>(`${this.base}/${id}`); }
  crear(req: CrearNegocioRequest) { return this.http.post<NegocioResumen>(this.base, req); }
  editar(id: number, req: EditarNegocioRequest) { return this.http.put<void>(`${this.base}/${id}`, req); }
  cambiarSuscripcion(id: number, req: CambiarSuscripcionRequest) { return this.http.put<void>(`${this.base}/${id}/suscripcion`, req); }
  resetPasswordAdmin(id: number, nuevaPassword: string) { return this.http.post<{ usuario: string }>(`${this.base}/${id}/reset-password-admin`, { nuevaPassword }); }
  resetPasswordUsuario(id: number, usuarioId: number, nuevaPassword: string) { return this.http.post<{ usuario: string }>(`${this.base}/${id}/usuarios/${usuarioId}/reset-password`, { nuevaPassword }); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }

  // ---------- Cobranza ----------
  /** Enlace de pago sin iniciar sesión para el titular; opcionalmente lo envía también por correo. */
  enlacePago(id: number, enviarCorreo = false) {
    return this.http.post<{ url: string; mensaje: string; correoEnviado: boolean; errorCorreo?: string | null }>(
      `${this.base}/${id}/enlace-pago`, {}, { params: { enviarCorreo } });
  }
  registrarPago(id: number, req: RegistrarPagoSuscripcionRequest) { return this.http.post<PagoSuscripcion>(`${this.base}/${id}/pagos`, req); }
  historialPagos(id: number) { return this.http.get<PagoSuscripcion[]>(`${this.base}/${id}/pagos`); }
  obtenerPago(id: number, pagoId: number) { return this.http.get<PagoSuscripcion>(`${this.base}/${id}/pagos/${pagoId}`); }

  configuracionPlataforma() { return this.http.get<ConfiguracionPlataforma>(`${environment.apiUrl}/plataforma/configuracion`); }
  guardarConfiguracionPlataforma(cfg: ConfiguracionPlataforma) { return this.http.put<void>(`${environment.apiUrl}/plataforma/configuracion`, cfg); }
}
