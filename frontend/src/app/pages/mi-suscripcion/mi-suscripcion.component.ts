import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, NgZone, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { EstadoPagoSuscripcion, Parametros3DS, SuscripcionService } from '../../core/services/suscripcion.service';
import { ToastService } from '../../core/services/toast.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

/* Globales que exponen los scripts oficiales de Culqi (checkout v4 y Culqi 3DS). */
declare global {
  interface Window {
    Culqi?: any;
    Culqi3DS?: any;
    culqi?: () => void;
  }
}

const CHECKOUT_JS = 'https://checkout.culqi.com/js/v4';
const CULQI_3DS_JS = 'https://3ds.culqi.com';

const PLANES: Record<string, string> = { BASICO: 'Básico', PRO: 'Factura', PREMIUM: 'Multisede', FACTURA: 'Factura', MULTISEDE: 'Multisede' };

/** Carga un script externo una sola vez. */
function cargarScript(src: string): Promise<void> {
  const existente = document.querySelector<HTMLScriptElement>(`script[src="${src}"]`);
  if (existente?.dataset['cargado'] === '1') return Promise.resolve();
  return new Promise((resolve, reject) => {
    const s = existente ?? document.createElement('script');
    s.addEventListener('load', () => { s.dataset['cargado'] = '1'; resolve(); }, { once: true });
    s.addEventListener('error', () => reject(new Error(`No se pudo cargar ${src}`)), { once: true });
    if (!existente) { s.src = src; s.async = true; document.head.appendChild(s); }
  });
}

/**
 * Mi suscripción: estado del plan y activación del cobro mensual automático con tarjeta (Culqi).
 * La tarjeta la tokeniza el checkout de Culqi en el navegador: LunaLav nunca ve el número.
 */
@Component({
  selector: 'app-mi-suscripcion',
  imports: [CommonModule, FormsModule, PageHeaderComponent],
  templateUrl: './mi-suscripcion.component.html',
  styleUrl: './mi-suscripcion.component.scss'
})
export class MiSuscripcionComponent implements OnInit, OnDestroy {
  private readonly svc = inject(SuscripcionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);

  readonly estado = signal<EstadoPagoSuscripcion | null>(null);
  readonly cargando = signal(true);
  readonly procesando = signal(false);
  readonly error = signal('');

  // Datos del titular de la tarjeta (Culqi los exige para registrar al cliente).
  nombre = '';
  apellido = '';
  email = '';
  telefono = '';
  direccion = '';
  ciudad = 'Lima';
  acepta = false;

  private tokenPendiente: { id: string; email: string } | null = null;
  private readonly onMessage = (event: MessageEvent) => this.recibir3DS(event);

  readonly planNombre = computed(() => PLANES[this.estado()?.plan ?? ''] ?? this.estado()?.plan ?? '');
  readonly diasRestantes = computed(() => {
    const p = this.estado()?.proximoPago;
    if (!p) return null;
    const [y, m, d] = p.slice(0, 10).split('-').map(Number);
    const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
    return Math.round((new Date(y, m - 1, d).getTime() - hoy.getTime()) / 86_400_000);
  });
  readonly autoActivo = computed(() => this.estado()?.pagoAutomatico.estado === 'ACTIVA');

  ngOnInit() {
    window.addEventListener('message', this.onMessage);
    this.cargar();
  }

  ngOnDestroy() {
    window.removeEventListener('message', this.onMessage);
    if (window.culqi) window.culqi = undefined;
  }

  volver() { this.router.navigate(['/ajustes']); }

  cargar() {
    this.cargando.set(true);
    this.svc.estadoPago().subscribe({
      next: e => {
        this.estado.set(e);
        const [nom, ...ape] = (e.titular.nombre ?? '').trim().split(/\s+/);
        this.nombre ||= nom ?? '';
        this.apellido ||= ape.join(' ');
        this.email ||= e.titular.email ?? '';
        this.telefono ||= e.titular.celular ?? '';
        this.cargando.set(false);
      },
      error: () => { this.cargando.set(false); this.toast.error('No se pudo cargar tu suscripción.'); }
    });
  }

