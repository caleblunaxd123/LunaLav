import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MetricasPlataforma, MetricasPlataformaService } from '../../core/services/metricas-plataforma.service';

const ETIQUETAS: Record<string, string> = {
  inicio_registro: 'Inicio de registro', registro_confirmado: 'Registro confirmado',
  primer_login: 'Primer ingreso', onboarding_completo: 'Configuración inicial',
  primer_pedido: 'Primer pedido', activos_en_prueba: 'Activos en prueba',
  suscripcion_activada: 'Suscripción LIVE', primer_pago: 'Primer pago'
};
const MOTIVOS: Record<string, string> = {
  no_login: 'Sin ingreso', sin_pedidos: 'Sin pedidos', inactivo_7d: 'Inactivo 7 días',
  prueba_vencida_sin_pago: 'Prueba vencida sin pago', convertido: 'Convertido', activo: 'Activo'
};

@Component({
  selector: 'app-plataforma-metricas',
  imports: [CommonModule],
  templateUrl: './plataforma-metricas.component.html',
  styleUrl: './plataforma-metricas.component.scss'
})
export class PlataformaMetricasComponent implements OnInit {
  private readonly servicio = inject(MetricasPlataformaService);
  readonly datos = signal<MetricasPlataforma | null>(null);
  readonly cargando = signal(false);
  readonly error = signal(false);
  readonly maximo = computed(() => Math.max(1, ...(this.datos()?.embudo.map(e => e.total) ?? [])));
  readonly etapas = computed(() => this.datos()?.embudo.map(e => ({
    ...e, etiqueta: ETIQUETAS[e.codigo] ?? e.codigo, ancho: 100 * e.total / this.maximo()
  })) ?? []);
  readonly kpis = computed(() => {
    const d = this.datos();
    return d ? [
      { etiqueta: 'Negocios reales', valor: d.negociosReales },
      { etiqueta: 'Configuración inicial', valor: d.onboardingCompleto },
      { etiqueta: 'Primer pedido', valor: d.primerPedido },
      { etiqueta: 'Activos en prueba', valor: d.activosEnPrueba },
      { etiqueta: 'Pagando', valor: d.pagando }
    ] : [];
  });

  ngOnInit() { this.cargar(); }
  cargar() {
    this.cargando.set(true);
    this.error.set(false);
    this.servicio.obtener().subscribe({
      next: d => { this.datos.set(d); this.cargando.set(false); },
      error: () => { this.error.set(true); this.cargando.set(false); }
    });
  }
  motivo(codigo: string) { return MOTIVOS[codigo] ?? codigo; }
  motivoExclusion(codigo: string) {
    return ({ inactivo: 'Negocio inactivo', demo: 'Demo', plataforma_interna: 'Plataforma interna',
      qa: 'QA', test: 'Prueba', email_ejemplo: 'Correo de ejemplo', configurado: 'Exclusión configurada'
    } as Record<string, string>)[codigo] ?? codigo;
  }
}
