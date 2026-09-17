import { Injectable, signal } from '@angular/core';

export type DemoPlan = 'BASICO' | 'FACTURA' | 'MULTISEDE';

export interface DemoPreviewProfile {
  nombre: string;
  plan: DemoPlan;
  logoUrl?: string;
}

/**
 * Personalización local de la demo pública. No crea un tenant, usuario ni dato
 * comercial: solo mejora el recorrido de quien está evaluando LunaLav.
 */
@Injectable({ providedIn: 'root' })
export class DemoPreviewService {
  private readonly storageKey = 'lunalav.demo-preview.v1';
  private readonly _perfil = signal<DemoPreviewProfile | null>(this.leer());
  readonly perfil = this._perfil.asReadonly();

  esDemoPublica(): boolean {
    return typeof window !== 'undefined' && window.location.hostname.toLowerCase() === 'demo.lunalav.pe';
  }

  guardar(perfil: DemoPreviewProfile): void {
    const limpio: DemoPreviewProfile = {
      nombre: perfil.nombre.trim().slice(0, 80) || 'Mi lavandería',
      plan: perfil.plan,
      logoUrl: perfil.logoUrl
    };
    this._perfil.set(limpio);
    try { localStorage.setItem(this.storageKey, JSON.stringify(limpio)); } catch { /* almacenamiento opcional */ }
  }

  private leer(): DemoPreviewProfile | null {
    try {
      const raw = localStorage.getItem(this.storageKey);
      if (!raw) return null;
      const value = JSON.parse(raw) as DemoPreviewProfile;
      return value?.nombre && ['BASICO', 'FACTURA', 'MULTISEDE'].includes(value.plan) ? value : null;
    } catch { return null; }
  }
}
