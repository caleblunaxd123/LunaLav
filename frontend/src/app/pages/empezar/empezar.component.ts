import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute } from '@angular/router';
import { Subject, debounceTime, map, switchMap } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import {
  DisponibilidadSlug,
  PlanComercial,
  RegistrarPruebaResponse,
  RegistroPublicoService,
  planDesdeQuery,
  sugerirSlug
} from '../../core/services/registro-publico.service';

type Paso = 'negocio' | 'cuenta' | 'codigo' | 'listo';

const SLUG_VALIDO = /^[a-z0-9][a-z0-9-]{1,49}$/;
const USUARIO_VALIDO = /^[a-z0-9._-]{3,50}$/;
const EMAIL_VALIDO = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

export const PLANES_ALTA: ReadonlyArray<{ id: PlanComercial; nombre: string; precio: number; resumen: string }> = [
  { id: 'BASICO', nombre: 'Básico', precio: 20, resumen: 'Pedidos, clientes y caja · 1 sede' },
  { id: 'FACTURA', nombre: 'Factura', precio: 50, resumen: 'Todo lo Básico + boletas y facturas SUNAT' },
  { id: 'MULTISEDE', nombre: 'Multisede', precio: 80, resumen: '2 sedes, reportes consolidados' }
];

/**
 * Alta autoservicio desde la web comercial: tres pasos y la lavandería queda creada con
 * 14 días de prueba, sin escribir a nadie. Reutiliza /api/registro (el mismo que usa la app móvil).
 */
