import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ActividadPlataforma, NegocioResumen, PlataformaResumen } from '../../core/models/models';
import { NegociosPlataformaService } from '../../core/services/negocios-plataforma.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

type FiltroEstado = 'todas' | 'activas' | 'prueba' | 'por-cobrar' | 'suspendidas';

/** Cada cuánto se refresca el panel solo (altas nuevas, pagos con tarjeta, vencimientos). */
const REFRESCO_MS = 60_000;

@Component({
  selector: 'app-plataforma-negocios',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, IconComponent],
  templateUrl: './plataforma-negocios.component.html',
  styleUrl: './plataforma-negocios.component.scss'
})
export class PlataformaNegociosComponent implements OnInit {
  private readonly svc = inject(NegociosPlataformaService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly negocios = signal<NegocioResumen[]>([]);
  readonly resumen = signal<PlataformaResumen | null>(null);
  readonly actividad = signal<ActividadPlataforma[]>([]);
  readonly actualizado = signal<Date | null>(null);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly busqueda = signal('');
  readonly filtro = signal<FiltroEstado>('todas');

  readonly confirmarDesactivar = signal<NegocioResumen | null>(null);

  readonly negociosFiltrados = computed(() => {
    const texto = this.busqueda().trim().toLowerCase();
    const f = this.filtro();
    return this.negocios().filter(n => {
      if (texto && !n.nombre.toLowerCase().includes(texto) && !n.slug.toLowerCase().includes(texto)) return false;
      if (f === 'activas') return n.activo;
      if (f === 'prueba') return n.activo && n.estadoSuscripcion === 'PRUEBA';
      if (f === 'suspendidas') return !n.activo;
      if (f === 'por-cobrar') return n.activo && this.diasParaVencer(n) !== null && this.diasParaVencer(n)! <= 7;
      return true;
    });
  });

  readonly conteos = computed(() => {
    const l = this.negocios();
    return {
      todas: l.length,
      activas: l.filter(n => n.activo).length,
      prueba: l.filter(n => n.activo && n.estadoSuscripcion === 'PRUEBA').length,
      porCobrar: l.filter(n => n.activo && this.diasParaVencer(n) !== null && this.diasParaVencer(n)! <= 7).length,
      suspendidas: l.filter(n => !n.activo).length,
    };
  });

  ngOnInit() {
    this.cargar();
    // El panel se mantiene al día solo: nuevas lavanderías registradas desde la app y
    // renovaciones cobradas con tarjeta aparecen sin recargar la página.
    const timer = setInterval(() => { if (document.visibilityState === 'visible') this.cargar(true); }, REFRESCO_MS);
    const alVolver = () => { if (document.visibilityState === 'visible') this.cargar(true); };
    document.addEventListener('visibilitychange', alVolver);
    this.destroyRef.onDestroy(() => { clearInterval(timer); document.removeEventListener('visibilitychange', alVolver); });
  }

  /** `silencioso`: refresco automático, sin spinner y avisando de las altas nuevas. */
  cargar(silencioso = false) {
    if (!silencioso) { this.cargando.set(true); this.error.set(null); }
    this.svc.resumen().subscribe({ next: r => this.resumen.set(r), error: () => {} });
    this.svc.actividad().subscribe({ next: a => this.actividad.set(a), error: () => {} });
    this.svc.listar().subscribe({
      next: list => {
        if (silencioso) {
          const antes = new Set(this.negocios().map(n => n.id));
          for (const n of list.filter(x => !antes.has(x.id))) this.toast.exito(`Nueva empresa registrada: ${n.nombre}`);
        }
        this.negocios.set(list);
        this.cargando.set(false);
        this.actualizado.set(new Date());
      },
      error: (err: HttpErrorResponse) => {
        if (silencioso) return;
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar las empresas.'));
      }
    });
  }

  abrirActividad(a: ActividadPlataforma) { this.router.navigate(['/plataforma/empresa', a.negocioId]); }

  /** Etiqueta del cobro automático con tarjeta, o null si la empresa paga a mano. */
  cobroAutomatico(n: NegocioResumen): { texto: string; clase: string } | null {
    switch (n.pagoAutomatico) {
      case 'ACTIVA': return { texto: 'Tarjeta automática', clase: 'badge--verde' };
      case 'FALLIDA': return { texto: 'Tarjeta rechazada', clase: 'badge--rojo' };
      default: return null;
    }
  }

  nueva() { this.router.navigate(['/plataforma/nueva']); }
  abrir(n: NegocioResumen) { this.router.navigate(['/plataforma/empresa', n.id]); }

  // ---- Suscripción / cobro ----
  diasParaVencer(n: NegocioResumen): number | null {
    if (!n.proximoPago) return null;
    const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
    const pago = new Date(n.proximoPago);
    return Math.round((pago.getTime() - hoy.getTime()) / 86_400_000);
  }

  estadoSuscripcion(n: NegocioResumen): { texto: string; clase: string } {
    if (!n.activo) return { texto: 'Suspendida', clase: 'badge--gris' };
    const dias = this.diasParaVencer(n);
    if (n.estadoSuscripcion === 'VENCIDA' || (dias !== null && dias < 0)) return { texto: 'Vencida', clase: 'badge--rojo' };
    if (n.estadoSuscripcion === 'PRUEBA') return { texto: 'Prueba', clase: 'badge--azul' };
    if (dias !== null && dias <= 7) return { texto: `Vence en ${dias}d`, clase: 'badge--naranja' };
    return { texto: 'Al día', clase: 'badge--verde' };
  }

  // ---- Suspender / reactivar ----
  toggleActivo(n: NegocioResumen, ev?: Event) {
    ev?.stopPropagation();
    if (n.activo) { this.confirmarDesactivar.set(n); return; }
    this.aplicarCambioEstado(n, true);
  }

  confirmarDesactivarOk() {
    const n = this.confirmarDesactivar();
    if (!n) return;
    this.aplicarCambioEstado(n, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(n: NegocioResumen, activo: boolean) {
    this.svc.cambiarEstado(n.id, activo).subscribe({
      next: () => {
        this.toast.info(activo ? 'Empresa reactivada' : 'Empresa suspendida');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }
}
