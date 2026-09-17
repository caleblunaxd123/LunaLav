import { CommonModule } from '@angular/common';
import { Component, Input, signal } from '@angular/core';
import { IconComponent } from '../icon/icon.component';

/**
 * Icono ℹ️ que muestra una explicación en lenguaje simple al tocarlo/pasar el mouse.
 * Pensado para jargon tecnico (IGV, OSE, credenciales SOL, etc.) en pantallas de Ajustes
 * usadas por dueños de negocio sin formación técnica.
 */
@Component({
  selector: 'app-info-tooltip',
  imports: [CommonModule, IconComponent],
  templateUrl: './info-tooltip.component.html',
  styleUrl: './info-tooltip.component.scss'
})
export class InfoTooltipComponent {
  @Input() texto = '';

  readonly abierto = signal(false);

  toggle() { this.abierto.update(v => !v); }
  cerrar() { this.abierto.set(false); }
}
