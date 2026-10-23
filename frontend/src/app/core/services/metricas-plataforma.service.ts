import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface EtapaEmbudo { codigo: string; total: number; }
export interface NegocioMetrica {
  slug: string; fechaCreacion: string; plan: string; estado: string;
  diasRestantesPrueba: number | null; primerLogin: boolean;
  pedidosTotales: number; ultimoPedidoFecha: string | null; motivoAbandono: string;
}
export interface MetricasPlataforma {
  embudo: EtapaEmbudo[]; excluidos: number; registrosConfirmadosAutonomos: number;
  suscripcionesTest: number; negociosReales: number; onboardingCompleto: number;
  primerPedido: number; activosEnPrueba: number; pagando: number; mrr: number;
  conversionPruebaPago: number; negocios: NegocioMetrica[];
  negociosExcluidos: { slug: string; motivo: string }[];
}

@Injectable({ providedIn: 'root' })
export class MetricasPlataformaService {
  private readonly http = inject(HttpClient);
  obtener() { return this.http.get<MetricasPlataforma>(`${environment.apiUrl}/plataforma/metricas`); }
}
