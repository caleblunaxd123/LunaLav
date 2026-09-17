import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ConfiguracionPlataforma, NegocioDetalle, PagoSuscripcion } from '../../core/models/models';
import { numeroWhatsapp } from '../../core/util/telefono';
import { NegociosPlataformaService } from '../../core/services/negocios-plataforma.service';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-plataforma-negocio-detalle',
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './plataforma-negocio-detalle.component.html',
  styleUrl: './plataforma-negocio-detalle.component.scss'
})
export class PlataformaNegocioDetalleComponent implements OnInit {
  private readonly svc = inject(NegociosPlataformaService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private id = 0;
  readonly negocio = signal<NegocioDetalle | null>(null);
  readonly cargando = signal(true);
  readonly guardando = signal(false);

  // Edición de datos
  readonly editandoDatos = signal(false);
  formDatos = { nombre: '', rucEmpresa: '', titularNombre: '', titularEmail: '', titularCelular: '', notasInternas: '' };

  // Edición de suscripción
  readonly editandoSuscripcion = signal(false);
  formSusc = { planSuscripcion: 'BASICO', estadoSuscripcion: 'ACTIVA', montoMensual: 0, proximoPago: '' };
  readonly planes = ['BASICO', 'PRO', 'PREMIUM'];
  readonly estados = ['PRUEBA', 'ACTIVA', 'VENCIDA', 'SUSPENDIDA'];

  // Reset password (soporte): puede apuntar al admin (por defecto) o a un usuario específico.
  readonly mostrarReset = signal(false);
  readonly resetUsuario = signal<{ id: number; nombre: string; usuario: string } | null>(null);
  passwordNueva = '';
  readonly credencialesReset = signal<{ usuario: string; password: string } | null>(null);

  // Suspender
  readonly confirmarEstado = signal(false);

  // Cobranza
  readonly pagos = signal<PagoSuscripcion[]>([]);
  readonly config = signal<ConfiguracionPlataforma | null>(null);
  readonly mostrarPago = signal(false);
  formPago = { monto: 0, metodo: 'YAPE', meses: 1, nota: '' };
  readonly metodosPago = ['YAPE', 'PLIN', 'TRANSFERENCIA', 'EFECTIVO', 'OTRO'];

  readonly urlAcceso = computed(() => {
    const n = this.negocio();
    return n ? `${window.location.origin}/${n.slug}/login` : '';
  });

  ngOnInit() {
    this.id = Number(this.route.snapshot.paramMap.get('id'));
    this.cargar();
    this.cargarCobranza();
    this.svc.configuracionPlataforma().subscribe({ next: c => this.config.set(c), error: () => {} });
  }

  cargar() {
    this.cargando.set(true);
    this.svc.detalle(this.id).subscribe({
      next: n => { this.negocio.set(n); this.cargando.set(false); },
      error: () => { this.cargando.set(false); this.toast.error('No se pudo cargar la empresa.'); }
    });
  }

  cargarCobranza() {
    this.svc.historialPagos(this.id).subscribe({ next: p => this.pagos.set(p), error: () => {} });
  }

  volver() { this.router.navigate(['/plataforma']); }

  // ---------- Editar datos ----------
  abrirEdicionDatos() {
    const n = this.negocio(); if (!n) return;
    this.formDatos = {
      nombre: n.nombre,
      rucEmpresa: n.rucEmpresa ?? '',
      titularNombre: n.titularNombre ?? '',
      titularEmail: n.titularEmail ?? '',
      titularCelular: n.titularCelular ?? '',
      notasInternas: n.notasInternas ?? ''
    };
    this.editandoDatos.set(true);
  }

  guardarDatos() {
    this.guardando.set(true);
    this.svc.editar(this.id, {
      nombre: this.formDatos.nombre.trim(),
      rucEmpresa: this.formDatos.rucEmpresa.trim() || null,
      titularNombre: this.formDatos.titularNombre.trim() || null,
      titularEmail: this.formDatos.titularEmail.trim() || null,
      titularCelular: this.formDatos.titularCelular.trim() || null,
      notasInternas: this.formDatos.notasInternas.trim() || null
    }).subscribe({
      next: () => { this.guardando.set(false); this.editandoDatos.set(false); this.toast.exito('Datos actualizados'); this.cargar(); },
      error: (err: HttpErrorResponse) => { this.guardando.set(false); this.toast.error(err.error?.mensaje ?? 'No se pudo guardar.'); }
    });
  }

  // ---------- Suscripción ----------
  abrirEdicionSuscripcion() {
    const n = this.negocio(); if (!n) return;
    this.formSusc = {
      planSuscripcion: n.planSuscripcion,
      estadoSuscripcion: n.estadoSuscripcion,
      montoMensual: n.montoMensual,
      proximoPago: n.proximoPago ? n.proximoPago.substring(0, 10) : ''
    };
    this.editandoSuscripcion.set(true);
  }

  guardarSuscripcion() {
    this.guardando.set(true);
    this.svc.cambiarSuscripcion(this.id, {
      planSuscripcion: this.formSusc.planSuscripcion,
      estadoSuscripcion: this.formSusc.estadoSuscripcion,
      montoMensual: Number(this.formSusc.montoMensual) || 0,
      proximoPago: this.formSusc.proximoPago || null
    }).subscribe({
      next: () => { this.guardando.set(false); this.editandoSuscripcion.set(false); this.toast.exito('Suscripción actualizada'); this.cargar(); },
      error: (err: HttpErrorResponse) => { this.guardando.set(false); this.toast.error(err.error?.mensaje ?? 'No se pudo guardar.'); }
    });
  }

  // ---------- Cobranza: registrar pago ----------
  abrirPago() {
    const n = this.negocio(); if (!n) return;
    this.formPago = { monto: n.montoMensual || 0, metodo: 'YAPE', meses: 1, nota: '' };
    this.mostrarPago.set(true);
  }

  cerrarPago() { if (!this.guardando()) this.mostrarPago.set(false); }

  /** Registra el pago: lo deja en el historial y avanza el próximo pago los meses cubiertos. */
  confirmarPago() {
    if (this.guardando()) return;
    const monto = Number(this.formPago.monto) || 0;
    if (monto <= 0) { this.toast.error('El monto debe ser mayor a S/ 0.'); return; }
    const meses = Math.max(1, Math.min(24, Math.floor(Number(this.formPago.meses) || 1)));
    this.guardando.set(true);
    this.svc.registrarPago(this.id, {
      monto, metodo: this.formPago.metodo, meses, nota: this.formPago.nota.trim() || null
    }).subscribe({
      next: pago => {
        this.guardando.set(false);
        this.mostrarPago.set(false);
        this.toast.exito(`Pago de S/ ${monto.toFixed(2)} registrado. Próximo pago +${meses} mes(es).`);
        this.cargar();
        this.cargarCobranza();
        this.abrirRecibo(pago.id);
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.error(err.error?.mensaje ?? 'No se pudo registrar el pago.');
      }
    });
  }

  abrirRecibo(pagoId: number) {
    window.open(`/recibo-suscripcion/${this.id}/${pagoId}`, '_blank');
  }

  /** Abre WhatsApp con un recordatorio de cobro ya redactado para el titular de la empresa. */
  recordarCobro() {
    const n = this.negocio(); if (!n?.titularCelular) return;
    const numero = numeroWhatsapp(n.titularCelular);
    const cfg = this.config();
    const plataforma = cfg?.nombrePlataforma || 'LunaLav';
    const vence = n.proximoPago ? new Date(n.proximoPago).toLocaleDateString('es-PE') : '';
    const saludo = n.titularNombre ? `Hola ${n.titularNombre}` : 'Hola';
    let msg = `${saludo}, te recordamos el pago de tu suscripción a ${plataforma} (${n.nombre}): S/ ${n.montoMensual.toFixed(2)}`;
    if (vence) msg += ` con vencimiento el ${vence}`;
    msg += '.';
    if (cfg?.yapeNumero) msg += ` Puedes pagar por Yape a ${(cfg.yapeNombre ? cfg.yapeNombre + ' ' : '')}${cfg.yapeNumero}.`;
    msg += ' ¡Gracias!';
    window.open(`https://wa.me/${numero}?text=${encodeURIComponent(msg)}`, '_blank');
  }

  /** Abre WhatsApp para dar soporte al titular (saludo genérico, no de cobro). */
  contactarTitular() {
    const n = this.negocio(); if (!n?.titularCelular) return;
    const numero = numeroWhatsapp(n.titularCelular);
    const saludo = n.titularNombre ? `Hola ${n.titularNombre.split(' ')[0]}` : 'Hola';
    const msg = `${saludo}, te escribo del soporte de LunaLav para ayudarte con tu sistema (${n.nombre}). ¿En qué puedo apoyarte?`;
    window.open(`https://wa.me/${numero}?text=${encodeURIComponent(msg)}`, '_blank');
  }

  metodoEtiqueta(m: string): string {
    const map: Record<string, string> = { YAPE: 'Yape', PLIN: 'Plin', TRANSFERENCIA: 'Transferencia', EFECTIVO: 'Efectivo', OTRO: 'Otro' };
    return map[m] ?? m;
  }

  // ---------- Reset password ----------
  generarPassword() {
    // Contraseña legible de 9 chars: 3 sílabas (6) + 3 dígitos. Cumple el mínimo (letras + números, 8+).
    const s = ['la', 've', 'ro', 'mi', 'sa', 'to', 'ni', 'ba', 'lu', 'ca'];
    const azar = (max: number) => {
      const valor = new Uint32Array(1);
      crypto.getRandomValues(valor);
      return valor[0] % max;
    };
    const pick = () => s[azar(s.length)];
    const p = pick() + pick() + pick();
    this.passwordNueva = p.charAt(0).toUpperCase() + p.slice(1) + (100 + azar(900));
  }

  /** Abre el modal de reset apuntando al administrador de la empresa. */
  abrirResetAdmin() { this.resetUsuario.set(null); this.passwordNueva = ''; this.mostrarReset.set(true); }

  /** Abre el modal de reset apuntando a un usuario específico (trabajador, coordinador, etc.). */
  abrirResetUsuario(u: { id: number; nombreCompleto: string; usuario: string }) {
    this.resetUsuario.set({ id: u.id, nombre: u.nombreCompleto, usuario: u.usuario });
    this.passwordNueva = '';
    this.mostrarReset.set(true);
  }

  confirmarReset() {
    if (this.passwordNueva.trim().length < 8) { this.toast.error('La contraseña debe tener al menos 8 caracteres.'); return; }
    const objetivo = this.resetUsuario();
    const clave = this.passwordNueva.trim();
    this.guardando.set(true);
    const peticion = objetivo
      ? this.svc.resetPasswordUsuario(this.id, objetivo.id, clave)
      : this.svc.resetPasswordAdmin(this.id, clave);
    peticion.subscribe({
      next: res => {
        this.guardando.set(false);
        this.credencialesReset.set({ usuario: res.usuario, password: clave });
        this.mostrarReset.set(false);
        this.resetUsuario.set(null);
        this.passwordNueva = '';
        this.toast.exito('Contraseña restablecida');
      },
      error: (err: HttpErrorResponse) => { this.guardando.set(false); this.toast.error(err.error?.mensaje ?? 'No se pudo restablecer.'); }
    });
  }

  copiar(texto: string) {
    navigator.clipboard?.writeText(texto).then(() => this.toast.info('Copiado'), () => {});
  }

  // ---------- Estado empresa ----------
  toggleEstado() {
    const n = this.negocio(); if (!n) return;
    if (n.activo) { this.confirmarEstado.set(true); return; }
    this.aplicarEstado(true);
  }

  confirmarSuspender() { this.aplicarEstado(false); this.confirmarEstado.set(false); }

  private aplicarEstado(activo: boolean) {
    this.svc.cambiarEstado(this.id, activo).subscribe({
      next: () => { this.toast.info(activo ? 'Empresa reactivada' : 'Empresa suspendida'); this.cargar(); },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  // ---------- Helpers de vista ----------
  diasParaVencer(): number | null {
    const n = this.negocio();
    if (!n?.proximoPago) return null;
    const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
    return Math.round((new Date(n.proximoPago).getTime() - hoy.getTime()) / 86_400_000);
  }

  estadoBadge(): { texto: string; clase: string } {
    const n = this.negocio();
    if (!n) return { texto: '', clase: '' };
    if (!n.activo) return { texto: 'Suspendida', clase: 'badge--gris' };
    const dias = this.diasParaVencer();
    if (n.estadoSuscripcion === 'VENCIDA' || (dias !== null && dias < 0)) return { texto: 'Vencida', clase: 'badge--rojo' };
    if (n.estadoSuscripcion === 'PRUEBA') return { texto: 'En prueba', clase: 'badge--azul' };
    if (dias !== null && dias <= 7) return { texto: `Vence en ${dias} días`, clase: 'badge--naranja' };
    return { texto: 'Al día', clase: 'badge--verde' };
  }
}
