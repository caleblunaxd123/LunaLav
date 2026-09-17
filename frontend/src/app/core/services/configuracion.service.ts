import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { catchError, tap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ConfiguracionNegocio } from '../models/models';

const FALLBACK: ConfiguracionNegocio = {
  id: 0,
  nombreNegocio: 'Mi Lavandería',
  logoUrl: null,
  colorPrimario: '#10233f',   // LunaLav azul noche
  colorSecundario: '#2dd4bf', // LunaLav turquesa
  colorAcento: '#8b5cf6',     // LunaLav violeta
  igv: 18,
  metaMensual: 0,
  solesPorPunto: 1,
  anchoTicketMm: 80,
  mensajePieTicket: 'Gracias por su preferencia.',
  costoDelivery: 0,
  valorPuntoCanje: 0,
  maxDescuentoPct: 0,
};

@Injectable({ providedIn: 'root' })
export class ConfiguracionService {
  private readonly http = inject(HttpClient);
  readonly configuracion = signal<ConfiguracionNegocio>(FALLBACK);

  cargar() {
    return this.http.get<ConfiguracionNegocio>(`${environment.apiUrl}/configuracion`).pipe(
      tap(c => {
        this.configuracion.set(c);
        this.aplicarTema(c);
      }),
      catchError(err => this.resetearAFallback(err))
    );
  }

  /** Marca de la empresa identificada por el slug de su URL (antes de iniciar sesión). */
  cargarPorSlug(slug: string) {
    return this.http.get<ConfiguracionNegocio>(`${environment.apiUrl}/configuracion/publico/${encodeURIComponent(slug)}`).pipe(
      tap(c => {
        this.configuracion.set(c);
        this.aplicarTema(c);
      }),
      catchError(err => this.resetearAFallback(err))
    );
  }

  /**
   * Si la carga falla (ej. una empresa que aún no guardó su configuración), se vuelve al
   * genérico neutral en vez de dejar pegada la marca de la última empresa que sí cargó bien.
   */
  private resetearAFallback(err: unknown) {
    this.configuracion.set(FALLBACK);
    this.aplicarTema(FALLBACK);
    return throwError(() => err);
  }

  /** Sube el logo desde el equipo del usuario y devuelve la ruta con la que quedó guardado. */
  subirLogo(archivo: File) {
    const fd = new FormData();
    fd.append('archivo', archivo);
    return this.http.post<{ logoUrl: string }>(`${environment.apiUrl}/configuracion/logo`, fd);
  }

  actualizar(c: ConfiguracionNegocio) {
    return this.http.put<void>(`${environment.apiUrl}/configuracion`, c).pipe(
      tap(() => {
        this.configuracion.set(c);
        this.aplicarTema(c);
      })
    );
  }

  /**
   * Variante del logo optimizada para impresión: monocroma sobre fondo transparente,
   * que se funde con el papel blanco del ticket/cuadre. El logo de marca con fondo de
   * color se reserva para el sidebar/login oscuros. Si el negocio no tiene variante de
   * impresión, cae a su logo normal (multi-tenant seguro).
   */
  static logoImpresion(logoUrl: string | null | undefined): string | null {
    if (!logoUrl) return null;
    return logoUrl;
  }

  private aplicarTema(c: ConfiguracionNegocio) {
    const root = document.documentElement;
    root.style.setProperty('--azul-oscuro', c.colorPrimario);
    root.style.setProperty('--azul-claro', c.colorSecundario);
    root.style.setProperty('--naranja', c.colorAcento);
    root.style.setProperty('--texto-sobre-secundario', this.textoConMayorContraste(c.colorSecundario));
    // Texto legible sobre el color de acento/marca (--naranja). Para marcas con acento claro
    // (ej. un dorado claro) da texto oscuro; para acentos oscuros, blanco.
    root.style.setProperty('--texto-sobre-acento', this.textoConMayorContraste(c.colorAcento));
    document.title = c.nombreNegocio;
  }

  private textoConMayorContraste(color: string): '#ffffff' | '#0f172a' {
    const valor = color.trim().replace(/^#/, '');
    const hex = valor.length === 3 ? valor.split('').map(c => c + c).join('') : valor;
    if (!/^[0-9a-f]{6}$/i.test(hex)) return '#ffffff';

    const canales = [0, 2, 4].map(i => Number.parseInt(hex.slice(i, i + 2), 16) / 255)
      .map(c => c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4));
    const luminancia = 0.2126 * canales[0] + 0.7152 * canales[1] + 0.0722 * canales[2];
    return (luminancia + 0.05) / 0.05 >= 1.05 / (luminancia + 0.05) ? '#0f172a' : '#ffffff';
  }
}
