import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfiguracionPlataforma } from '../../core/models/models';
import { NegociosPlataformaService } from '../../core/services/negocios-plataforma.service';
import { ToastService } from '../../core/services/toast.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

/** Configuración del dueño del SaaS: datos de cobro (Yape) y contacto, usados en recordatorios y recibos. */
@Component({
  selector: 'app-plataforma-ajustes',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header icono="bank" color="gris" titulo="Ajustes de la plataforma"
        subtitulo="Tus datos de cobro y contacto. Aparecen en los recordatorios de pago y en los recibos."
        volverTexto="Panel" (volver)="volver()"></app-page-header>

      @if (cargando()) {
        <div class="estado">Cargando…</div>
      } @else {
        <div class="card form">
          <label>Nombre de la plataforma</label>
          <input type="text" maxlength="100" [(ngModel)]="form.nombrePlataforma" placeholder="Ej: LunaLav" />

          <div class="row-2">
            <div>
              <label>Nombre para el Yape</label>
              <input type="text" maxlength="100" [(ngModel)]="form.yapeNombre" placeholder="Ej: Mekias L." />
            </div>
            <div>
              <label>Número de Yape / Plin</label>
              <input type="tel" maxlength="20" [(ngModel)]="form.yapeNumero" placeholder="Ej: 999888777" />
            </div>
          </div>

          <label>Contacto de soporte <small>(opcional)</small></label>
          <input type="text" maxlength="100" [(ngModel)]="form.contactoSoporte" placeholder="Celular o correo que ve el cliente" />

          <label>Días de aviso antes del vencimiento</label>
          <input type="number" min="0" max="60" step="1" [(ngModel)]="form.diasAvisoCobro" />
          <p class="hint">Sirve como referencia para cuándo recordarle el cobro a cada empresa.</p>

          <div class="acciones">
            <button class="btn btn--primario" (click)="guardar()" [disabled]="guardando()">
              {{ guardando() ? 'Guardando…' : 'Guardar cambios' }}
            </button>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .page { padding: 20px; max-width: 720px; }
    .estado { padding: 30px; text-align: center; color: #64748b; }
    .card { background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 22px; }
    label { display: block; font-size: 12.5px; font-weight: 600; color: #475569; margin: 14px 0 5px; }
    label:first-child { margin-top: 0; }
    label small { font-weight: 400; color: #94a3b8; }
    input { width: 100%; padding: 10px 12px; border: 1px solid #cbd5e1; border-radius: 8px; font-size: 14px; box-sizing: border-box; &:focus { outline: none; border-color: #1e293b; } }
    .row-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
    .hint { font-size: 12px; color: #64748b; margin: 6px 0 0; }
    .acciones { display: flex; justify-content: flex-end; margin-top: 22px; }
    @media (max-width: 560px) { .row-2 { grid-template-columns: 1fr; } }
  `]
})
export class PlataformaAjustesComponent implements OnInit {
  private readonly svc = inject(NegociosPlataformaService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly cargando = signal(true);
  readonly guardando = signal(false);
  form: ConfiguracionPlataforma = { nombrePlataforma: 'LunaLav', yapeNombre: '', yapeNumero: '', contactoSoporte: '', diasAvisoCobro: 3 };

  ngOnInit() {
    this.svc.configuracionPlataforma().subscribe({
      next: c => { this.form = { ...c, yapeNombre: c.yapeNombre ?? '', yapeNumero: c.yapeNumero ?? '', contactoSoporte: c.contactoSoporte ?? '' }; this.cargando.set(false); },
      error: () => { this.cargando.set(false); this.toast.error('No se pudo cargar la configuración.'); }
    });
  }

  guardar() {
    if (this.guardando()) return;
    if (!this.form.nombrePlataforma?.trim()) { this.toast.error('Indica el nombre de la plataforma.'); return; }
    this.guardando.set(true);
    this.svc.guardarConfiguracionPlataforma({
      nombrePlataforma: this.form.nombrePlataforma.trim(),
      yapeNombre: this.form.yapeNombre?.trim() || null,
      yapeNumero: this.form.yapeNumero?.trim() || null,
      contactoSoporte: this.form.contactoSoporte?.trim() || null,
      diasAvisoCobro: Math.max(0, Math.min(60, Math.floor(Number(this.form.diasAvisoCobro) || 0)))
    }).subscribe({
      next: () => { this.guardando.set(false); this.toast.exito('Configuración guardada'); },
      error: (err: HttpErrorResponse) => { this.guardando.set(false); this.toast.error(err.error?.mensaje ?? 'No se pudo guardar.'); }
    });
  }

  volver() { this.router.navigate(['/plataforma']); }
}
