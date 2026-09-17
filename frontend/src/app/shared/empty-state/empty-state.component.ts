import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  imports: [CommonModule],
  templateUrl: './empty-state.component.html',
  styleUrl: './empty-state.component.scss'
})
export class EmptyStateComponent {
  @Input() icono: 'busqueda' | 'lista' | 'error' | 'ok' | 'inbox' | 'config' = 'lista';
  @Input() titulo = '';
  @Input() mensaje = '';
  @Input() accionTexto?: string;
}
