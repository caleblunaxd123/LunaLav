import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { inject } from '@angular/core';
import { ConfiguracionNegocio } from '../../core/models/models';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { ToastService } from '../../core/services/toast.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-ajustes-puntos',
  imports: [PageHeaderComponent, CommonModule, FormsModule],
  templateUrl: './ajustes-puntos.component.html',
  styleUrl: './ajustes-puntos.component.scss'
})
export class AjustesPuntosComponent implements OnInit {
  private readonly svc = inject(ConfiguracionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  private base: ConfiguracionNegocio = { ...this.svc.configuracion() };
  solesPorPunto = this.base.solesPorPunto;
  valorPuntoCanje = this.base.valorPuntoCanje ?? 0;
  maxDescuentoPct = this.base.maxDescuentoPct ?? 0;
  readonly guardando = signal(false);

  ngOnInit() {
    this.svc.cargar().subscribe({
      next: c => {
        this.base = { ...c };
        this.solesPorPunto = c.solesPorPunto;
        this.valorPuntoCanje = c.valorPuntoCanje ?? 0;
        this.maxDescuentoPct = c.maxDescuentoPct ?? 0;
      },
      error: () => this.toast.error('No se pudo cargar la configuración.')
    });
  }

  guardar() {
    if (this.solesPorPunto <= 0) return;
    if (this.valorPuntoCanje < 0 || this.maxDescuentoPct < 0 || this.maxDescuentoPct > 100) return;
    this.guardando.set(true);
    this.svc.actualizar({
      ...this.base,
      solesPorPunto: this.solesPorPunto,
      valorPuntoCanje: this.valorPuntoCanje,
      maxDescuentoPct: this.maxDescuentoPct
    }).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.exito('Configuración guardada');
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