@Component({
  selector: 'app-empezar',
  imports: [FormsModule],
  templateUrl: './empezar.component.html',
  styleUrl: './empezar.component.scss'
})
export class EmpezarComponent implements OnInit {
  private readonly registro = inject(RegistroPublicoService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly title = inject(Title);
  private readonly auth = inject(AuthService);
  private readonly slugCambios = new Subject<string>();
  private slugEditadoAMano = false;

  readonly planes = PLANES_ALTA;
  readonly paso = signal<Paso>('negocio');
  readonly enviando = signal(false);
  readonly error = signal<string | null>(null);
  readonly disponibilidad = signal<DisponibilidadSlug | null>(null);
  readonly resultado = signal<RegistrarPruebaResponse | null>(null);

  form = {
    nombreNegocio: '',
    slug: '',
    plan: 'BASICO' as PlanComercial,
    sedeNombre: 'Principal',
    nombreResponsable: '',
    email: '',
    celular: '',
    usuario: '',
    password: '',
    aceptaTerminos: false,
    codigo: ''
  };

  readonly planElegido = computed(() => this.planes.find(p => p.id === this.form.plan) ?? this.planes[0]);

  ngOnInit(): void {
    this.title.setTitle('Prueba LunaLav gratis 14 días · Sistema para lavanderías en Perú');
    this.form.plan = planDesdeQuery(this.route.snapshot.queryParamMap.get('plan'));
    // Cada respuesta se aplica sólo si corresponde al código que hay escrito ahora: una respuesta
    // tardía de un código anterior no puede marcar el actual como disponible u ocupado.
    this.slugCambios.pipe(
      debounceTime(350),
      switchMap(slug => this.registro.disponible(slug).pipe(map(d => ({ slug, d })))),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(({ slug, d }) => {
      if (slug === this.form.slug) this.disponibilidad.set(d);
    });
  }

  alCambiarNombre(): void {
    if (this.slugEditadoAMano) return;
    this.form.slug = sugerirSlug(this.form.nombreNegocio);
    this.verificarSlug();
  }

  alCambiarSlug(): void {
    this.slugEditadoAMano = true;
    this.form.slug = this.form.slug.toLowerCase().trim();
    this.verificarSlug();
  }

  private verificarSlug(): void {
    this.disponibilidad.set(null);
    if (SLUG_VALIDO.test(this.form.slug)) this.slugCambios.next(this.form.slug);
  }

  codigoValido(): boolean {
    return /^\d{6}$/.test(this.form.codigo.trim());
  }

  // Los límites replican RegistrarPruebaRequest: así un 400 de validación nunca llega después de pedir el código.
  erroresNegocio(): string[] {
    const e: string[] = [];
    const nombre = this.form.nombreNegocio.trim();
    if (nombre.length < 2 || nombre.length > 120) e.push('Escribe el nombre de tu lavandería (2 a 120 caracteres).');
    if (!SLUG_VALIDO.test(this.form.slug)) e.push('El código de empresa usa letras minúsculas, números y guiones.');
    const d = this.disponibilidad();
    if (d?.estado === 'ocupado') e.push(d.mensaje);
    return e;
  }

  erroresCuenta(): string[] {
    const e: string[] = [];
    const responsable = this.form.nombreResponsable.trim();
    const email = this.form.email.trim();
    const celular = this.form.celular.trim();
    if (responsable.length < 2 || responsable.length > 120) e.push('Escribe tu nombre (2 a 120 caracteres).');
    if (!EMAIL_VALIDO.test(email) || email.length > 150) e.push('Escribe un correo válido.');
    if (celular.replace(/\D/g, '').length < 7 || celular.length > 30) e.push('Escribe un celular válido.');
    if (!USUARIO_VALIDO.test(this.form.usuario.trim().toLowerCase())) e.push('El usuario tiene 3 a 50 letras, números, punto o guion.');
    if (this.form.password.length < 8 || this.form.password.length > 100) e.push('La contraseña debe tener entre 8 y 100 caracteres.');
    if (!this.form.aceptaTerminos) e.push('Acepta los términos y la política de privacidad.');
    return e;
  }

  irACuenta(): void {
    this.error.set(null);
    if (this.erroresNegocio().length === 0) this.paso.set('cuenta');
  }

  volver(paso: Paso): void {
    this.error.set(null);
    this.paso.set(paso);
  }

  enviarCodigo(): void {
    if (this.erroresCuenta().length > 0 || this.enviando()) return;
    this.enviando.set(true);
    this.error.set(null);
    this.registro.solicitarCodigo(this.form.email.trim()).subscribe({
      next: () => {
        this.enviando.set(false);
        this.paso.set('codigo');
      },
      error: (err: HttpErrorResponse) => this.fallar(err)
    });
  }

  crearLavanderia(): void {
    if (!this.codigoValido() || this.enviando()) return;
    this.enviando.set(true);
    this.error.set(null);
    this.registro.registrar({
      nombreNegocio: this.form.nombreNegocio.trim(),
      slug: this.form.slug,
      nombreResponsable: this.form.nombreResponsable.trim(),
      email: this.form.email.trim(),
      celular: this.form.celular.trim(),
      usuario: this.form.usuario.trim().toLowerCase(),
      password: this.form.password,
      plan: this.form.plan,
      sedeNombre: this.form.sedeNombre.trim() || 'Principal',
      aceptaTerminos: this.form.aceptaTerminos,
      codigoVerificacion: this.form.codigo.trim()
    }).subscribe({
      next: r => {
        this.enviando.set(false);
        this.resultado.set(r);
        this.paso.set('listo');
      },
      error: (err: HttpErrorResponse) => this.fallar(err)
    });
  }

  /** Origen de la app: la web comercial (lunalav.pe) manda a app.lunalav.pe; app, staging y local usan el suyo. */
  private origenApp(): string {
    if (typeof window === 'undefined') return 'https://app.lunalav.pe';
    const host = window.location.hostname;
    return host === 'lunalav.pe' || host === 'www.lunalav.pe' ? 'https://app.lunalav.pe' : window.location.origin;
  }

  urlLogin(): string {
    return `${this.origenApp()}/login`;
  }

  /**
   * Si el navegador ya tenía la sesión de otra lavandería, el guard de invitados no dejaría abrir
   * el login de la nueva: se cierra esa sesión y se navega con recarga completa (contexto limpio).
   */
  entrar(evento: Event): void {
    if (!this.auth.autenticado()) return;
    evento.preventDefault();
    this.auth.cerrarSesion();
    this.navegar(this.urlIngreso());
  }

  /** Separado para poder probarlo: window.location.assign no se puede espiar en el navegador. */
  navegar(url: string): void {
    window.location.assign(url);
  }

  urlIngreso(): string {
    return `${this.origenApp()}/${this.resultado()?.slug ?? this.form.slug}/login`;
  }

  /** "2026-11-06" -> "6 de noviembre de 2026" (fecha de calendario, sin desfase horario). */
  fechaFinTexto(): string {
    const iso = this.resultado()?.pruebaHasta;
    if (!iso) return '';
    return new Date(`${iso}T00:00:00Z`).toLocaleDateString('es-PE', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });
  }

  private fallar(err: HttpErrorResponse): void {
    this.enviando.set(false);
    const mensaje = typeof err.error?.mensaje === 'string' ? err.error.mensaje : null;
    if (err.status === 429) this.error.set('Hiciste muchos intentos seguidos. Espera unos minutos y vuelve a probar.');
    else this.error.set(mensaje ?? 'No pudimos completar el alta. Revisa tu conexión e inténtalo de nuevo.');
    // El backend marca con campo "codigo" los errores del código; el resto vuelve al paso de cuenta o negocio.
    if (err.error?.campo !== 'codigo' && this.paso() === 'codigo' && mensaje?.toLowerCase().includes('código de empresa')) {
      this.paso.set('negocio');
    }
  }
}
