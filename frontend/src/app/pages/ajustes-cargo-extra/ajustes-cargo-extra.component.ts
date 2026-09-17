import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfiguracionNegocio } from '../../core/models/models';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { ToastService } from '../../core/services/toast.service';
import { InfoTooltipComponent } from '../../shared/info-tooltip/info-tooltip.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-ajustes-cargo-extra',
  imports: [PageHeaderComponent, CommonModule, FormsModule, InfoTooltipComponent],
  templateUrl: './ajustes-cargo-extra.component.html',
  styleUrl: './ajustes-cargo-extra.component.scss'
})
export class AjustesCargoExtraComponent implements OnInit {
  private readonly svc = inject(ConfiguracionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  private base: ConfiguracionNegocio = { ...this.svc.configuracion() };
  igv = this.base.igv;
  readonly guardando = signal(false);

  ngOnInit() {
    this.svc.cargar().subscribe({
      next: c => { this.base = { ...c }; this.igv = c.igv; },
      error: () => this.toast.error('No se pudo cargar la configuración.')
    });
  }

  guardar() {
    if (this.igv < 0 || this.igv > 100) return;
    this.guardando.set(true);
    this.svc.actualizar({ ...this.base, igv: this.igv }).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.exito('Cargo extra guardado');
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
