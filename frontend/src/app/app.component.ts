import { CommonModule } from '@angular/common';
import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { HeaderComponent } from './layout/header/header.component';
import { PlataformaSidebarComponent } from './layout/plataforma-sidebar/plataforma-sidebar.component';
import { AuthService } from './core/services/auth.service';
import { ConfiguracionService } from './core/services/configuracion.service';
import { TenantContextService } from './core/services/tenant-context.service';
import { ToasterComponent } from './shared/toaster/toaster.component';
import { AlertasGlobalesComponent } from './shared/alertas-globales/alertas-globales.component';
import { TourOverlayComponent } from './shared/tour/tour-overlay.component';
import { VersionCheckService } from './core/services/version-check.service';

const SEGMENTOS_RUTA_APP = new Set([
  'login', 'ticket', 'cuadre-caja', 'seleccionar-sede', 'inicio', 'pedidos', 'registrar',
  'registro-antiguo', 'clientes', 'promociones', 'reportes', 'inventario', 'ajustes',
  'facturacion', 'assets', 'plataforma', 'seguimiento', 'repartidor', 'recibo-suscripcion', 'nosotros',
]);

@Component({
  selector: 'app-root',
  imports: [CommonModule, RouterOutlet, HeaderComponent, PlataformaSidebarComponent, AlertasGlobalesComponent, ToasterComponent, TourOverlayComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly config = inject(ConfiguracionService);
  private readonly tenant = inject(TenantContextService);
  readonly versionCheck = inject(VersionCheckService);

  private readonly rutaActual = signal<string | null>(null);
  readonly esPlataforma = computed(() => this.rutaActual()?.startsWith('/plataforma') ?? false);
  readonly mostrarHeader = computed(() => {
    const r = this.rutaActual();
    if (!r || !this.auth.autenticado()) return false;
    // Un usuario operativo sin sede activa está necesariamente en el selector de sede. No debe
    // ver enlaces a módulos que requieren SedeId y solo devolverían "Selecciona una sede".
    if (!this.auth.usuario()?.sedeId) return false;
    if (r.startsWith('/login')) return false;
    if (r.startsWith('/seleccionar-sede')) return false;
    if (r.startsWith('/ticket/')) return false;  // ticket es fullscreen para imprimir
    if (r.startsWith('/cuadre-caja/imprimir/')) return false;  // cuadre imprimible tambien
    if (r.startsWith('/seguimiento/')) return false;  // portal publico del cliente (incluye pago): jamas mostrar el nav interno
    if (r.startsWith('/repartidor/')) return false;  // portal publico del repartidor
    if (r.startsWith('/recibo-suscripcion/')) return false;  // recibo imprimible del propietario
    if (this.esPlataforma()) return false;  // usa su propio header minimo
    return true;
  });
  readonly mostrarAlertas = computed(() => {
    if (!this.auth.autenticado()) return false;
    const r = this.rutaActual();
    if (!r) return false;
    // El aviso global (franja de "atención") vive SOLO en la pantalla de inicio de cada panel
    // —Inicio del negocio y Panel del propietario—. En el resto de módulos ya no aparece:
    // las mismas alertas están en la campana "Atención operativa" del sidebar.
    // endsWith cubre la ruta con o sin el slug del tenant (ej. "/inicio" y "/demo/inicio").
    return r.endsWith('/inicio') || r === '/plataforma' || r.endsWith('/plataforma');
  });

  ngOnInit() {
    // Vigila si el sistema fue actualizado en el servidor para ofrecer recargar.
    this.versionCheck.iniciar();

    // La primera navegacion es la que hace que TenantUrlSerializer.parse() fije el slug;
    // si se lee tenant.slug() antes de eso (fuera de este subscribe) puede llegar en null
    // y cargar la marca generica por error. Por eso la carga de marca va DESPUES del primer
    // NavigationEnd, nunca de forma sincronica en el cuerpo de ngOnInit.
    let primeraNavegacion = true;
    this.router.events
      .pipe(filter(e => e instanceof NavigationEnd))
      .subscribe(e => {
        this.rutaActual.set(this.quitarSlug((e as NavigationEnd).urlAfterRedirects));
        if (primeraNavegacion) {
          primeraNavegacion = false;
          const slug = this.tenant.slug();
          // Login neutral (/login, sin slug): NUNCA cargar la marca de un negocio —
          // la pantalla es del PRODUCTO (LunaLav), no de ninguna lavandería, aun
          // si quedó una sesión abierta de un tenant.
          const enLoginNeutral = !slug && (this.rutaActual()?.startsWith('/login') ?? false);
          if (slug) {
            this.config.cargarPorSlug(slug).subscribe({ error: () => {} });
          } else if (this.auth.autenticado() && !enLoginNeutral) {
            this.config.cargar().subscribe({ error: () => {} });
          }
        }
      });
  }

  /**
   * `urlAfterRedirects` ya viene con el slug de empresa antepuesto (pasa por
   * TenantUrlSerializer.serialize). Se le quita antes de guardarla para que los chequeos
   * de mostrarHeader (basados en rutas absolutas sin slug) sigan funcionando sin cambios.
   */
  private quitarSlug(url: string): string {
    const slug = this.tenant.slug();
    if (slug && url.startsWith(`/${slug}`)) {
      const resto = url.slice(slug.length + 1);
      return resto ? `/${resto}` : '/';
    }

    // NavigationEnd puede entregar la URL ya serializada con /:slug aunque el contexto haya
    // cambiado durante la misma navegación (por ejemplo, login -> seleccionar sede). Detectar
    // la segunda parte evita que el shell interno aparezca en pantallas fullscreen.
    const corte = url.search(/[?#]/u);
    const ruta = corte >= 0 ? url.slice(0, corte) : url;
    const sufijo = corte >= 0 ? url.slice(corte) : '';
    const segmentos = ruta.split('/').filter(Boolean);
    if (segmentos.length > 1 && !SEGMENTOS_RUTA_APP.has(segmentos[0].toLowerCase()) &&
        SEGMENTOS_RUTA_APP.has(segmentos[1].toLowerCase())) {
      return `/${segmentos.slice(1).join('/')}${sufijo}`;
    }
    return url;
  }

  // Cierra el modal/drawer más al frente simulando el click que cada
  // componente ya usa en su backdrop para cerrar (evita duplicar lógica
  // de cierre en cada pantalla).
  @HostListener('document:keydown.escape')
  cerrarConEscape() {
    const backdrops = document.querySelectorAll<HTMLElement>('.modal-backdrop, .drawer-backdrop, .sb-backdrop');
    const topmost = backdrops[backdrops.length - 1];
    topmost?.click();
  }
}
