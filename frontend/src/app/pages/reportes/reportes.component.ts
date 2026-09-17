import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface Reporte {
  nombre: string;
  clave: string;
  icono: SafeHtml;
  color: string;
}

// Ilustraciones hero SVG estilo flat, similar al SaaS de referencia
const ILUSTRACIONES: Record<string, string> = {
  clipboard: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rC1" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#dbeafe"/><stop offset="1" stop-color="#bfdbfe"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rC1)"/>
      <rect x="34" y="24" width="52" height="74" rx="4" fill="#fff" stroke="#1e40af" stroke-width="2"/>
      <rect x="46" y="18" width="28" height="12" rx="2" fill="#1e40af"/>
      <circle cx="60" cy="24" r="2.5" fill="#fbbf24"/>
      <rect x="42" y="42" width="36" height="4" rx="2" fill="#93c5fd"/>
      <rect x="42" y="52" width="30" height="4" rx="2" fill="#e5e7eb"/>
      <rect x="42" y="62" width="36" height="4" rx="2" fill="#93c5fd"/>
      <rect x="42" y="72" width="26" height="4" rx="2" fill="#e5e7eb"/>
      <circle cx="98" cy="34" r="10" fill="#f59e0b"/>
      <circle cx="98" cy="34" r="5" fill="#fff"/>
      <path d="M96 33l1.5 1.5L101 31" stroke="#f59e0b" stroke-width="1.6" fill="none" stroke-linecap="round"/>
    </svg>
  `,
  receipt: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rC2" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fee2e2"/><stop offset="1" stop-color="#fecaca"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rC2)"/>
      <path d="M36 20h48v78l-8-6-8 6-8-6-8 6-8-6-8 6z" fill="#fff" stroke="#dc2626" stroke-width="2"/>
      <rect x="42" y="30" width="36" height="3" rx="1.5" fill="#dc2626"/>
      <rect x="42" y="40" width="24" height="3" rx="1.5" fill="#94a3b8"/>
      <rect x="70" y="40" width="8" height="3" rx="1.5" fill="#dc2626"/>
      <rect x="42" y="50" width="28" height="3" rx="1.5" fill="#94a3b8"/>
      <rect x="72" y="50" width="6" height="3" rx="1.5" fill="#dc2626"/>
      <rect x="42" y="60" width="20" height="3" rx="1.5" fill="#94a3b8"/>
      <rect x="66" y="60" width="12" height="3" rx="1.5" fill="#dc2626"/>
      <rect x="42" y="72" width="36" height="4" rx="2" fill="#1e293b"/>
      <circle cx="24" cy="80" r="6" fill="#fbbf24"/>
      <text x="24" y="83" text-anchor="middle" font-size="7" font-weight="800" fill="#78350f" font-family="sans-serif">S/</text>
    </svg>
  `,
  chart: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rC3" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fef3c7"/><stop offset="1" stop-color="#fde68a"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rC3)"/>
      <rect x="24" y="80" width="18" height="20" rx="2" fill="#3b82f6"/>
      <rect x="46" y="60" width="18" height="40" rx="2" fill="#f59e0b"/>
      <rect x="68" y="42" width="18" height="58" rx="2" fill="#10b981"/>
      <rect x="90" y="26" width="14" height="74" rx="2" fill="#dc2626"/>
      <path d="M20 96l16-24 20 12 22-32 20-14" stroke="#1e293b" stroke-width="2.5" fill="none" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="4 3"/>
      <circle cx="36" cy="72" r="3" fill="#1e293b"/>
      <circle cx="56" cy="84" r="3" fill="#1e293b"/>
      <circle cx="78" cy="52" r="3" fill="#1e293b"/>
      <circle cx="98" cy="38" r="3" fill="#1e293b"/>
    </svg>
  `,
  basket: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rC4" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#dcfce7"/><stop offset="1" stop-color="#bbf7d0"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rC4)"/>
      <path d="M28 46h64l-8 50a4 4 0 0 1-4 4H40a4 4 0 0 1-4-4z" fill="#10b981"/>
      <path d="M28 46h64v6H28z" fill="#065f46"/>
      <path d="M42 46V32c0-10 8-18 18-18s18 8 18 18v14" fill="none" stroke="#065f46" stroke-width="3"/>
      <rect x="44" y="60" width="4" height="26" rx="2" fill="#065f46" opacity=".4"/>
      <rect x="58" y="60" width="4" height="26" rx="2" fill="#065f46" opacity=".4"/>
      <rect x="72" y="60" width="4" height="26" rx="2" fill="#065f46" opacity=".4"/>
      <circle cx="20" cy="30" r="6" fill="#f59e0b"/>
      <circle cx="100" cy="26" r="5" fill="#8b5cf6"/>
    </svg>
  `,
  cash: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rC5" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#e0e7ff"/><stop offset="1" stop-color="#c7d2fe"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rC5)"/>
      <rect x="22" y="42" width="76" height="44" rx="4" fill="#059669" stroke="#065f46" stroke-width="2"/>
      <circle cx="60" cy="64" r="14" fill="#a7f3d0" stroke="#065f46" stroke-width="2"/>
      <text x="60" y="69" text-anchor="middle" font-size="14" font-weight="800" fill="#065f46" font-family="sans-serif">S/</text>
      <rect x="28" y="48" width="6" height="6" rx="1" fill="#065f46"/>
      <rect x="86" y="74" width="6" height="6" rx="1" fill="#065f46"/>
      <circle cx="30" cy="30" r="8" fill="#fbbf24"/>
      <text x="30" y="34" text-anchor="middle" font-size="8" font-weight="800" fill="#78350f" font-family="sans-serif">S/</text>
      <circle cx="94" cy="34" r="6" fill="#fbbf24"/>
    </svg>
  `,
  calendar: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rC6" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fce7f3"/><stop offset="1" stop-color="#fbcfe8"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rC6)"/>
      <rect x="26" y="30" width="68" height="66" rx="6" fill="#fff" stroke="#be185d" stroke-width="2"/>
      <rect x="26" y="30" width="68" height="16" rx="6" fill="#be185d"/>
      <rect x="26" y="42" width="68" height="4" fill="#be185d"/>
      <rect x="36" y="22" width="6" height="14" rx="2" fill="#831843"/>
      <rect x="78" y="22" width="6" height="14" rx="2" fill="#831843"/>
      <!-- días -->
      <circle cx="42" cy="60" r="3" fill="#fbcfe8"/>
      <circle cx="60" cy="60" r="3" fill="#fbcfe8"/>
      <circle cx="78" cy="60" r="3" fill="#fbcfe8"/>
      <circle cx="42" cy="76" r="3" fill="#fbcfe8"/>
      <circle cx="60" cy="76" r="6" fill="#be185d"/>
      <text x="60" y="80" text-anchor="middle" font-size="8" font-weight="800" fill="#fff" font-family="sans-serif">15</text>
      <circle cx="78" cy="76" r="3" fill="#fbcfe8"/>
    </svg>
  `,
  archive: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rC7" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#e9d5ff"/><stop offset="1" stop-color="#d8b4fe"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rC7)"/>
      <rect x="22" y="34" width="76" height="14" rx="2" fill="#7c3aed"/>
      <rect x="26" y="48" width="68" height="54" rx="2" fill="#a78bfa"/>
      <rect x="42" y="60" width="36" height="6" rx="3" fill="#fff"/>
      <rect x="30" y="72" width="24" height="14" rx="2" fill="#fff"/>
      <rect x="60" y="72" width="18" height="14" rx="2" fill="#fff"/>
      <rect x="82" y="72" width="10" height="14" rx="2" fill="#fff"/>
      <circle cx="14" cy="24" r="5" fill="#f59e0b"/>
      <circle cx="100" cy="20" r="4" fill="#10b981"/>
    </svg>
  `,
  ban: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rC8" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fee2e2"/><stop offset="1" stop-color="#fecaca"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rC8)"/>
      <rect x="32" y="34" width="52" height="60" rx="4" fill="#fff" stroke="#dc2626" stroke-width="2" opacity=".85"/>
      <rect x="40" y="42" width="30" height="3" rx="1" fill="#dc2626"/>
      <rect x="40" y="50" width="24" height="3" rx="1" fill="#94a3b8"/>
      <rect x="40" y="58" width="28" height="3" rx="1" fill="#94a3b8"/>
      <path d="M50 40l30 30M80 40l-30 30" stroke="#dc2626" stroke-width="4" stroke-linecap="round"/>
      <circle cx="94" cy="30" r="14" fill="#dc2626"/>
      <path d="M84 30h20" stroke="#fff" stroke-width="3" stroke-linecap="round"/>
    </svg>
  `,
  moped: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rC9" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ccfbf1"/><stop offset="1" stop-color="#99f6e4"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rC9)"/>
      <!-- Bolsa entrega -->
      <rect x="46" y="34" width="36" height="30" rx="3" fill="#f59e0b"/>
      <rect x="46" y="34" width="36" height="10" rx="3" fill="#d97706"/>
      <!-- Moto -->
      <circle cx="34" cy="86" r="12" fill="#1e293b"/>
      <circle cx="34" cy="86" r="5" fill="#94a3b8"/>
      <circle cx="88" cy="86" r="12" fill="#1e293b"/>
      <circle cx="88" cy="86" r="5" fill="#94a3b8"/>
      <path d="M34 86l14-20h30l10 20" stroke="#dc2626" stroke-width="6" fill="none" stroke-linecap="round"/>
      <path d="M78 66l-4-12h-4" stroke="#dc2626" stroke-width="4" fill="none" stroke-linecap="round"/>
      <circle cx="70" cy="52" r="6" fill="#fbcfe8"/>
      <path d="M60 20l4 8" stroke="#3b82f6" stroke-width="2" stroke-linecap="round"/>
    </svg>
  `,
  wallet: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rCa" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fef3c7"/><stop offset="1" stop-color="#fde68a"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rCa)"/>
      <rect x="22" y="44" width="76" height="44" rx="6" fill="#0f766e"/>
      <path d="M22 44l38-18 38 18" fill="none" stroke="#0f766e" stroke-width="3" stroke-linecap="round"/>
      <path d="M22 62c4-4 12-6 24-6s20 2 24 6" stroke="#134e4a" stroke-width="2" fill="none" opacity=".4"/>
      <circle cx="82" cy="68" r="6" fill="#fef3c7"/>
      <circle cx="82" cy="68" r="3" fill="#0f766e"/>
      <circle cx="38" cy="30" r="7" fill="#fbbf24"/>
      <text x="38" y="33" text-anchor="middle" font-size="7" font-weight="800" fill="#78350f" font-family="sans-serif">S/</text>
      <circle cx="66" cy="24" r="5" fill="#fbbf24"/>
    </svg>
  `,
  tag: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs><linearGradient id="rCb" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fce7f3"/><stop offset="1" stop-color="#fbcfe8"/></linearGradient></defs>
      <circle cx="60" cy="60" r="52" fill="url(#rCb)"/>
      <path d="M22 32h50l30 30-42 42-38-38z" fill="#ec4899"/>
      <circle cx="40" cy="50" r="7" fill="#fff"/>
      <text x="40" y="54" text-anchor="middle" font-size="10" font-weight="800" fill="#ec4899" font-family="sans-serif">%</text>
      <path d="M56 66l16-16M64 74l16-16M72 82l8-8" stroke="#fff" stroke-width="3" stroke-linecap="round" opacity=".5"/>
      <circle cx="20" cy="30" r="4" fill="#fbbf24"/>
      <circle cx="102" cy="94" r="4" fill="#10b981"/>
    </svg>
  `,
};

@Component({
  selector: 'app-reportes',
  imports: [CommonModule, RouterLink, PageHeaderComponent],
  templateUrl: './reportes.component.html',
  styleUrl: './reportes.component.scss'
})
export class ReportesComponent {
  reportes: Reporte[];

  constructor(private sanitizer: DomSanitizer) {
    this.reportes = [
      { nombre: 'Vista Gerencial', clave: 'gerencial', icono: this.svg('chart'), color: '#e8f1ff' },
      { nombre: 'Consolidado de sedes', clave: 'consolidado', icono: this.svg('chart'), color: '#eef0ff' },
      { nombre: 'Órdenes Pendientes', clave: 'ordenes-pendientes', icono: this.svg('clipboard'), color: '#e8f1ff' },
      { nombre: 'Gastos', clave: 'gastos', icono: this.svg('receipt'), color: '#fde8e8' },
      { nombre: 'General', clave: 'general', icono: this.svg('chart'), color: '#fff4e0' },
      { nombre: 'Servicios', clave: 'servicios', icono: this.svg('basket'), color: '#e6f9ee' },
      { nombre: 'Cuadres de Caja', clave: 'cuadres-caja', icono: this.svg('cash'), color: '#eef0ff' },
      { nombre: 'Órdenes Mensual', clave: 'ordenes-mensual', icono: this.svg('calendar'), color: '#e8f1ff' },
      { nombre: 'Almacén', clave: 'almacen', icono: this.svg('archive'), color: '#f1e8ff' },
      { nombre: 'Anulados', clave: 'anulados', icono: this.svg('ban'), color: '#fde8e8' },
      { nombre: 'Registro y Entregas', clave: 'registro-entregas', icono: this.svg('moped'), color: '#e6f9ee' },
      { nombre: 'Pagos', clave: 'pagos', icono: this.svg('wallet'), color: '#fff4e0' },
      { nombre: 'Descuento Directo', clave: 'descuento-directo', icono: this.svg('tag'), color: '#f1e8ff' },
    ];
  }

  private svg(key: string): SafeHtml {
    return this.sanitizer.bypassSecurityTrustHtml(ILUSTRACIONES[key] ?? '');
  }
}
