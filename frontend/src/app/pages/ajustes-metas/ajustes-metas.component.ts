import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfiguracionNegocio } from '../../core/models/models';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { ToastService } from '../../core/services/toast.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-ajustes-metas',
  imports: [PageHeaderComponent, CommonModule, FormsModule],
  templateUrl: './ajustes-metas.component.html',
  styleUrl: './ajustes-metas.component.scss'
})
export class AjustesMetasComponent implements OnInit {
  private readonly svc = inject(ConfiguracionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  private base: ConfiguracionNegocio = { ...this.svc.configuracion() };
  metaMensual = this.base.metaMensual;
  readonly guardando = signal(false);

  ngOnInit() {
    this.svc.cargar().subscribe({
      next: c => { this.base = { ...c }; this.metaMensual = c.metaMensual; },
      error: () => this.toast.error('No se pudo cargar la configuración.')
    });
  }

  guardar() {
    if (this.metaMensual < 0) return;
    this.guardando.set(true);
    this.svc.actualizar({ ...this.base, metaMensual: this.metaMensual }).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.exito('Meta mensual guardada');
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
