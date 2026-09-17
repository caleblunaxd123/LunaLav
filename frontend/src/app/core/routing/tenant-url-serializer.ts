import { Injectable, inject } from '@angular/core';
import { DefaultUrlSerializer, UrlTree } from '@angular/router';
import { TenantContextService } from '../services/tenant-context.service';

/**
 * Antepone/quita de forma transparente el slug de empresa (/:empresaSlug/...) de la URL
 * visible en el navegador, sin que el árbol de rutas de Angular (app.routes.ts) ni ningún
 * componente existente sepa que existe. Así /demo/pedidos navega exactamente a la misma
 * ruta 'pedidos' de siempre — el slug solo se guarda en TenantContextService para volver
 * a anteponerlo al serializar la siguiente URL.
 *
 * Los segmentos de primer nivel que YA existen como rutas reales (login, pedidos, etc.) se
 * tratan como "sin slug" — así un bookmark viejo a /pedidos sigue funcionando igual que hoy.
 */
const SEGMENTOS_RESERVADOS = new Set([
  'login', 'ticket', 'cuadre-caja', 'seleccionar-sede', 'inicio', 'pedidos', 'registrar',
  'registro-antiguo', 'clientes', 'promociones', 'reportes', 'inventario', 'ajustes',
  'facturacion', 'assets', 'plataforma', 'seguimiento', 'repartidor', 'recibo-suscripcion',
]);

const SLUG_VALIDO = /^[a-z0-9][a-z0-9-]{1,49}$/i;

@Injectable()
export class TenantUrlSerializer extends DefaultUrlSerializer {
  private readonly tenant = inject(TenantContextService);

  override parse(url: string): UrlTree {
    const match = url.match(/^\/([^/?#]+)(.*)$/);
    if (match && SLUG_VALIDO.test(match[1]) && !SEGMENTOS_RESERVADOS.has(match[1].toLowerCase())) {
      this.tenant.establecerSlug(match[1]);
      const resto = match[2] || '/';
      return super.parse(resto.startsWith('/') ? resto : `/${resto}`);
    }
    this.tenant.establecerSlug(null);
    return super.parse(url);
  }

  override serialize(tree: UrlTree): string {
    const base = super.serialize(tree);
    const slug = this.tenant.slug();
    return slug ? `/${slug}${base}` : base;
  }
}
