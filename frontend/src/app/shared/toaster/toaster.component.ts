import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-toaster',
  imports: [CommonModule],
  templateUrl: './toaster.component.html',
  styleUrl: './toaster.component.scss'
})
export class ToasterComponent {
  private readonly svc = inject(ToastService);
  readonly toasts = this.svc.toasts;
  cerrar(id: number) { this.svc.cerrar(id); }
}