  validar(): string {
    if (this.nombre.trim().length < 2 || this.apellido.trim().length < 2) return 'Escribe el nombre y apellido del titular de la tarjeta.';
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(this.email.trim())) return 'Escribe un correo válido.';
    if (this.telefono.replace(/\D/g, '').length < 6) return 'Escribe un celular válido.';
    if (this.direccion.trim().length < 5) return 'Escribe la dirección de facturación.';
    if (this.ciudad.trim().length < 2) return 'Escribe la ciudad.';
    if (!this.acepta) return 'Debes aceptar el cobro mensual automático.';
    return '';
  }

  /** Abre el formulario seguro de Culqi para ingresar la tarjeta. */
  async agregarTarjeta() {
    const e = this.estado();
    const falta = this.validar();
    this.error.set(falta);
    if (falta || !e?.publicKey) return;
    this.procesando.set(true);
    try {
      await cargarScript(CHECKOUT_JS);
      const Culqi = window.Culqi;
      Culqi.publicKey = e.publicKey;
      Culqi.settings({ title: 'LunaLav', currency: 'PEN', amount: Math.round(e.montoMensual * 100) });
      Culqi.options({
        lang: 'auto',
        installments: false,
        paymentMethods: { tarjeta: true, yape: false, billetera: false, bancaMovil: false, agente: false, cuotealo: false },
        style: { bannerColor: '#00245E', buttonBackground: '#0074CC', buttonText: 'Guardar tarjeta', priceColor: '#00245E' }
      });
      window.culqi = () => this.zone.run(() => this.tokenRecibido());
      Culqi.open();
    } catch {
      this.error.set('No se pudo abrir el formulario de pago. Revisa tu conexión e inténtalo de nuevo.');
    } finally {
      this.procesando.set(false);
    }
  }

  private tokenRecibido() {
    const Culqi = window.Culqi;
    if (Culqi?.token) {
      const token = { id: Culqi.token.id as string, email: (Culqi.token.email as string) || this.email.trim() };
      Culqi.close?.();
      this.tokenPendiente = token;
      this.enviar(token, null);
    } else if (Culqi?.error) {
      this.error.set(Culqi.error.user_message || 'La tarjeta no pudo validarse.');
    }
  }

  private enviar(token: { id: string; email: string }, tds: Parametros3DS | null) {
    this.procesando.set(true);
    this.error.set('');
    this.svc.activarPago({
      tokenId: token.id, nombre: this.nombre.trim(), apellido: this.apellido.trim(), email: this.email.trim(),
      telefono: this.telefono.trim(), direccion: this.direccion.trim(), ciudad: this.ciudad.trim(),
      aceptaCobroRecurrente: this.acepta, parametros3DS: tds
    }).subscribe({
      next: r => {
        if (r.requiere3DS) { void this.iniciar3DS(token); return; }
        this.procesando.set(false);
        this.tokenPendiente = null;
        this.toast.exito(r.pagosRegistrados > 0 ? '¡Pago realizado! Tu suscripción está al día.' : 'Pago automático activado.');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.error.set(err.error?.mensaje ?? 'No se pudo activar el pago. Inténtalo nuevamente.');
      }
    });
  }

  /** El banco pide autenticar la tarjeta (3D Secure): lo hace la librería de Culqi en un modal. */
  private async iniciar3DS(token: { id: string; email: string }) {
    try {
      await cargarScript(CULQI_3DS_JS);
      const tds = window.Culqi3DS;
      tds.publicKey = this.estado()!.publicKey;
      tds.options = { showModal: true, showLoading: true, showIcon: true, closeModalAction: () => this.zone.run(() => this.procesando.set(false)) };
      tds.settings = { charge: { totalAmount: Math.round(this.estado()!.montoMensual * 100), returnUrl: window.location.href }, card: { email: token.email } };
      tds.initAuthentication(token.id);
    } catch {
      this.procesando.set(false);
      this.error.set('Tu banco pidió verificar la tarjeta y no pudimos iniciar la verificación. Inténtalo de nuevo.');
    }
  }

  private recibir3DS(event: MessageEvent) {
    if (event.origin !== window.location.origin || !this.tokenPendiente) return;
    const { parameters3DS, error } = (event.data ?? {}) as { parameters3DS?: Parametros3DS; error?: unknown };
    this.zone.run(() => {
      if (parameters3DS) { window.Culqi3DS?.reset?.(); this.enviar(this.tokenPendiente!, parameters3DS); }
      else if (error) { this.procesando.set(false); this.error.set('No se pudo verificar la tarjeta con tu banco.'); }
    });
  }

  desactivar() {
    if (!confirm('¿Desactivar el cobro automático? Tu suscripción seguirá activa hasta la fecha ya pagada.')) return;
    this.procesando.set(true);
    this.svc.cancelarPago().subscribe({
      next: () => { this.procesando.set(false); this.toast.exito('Cobro automático desactivado.'); this.cargar(); },
      error: (err: HttpErrorResponse) => { this.procesando.set(false); this.toast.error(err.error?.mensaje ?? 'No se pudo desactivar.'); }
    });
  }

  sincronizar() {
    this.procesando.set(true);
    this.svc.sincronizar().subscribe({
      next: r => { this.procesando.set(false); if (r.pagosRegistrados) this.toast.exito('Pago registrado.'); this.cargar(); },
      error: () => { this.procesando.set(false); this.cargar(); }
    });
  }
}
