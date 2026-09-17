import { Injectable, signal } from '@angular/core';

/**
 * Guarda el slug de empresa leído de la URL (ej. /demo/pedidos → 'demo').
 * Lo llena TenantUrlSerializer al interpretar cada navegación; el resto de la
 * app solo lo lee (para pintar la marca antes del login y para enviarlo en el login).
 */
@Injectable({ providedIn: 'root' })
export class TenantContextService {
  private readonly _slug = signal<string | null>(null);
  readonly slug = this._slug.asReadonly();

  establecerSlug(slug: string | null) {
    if (slug !== this._slug()) this._slug.set(slug);
  }
}
