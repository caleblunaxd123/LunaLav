import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ConfiguracionPlataforma, NegocioDetalle, PagoSuscripcion } from '../../core/models/models';
import { NegociosPlataformaService } from '../../core/services/negocios-plataforma.service';

/**
 * Recibo imprimible de un pago de suscripción, para que el propietario se lo envíe al cliente.
 * Pantalla completa (sin nav) para imprimir o guardar como PDF desde el navegador.
 */
@Component({
  selector: 'app-plataforma-recibo',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="wrap">
      @if (cargando()) {
        <p class="estado">Cargando recibo…</p>
      } @else if (error()) {
        <p class="estado estado--error">{{ error() }}</p>
      } @else if (pago() && negocio(); as _) {
        <div class="acciones no-print">
          <button class="btn" (click)="imprimir()">Imprimir / Guardar PDF</button>
          <button class="btn btn--ghost" (click)="cerrar()">Cerrar</button>
        </div>

        <div class="recibo" id="recibo">
          <header class="recibo__head">
            <div>
              <div class="marca">{{ cfg()?.nombrePlataforma || 'LunaLav' }}</div>
              <div class="sub">Comprobante de pago de suscripción</div>
            </div>
            <div class="numero">
              <span>RECIBO</span>
              <strong>N° {{ pago()!.id.toString().padStart(5, '0') }}</strong>
            </div>
          </header>

          <div class="fila"><span>Fecha de pago</span><b>{{ pago()!.fecha | date:'dd/MM/yyyy' }}</b></div>
          <div class="fila"><span>Recibí de</span><b>{{ negocio()!.nombre }}</b></div>
          @if (negocio()!.rucEmpresa) { <div class="fila"><span>RUC</span><b>{{ negocio()!.rucEmpresa }}</b></div> }
          @if (negocio()!.titularNombre) { <div class="fila"><span>Titular</span><b>{{ negocio()!.titularNombre }}</b></div> }

          <div class="concepto">
            <span>Concepto</span>
            <b>Suscripción {{ negocio()!.planSuscripcion }} — {{ cfg()?.nombrePlataforma || 'LunaLav' }}</b>
            @if (pago()!.periodoDesde && pago()!.periodoHasta) {
              <small>Período cubierto: {{ pago()!.periodoDesde | date:'dd/MM/yyyy' }} al {{ pago()!.periodoHasta | date:'dd/MM/yyyy' }}</small>
            }
            @if (pago()!.nota) { <small>{{ pago()!.nota }}</small> }
          </div>

          <div class="fila"><span>Método de pago</span><b>{{ metodo() }}</b></div>

          <div class="total">
            <span>Total pagado</span>
            <strong>S/ {{ pago()!.monto | number:'1.2-2' }}</strong>
          </div>

          <footer class="recibo__foot">
            @if (cfg()?.yapeNumero) { <div>Pagos: Yape {{ cfg()?.yapeNombre }} {{ cfg()?.yapeNumero }}</div> }
            @if (cfg()?.contactoSoporte) { <div>Contacto: {{ cfg()?.contactoSoporte }}</div> }
            <div class="gracias">¡Gracias por confiar en {{ cfg()?.nombrePlataforma || 'LunaLav' }}!</div>
          </footer>
        </div>
      }
    </div>
  `,
  styles: [`
    .wrap { max-width: 520px; margin: 0 auto; padding: 24px 16px; }
    .estado { text-align: center; color: #64748b; padding: 40px 0; }
    .estado--error { color: #b91c1c; }
    .acciones { display: flex; gap: 10px; justify-content: flex-end; margin-bottom: 16px; }
    .btn { padding: 9px 16px; border-radius: 8px; border: 1px solid #1e293b; background: #1e293b; color: #fff; cursor: pointer; font-size: 14px; }
    .btn--ghost { background: #fff; color: #1e293b; }
    .recibo { border: 1px solid #e2e8f0; border-radius: 12px; padding: 24px; background: #fff; color: #0f172a; }
    .recibo__head { display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 2px solid #1e293b; padding-bottom: 14px; margin-bottom: 16px; }
    .marca { font-size: 20px; font-weight: 800; }
    .sub { font-size: 12px; color: #64748b; }
    .numero { text-align: right; span { display: block; font-size: 10px; color: #64748b; letter-spacing: 1px; } strong { font-size: 15px; } }
    .fila { display: flex; justify-content: space-between; gap: 12px; padding: 7px 0; font-size: 13.5px; span { color: #64748b; } b { text-align: right; } }
    .concepto { margin: 12px 0; padding: 12px; background: #f8fafc; border-radius: 8px; span { display: block; font-size: 11px; color: #64748b; } b { display: block; font-size: 14px; margin-top: 2px; } small { display: block; color: #64748b; font-size: 11.5px; margin-top: 4px; } }
    .total { display: flex; justify-content: space-between; align-items: center; margin-top: 14px; padding-top: 14px; border-top: 2px dashed #cbd5e1; span { font-weight: 600; } strong { font-size: 26px; color: #047857; } }
    .recibo__foot { margin-top: 20px; padding-top: 14px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #64748b; div { margin-bottom: 3px; } .gracias { margin-top: 8px; font-weight: 600; color: #1e293b; } }
    @media print {
      .no-print { display: none !important; }
      .wrap { padding: 0; max-width: none; }
      .recibo { border: none; border-radius: 0; padding: 0; }
    }
  `]
})
export class PlataformaReciboComponent implements OnInit {
  private readonly svc = inject(NegociosPlataformaService);
  private readonly route = inject(ActivatedRoute);

  readonly negocio = signal<NegocioDetalle | null>(null);
  readonly pago = signal<PagoSuscripcion | null>(null);
  readonly cfg = signal<ConfiguracionPlataforma | null>(null);
  readonly cargando = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit() {
    const negocioId = Number(this.route.snapshot.paramMap.get('negocioId'));
    const pagoId = Number(this.route.snapshot.paramMap.get('pagoId'));
    forkJoin({
      negocio: this.svc.detalle(negocioId),
      pago: this.svc.obtenerPago(negocioId, pagoId),
      cfg: this.svc.configuracionPlataforma()
    }).subscribe({
      next: ({ negocio, pago, cfg }) => {
        this.negocio.set(negocio);
        this.pago.set(pago);
        this.cfg.set(cfg);
        this.cargando.set(false);
      },
      error: () => { this.cargando.set(false); this.error.set('No se pudo cargar el recibo.'); }
    });
  }

  metodo(): string {
    const m = this.pago()?.metodo ?? '';
    const map: Record<string, string> = { YAPE: 'Yape', PLIN: 'Plin', TRANSFERENCIA: 'Transferencia', EFECTIVO: 'Efectivo', OTRO: 'Otro' };
    return map[m] ?? m;
  }

  imprimir() { window.print(); }
  cerrar() { window.close(); }
}
