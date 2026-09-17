import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AlertaGlobal, AlertasGlobalesService } from '../../core/services/alertas-globales.service';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'app-alertas-globales',
  imports: [CommonModule, RouterLink, IconComponent],
  templateUrl: './alertas-globales.component.html',
  styleUrl: './alertas-globales.component.scss'
})
export class AlertasGlobalesComponent implements OnInit, OnDestroy {
  readonly servicio = inject(AlertasGlobalesService);
  private readonly router = inject(Router);
  readonly expandido = signal(false);
  readonly limite = 3;
  readonly visibles = computed(() => this.expandido()
    ? this.servicio.alertas()
    : this.servicio.alertas().slice(0, this.limite));
  readonly restantes = computed(() => Math.max(0, this.servicio.alertas().length - this.limite));

  ngOnInit(): void { this.servicio.iniciar(); }
  ngOnDestroy(): void { this.servicio.detener(); }

  descartar(alerta: AlertaGlobal): void {
    this.servicio.descartar(alerta);
    if (this.servicio.alertas().length <= this.limite) this.expandido.set(false);
  }

  toggleExpandido(): void { this.expandido.update(valor => !valor); }

  /** Navega al destino de la alerta al hacer click en cualquier parte de la franja. */
  irAAlerta(alerta: AlertaGlobal): void {
    if (!alerta.ruta) return;
    this.router.navigate([alerta.ruta], { queryParams: alerta.queryParams ?? undefined });
  }
}
