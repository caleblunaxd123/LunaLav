import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { DemoPlan, DemoPreviewService } from '../../core/services/demo-preview.service';

@Component({
  selector: 'app-demo-bienvenida',
  imports: [CommonModule, FormsModule],
  templateUrl: './demo-bienvenida.component.html',
  styleUrl: './demo-bienvenida.component.scss'
})
export class DemoBienvenidaComponent {
  private readonly router = inject(Router);
  private readonly preview = inject(DemoPreviewService);
  paso = signal(1);
  nombre = this.preview.perfil()?.nombre ?? '';
  plan = signal<DemoPlan>(this.preview.perfil()?.plan ?? 'BASICO');
  logoUrl = signal<string | undefined>(this.preview.perfil()?.logoUrl);

  siguiente(): void {
    if (this.paso() === 1 && !this.nombre.trim()) return;
    this.paso.update(p => Math.min(3, p + 1));
  }

  anterior(): void { this.paso.update(p => Math.max(1, p - 1)); }
  elegirPlan(plan: DemoPlan): void { this.plan.set(plan); }

  cargarLogo(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file || !file.type.startsWith('image/')) return;
    const reader = new FileReader();
    reader.onload = () => this.logoUrl.set(typeof reader.result === 'string' ? reader.result : undefined);
    reader.readAsDataURL(file);
  }

  entrar(): void {
    this.preview.guardar({ nombre: this.nombre, plan: this.plan(), logoUrl: this.logoUrl() });
    this.router.navigate(['/login']);
  }

  textoPlan(): string {
    return {
      BASICO: 'Pedidos, clientes, caja y reportes básicos para una sede.',
      FACTURA: 'Todo Básico más el flujo de comprobantes electrónicos en modo demostración.',
      MULTISEDE: 'Todo Factura más selección de sedes y vista consolidada de ejemplo.'
    }[this.plan()];
  }
}
