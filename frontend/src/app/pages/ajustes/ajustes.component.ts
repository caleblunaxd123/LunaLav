import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface Ajuste {
  nombre: string;
  descripcion: string;
  icono: SafeHtml;
  ruta?: string;
  proximamente?: boolean;
}

// Ilustraciones hero: SVGs completos con fondo, figura central y decoración
// Estilo: flat illustration, paleta pastel, similar al SaaS de referencia
const ILUSTRACIONES: Record<string, string> = {
  usuario: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gUsr" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#dbeafe"/><stop offset="1" stop-color="#bfdbfe"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gUsr)"/>
      <!-- Cuerpo -->
      <path d="M28 100c0-18 14-30 32-30s32 12 32 30" fill="#3b82f6"/>
      <!-- Cabeza -->
      <circle cx="60" cy="48" r="18" fill="#fbcfe8"/>
      <!-- Cabello -->
      <path d="M42 42c0-10 8-18 18-18s18 8 18 18c0 3-2 5-4 6-3-6-8-10-14-10s-11 4-14 10c-2-1-4-3-4-6z" fill="#1e293b"/>
      <!-- Bufanda/cuello -->
      <path d="M46 65c4 3 9 5 14 5s10-2 14-5v6c0 2-2 4-4 5H50c-2-1-4-3-4-5v-6z" fill="#f59e0b"/>
      <!-- Estrella decorativa -->
      <circle cx="98" cy="22" r="6" fill="#fbbf24"/>
      <circle cx="20" cy="90" r="4" fill="#a78bfa"/>
    </svg>
  `,
  tienda: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gTda" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#fef3c7"/><stop offset="1" stop-color="#fde68a"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gTda)"/>
      <!-- Techo -->
      <path d="M22 50l6-16h64l6 16H22z" fill="#dc2626"/>
      <path d="M22 50h76v6H22z" fill="#991b1b"/>
      <!-- Fachada -->
      <rect x="26" y="56" width="68" height="46" fill="#fef2f2"/>
      <!-- Puerta -->
      <rect x="52" y="72" width="16" height="30" fill="#7f1d1d"/>
      <circle cx="65" cy="87" r="1.2" fill="#fbbf24"/>
      <!-- Ventanas -->
      <rect x="32" y="64" width="14" height="14" fill="#93c5fd" stroke="#1e40af" stroke-width="1.5"/>
      <rect x="74" y="64" width="14" height="14" fill="#93c5fd" stroke="#1e40af" stroke-width="1.5"/>
      <!-- Cartel -->
      <rect x="42" y="42" width="36" height="10" rx="2" fill="#1e293b"/>
      <text x="60" y="49" text-anchor="middle" font-size="6" font-weight="700" fill="#fff" font-family="sans-serif">SHOP</text>
      <!-- Detalles -->
      <circle cx="16" cy="24" r="4" fill="#f59e0b"/>
      <circle cx="102" cy="94" r="3" fill="#84cc16"/>
    </svg>
  `,
  personal: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gPer" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#d1fae5"/><stop offset="1" stop-color="#a7f3d0"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gPer)"/>
      <!-- Persona izquierda -->
      <circle cx="38" cy="52" r="13" fill="#fbcfe8"/>
      <path d="M20 96c0-12 8-20 18-20s18 8 18 20" fill="#8b5cf6"/>
      <!-- Persona centro (más alta) -->
      <circle cx="60" cy="46" r="15" fill="#fed7aa"/>
      <path d="M40 100c0-14 9-24 20-24s20 10 20 24" fill="#059669"/>
      <!-- Persona derecha -->
      <circle cx="82" cy="52" r="13" fill="#fbcfe8"/>
      <path d="M64 96c0-12 8-20 18-20s18 8 18 20" fill="#3b82f6"/>
      <!-- Corazón decorativo -->
      <path d="M100 20c-2-3-7-3-7 1 0 3 7 8 7 8s7-5 7-8c0-4-5-4-7-1z" fill="#f43f5e"/>
    </svg>
  `,
  lavado: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gLav" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#bae6fd"/><stop offset="1" stop-color="#7dd3fc"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gLav)"/>
      <!-- Lavadora cuerpo -->
      <rect x="30" y="22" width="60" height="80" rx="6" fill="#fff" stroke="#1e293b" stroke-width="2"/>
      <!-- Panel superior -->
      <rect x="34" y="26" width="52" height="12" rx="3" fill="#e0e7ff"/>
      <circle cx="42" cy="32" r="2" fill="#f59e0b"/>
      <circle cx="52" cy="32" r="2" fill="#84cc16"/>
      <rect x="62" y="30" width="20" height="4" rx="2" fill="#94a3b8"/>
      <!-- Tambor -->
      <circle cx="60" cy="70" r="22" fill="#f0f9ff" stroke="#1e293b" stroke-width="2"/>
      <circle cx="60" cy="70" r="16" fill="#38bdf8" opacity=".3"/>
      <!-- Ropa girando -->
      <path d="M50 62c4-2 10-2 14 2s4 10-2 14-14-2-14-8" fill="#f472b6"/>
      <path d="M62 76c2-2 6-2 8 0" stroke="#8b5cf6" stroke-width="2" fill="none" stroke-linecap="round"/>
      <!-- Burbujas -->
      <circle cx="20" cy="30" r="4" fill="#fff" opacity=".8"/>
      <circle cx="14" cy="45" r="3" fill="#fff" opacity=".7"/>
      <circle cx="102" cy="25" r="5" fill="#fff" opacity=".8"/>
      <circle cx="105" cy="42" r="3" fill="#fff" opacity=".6"/>
    </svg>
  `,
  servicios: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gSrv" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#fce7f3"/><stop offset="1" stop-color="#fbcfe8"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gSrv)"/>
      <!-- Clipboard -->
      <rect x="34" y="26" width="52" height="72" rx="4" fill="#fff" stroke="#1e293b" stroke-width="2"/>
      <rect x="46" y="20" width="28" height="12" rx="2" fill="#8b5cf6"/>
      <!-- Líneas de servicios con checks -->
      <circle cx="44" cy="46" r="3" fill="#10b981"/>
      <path d="M42 46l1.5 1.5L46 45" stroke="#fff" stroke-width="1.4" fill="none" stroke-linecap="round"/>
      <rect x="52" y="43" width="30" height="4" rx="2" fill="#e5e7eb"/>
      <circle cx="44" cy="60" r="3" fill="#10b981"/>
      <path d="M42 60l1.5 1.5L46 59" stroke="#fff" stroke-width="1.4" fill="none" stroke-linecap="round"/>
      <rect x="52" y="57" width="26" height="4" rx="2" fill="#e5e7eb"/>
      <circle cx="44" cy="74" r="3" fill="#f59e0b"/>
      <rect x="52" y="71" width="22" height="4" rx="2" fill="#e5e7eb"/>
      <circle cx="44" cy="88" r="3" fill="#f59e0b"/>
      <rect x="52" y="85" width="18" height="4" rx="2" fill="#e5e7eb"/>
      <!-- Estrella decorativa -->
      <path d="M100 32l2 5 5 1-4 3 1 5-4-3-4 3 1-5-4-3 5-1z" fill="#fbbf24"/>
    </svg>
  `,
  categorias: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gCat" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#e0e7ff"/><stop offset="1" stop-color="#c7d2fe"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gCat)"/>
      <!-- Carpetas apiladas -->
      <rect x="26" y="70" width="60" height="34" rx="4" fill="#6366f1"/>
      <path d="M26 70v-6h22l6 6h32v6H26z" fill="#4f46e5"/>
      <rect x="34" y="52" width="60" height="34" rx="4" fill="#818cf8"/>
      <path d="M34 52v-6h22l6 6h32v6H34z" fill="#6366f1"/>
      <rect x="42" y="34" width="60" height="34" rx="4" fill="#a5b4fc"/>
      <path d="M42 34v-6h22l6 6h32v6H42z" fill="#818cf8"/>
      <!-- Tag -->
      <path d="M96 20l14 14-14 14-14-14z" fill="#f59e0b"/>
      <circle cx="96" cy="34" r="3" fill="#fff"/>
    </svg>
  `,
  gastos: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gGas" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#fee2e2"/><stop offset="1" stop-color="#fecaca"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gGas)"/>
      <!-- Billetera -->
      <path d="M22 44h76a4 4 0 0 1 4 4v40a4 4 0 0 1-4 4H22a4 4 0 0 1-4-4V48a4 4 0 0 1 4-4z" fill="#dc2626"/>
      <path d="M22 44l40-16 40 16" fill="none" stroke="#7f1d1d" stroke-width="3"/>
      <!-- Solapa -->
      <circle cx="86" cy="68" r="6" fill="#fef3c7"/>
      <circle cx="86" cy="68" r="2.5" fill="#dc2626"/>
      <!-- Monedas cayendo -->
      <circle cx="40" cy="30" r="8" fill="#fbbf24" stroke="#f59e0b" stroke-width="1.5"/>
      <text x="40" y="34" text-anchor="middle" font-size="10" font-weight="800" fill="#78350f" font-family="sans-serif">S/</text>
      <circle cx="70" cy="20" r="6" fill="#fbbf24" stroke="#f59e0b" stroke-width="1.5"/>
      <text x="70" y="23" text-anchor="middle" font-size="7" font-weight="800" fill="#78350f" font-family="sans-serif">S/</text>
    </svg>
  `,
  puntos: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gPto" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#fef3c7"/><stop offset="1" stop-color="#fde68a"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gPto)"/>
      <!-- Estrella grande centro -->
      <path d="M60 22l10 22 24 3-17 17 4 24-21-12-21 12 4-24-17-17 24-3z" fill="#fbbf24" stroke="#f59e0b" stroke-width="2"/>
      <!-- Brillo -->
      <path d="M52 40l4 8" stroke="#fff" stroke-width="3" stroke-linecap="round"/>
      <!-- Estrellas pequeñas alrededor -->
      <path d="M20 32l1.5 3 3.5.5-2.5 2.5.5 3.5-3-2-3 2 .5-3.5-2.5-2.5 3.5-.5z" fill="#f59e0b"/>
      <path d="M96 82l1.5 3 3.5.5-2.5 2.5.5 3.5-3-2-3 2 .5-3.5-2.5-2.5 3.5-.5z" fill="#f59e0b"/>
      <path d="M22 88l1 2 2 .3-1.5 1.5.3 2.2-1.8-1.2-1.8 1.2.3-2.2-1.5-1.5 2-.3z" fill="#f59e0b"/>
    </svg>
  `,
  whatsapp: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gWsp" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#dcfce7"/><stop offset="1" stop-color="#bbf7d0"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gWsp)"/>
      <!-- Burbuja de chat 1 -->
      <path d="M22 40c0-8 6-14 14-14h28c8 0 14 6 14 14v14c0 8-6 14-14 14H46l-10 10-2-12c-7-1-12-8-12-14V40z" fill="#25d366"/>
      <circle cx="42" cy="47" r="2.5" fill="#fff"/>
      <circle cx="50" cy="47" r="2.5" fill="#fff"/>
      <circle cx="58" cy="47" r="2.5" fill="#fff"/>
      <!-- Burbuja de chat 2 -->
      <path d="M52 68c0-6 5-11 11-11h26c6 0 11 5 11 11v10c0 6-5 11-11 11h-4l-8 8-1-9c-4-1-8-4-8-9V68z" fill="#fff" stroke="#25d366" stroke-width="2"/>
      <rect x="60" y="70" width="26" height="3" rx="1.5" fill="#25d366"/>
      <rect x="60" y="76" width="18" height="3" rx="1.5" fill="#25d366" opacity=".6"/>
    </svg>
  `,
  metas: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gMet" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#fecaca"/><stop offset="1" stop-color="#fca5a5"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gMet)"/>
      <!-- Diana -->
      <circle cx="60" cy="60" r="34" fill="#fff"/>
      <circle cx="60" cy="60" r="34" fill="none" stroke="#dc2626" stroke-width="3"/>
      <circle cx="60" cy="60" r="24" fill="none" stroke="#dc2626" stroke-width="3"/>
      <circle cx="60" cy="60" r="14" fill="none" stroke="#dc2626" stroke-width="3"/>
      <circle cx="60" cy="60" r="5" fill="#dc2626"/>
      <!-- Flecha en el centro -->
      <line x1="88" y1="32" x2="60" y2="60" stroke="#78350f" stroke-width="3" stroke-linecap="round"/>
      <path d="M60 60l-2 8 8-4z" fill="#fbbf24"/>
      <path d="M84 28l6 6-2 6-6-2z" fill="#dc2626"/>
    </svg>
  `,
  permisos: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gPmi" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#e9d5ff"/><stop offset="1" stop-color="#d8b4fe"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gPmi)"/>
      <!-- Escudo -->
      <path d="M60 20l30 10v22c0 20-14 34-30 40-16-6-30-20-30-40V30z" fill="#8b5cf6"/>
      <path d="M60 26l24 8v18c0 16-11 27-24 32-13-5-24-16-24-32V34z" fill="#a78bfa"/>
      <!-- Candado -->
      <rect x="48" y="58" width="24" height="20" rx="3" fill="#fff"/>
      <path d="M52 58v-6c0-4 4-8 8-8s8 4 8 8v6" fill="none" stroke="#fff" stroke-width="3"/>
      <circle cx="60" cy="66" r="3" fill="#8b5cf6"/>
      <rect x="59" y="68" width="2" height="6" fill="#8b5cf6"/>
      <!-- Check decorativo -->
      <circle cx="96" cy="30" r="8" fill="#10b981"/>
      <path d="M92 30l3 3 5-5" stroke="#fff" stroke-width="2" fill="none" stroke-linecap="round"/>
    </svg>
  `,
  factura: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gFac" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#dbeafe"/><stop offset="1" stop-color="#bfdbfe"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gFac)"/>
      <!-- Boleta/factura -->
      <path d="M36 18h48v84l-6-5-6 5-6-5-6 5-6-5-6 5-6-5-6 5V18z" fill="#fff" stroke="#1e293b" stroke-width="2"/>
      <!-- Lineas de texto -->
      <rect x="44" y="30" width="32" height="4" rx="2" fill="#93c5fd"/>
      <rect x="44" y="40" width="24" height="3" rx="1.5" fill="#e2e8f0"/>
      <rect x="44" y="48" width="28" height="3" rx="1.5" fill="#e2e8f0"/>
      <rect x="44" y="56" width="20" height="3" rx="1.5" fill="#e2e8f0"/>
      <!-- Sello / check -->
      <circle cx="60" cy="76" r="14" fill="#2563eb"/>
      <path d="M53 76l5 5 9-10" stroke="#fff" stroke-width="3" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
      <!-- Reloj decorativo (proximamente) -->
      <circle cx="98" cy="26" r="10" fill="#fbbf24"/>
      <path d="M98 20v6l4 3" stroke="#78350f" stroke-width="2" fill="none" stroke-linecap="round"/>
    </svg>
  `,
  sedes: `
    <svg viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="gSed" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#dcfce7"/><stop offset="1" stop-color="#bbf7d0"/>
        </linearGradient>
      </defs>
      <circle cx="60" cy="60" r="52" fill="url(#gSed)"/>
      <!-- Pin de mapa grande -->
      <path d="M60 20c14 0 24 10 24 23 0 17-24 42-24 42s-24-25-24-42c0-13 10-23 24-23z" fill="#059669"/>
      <circle cx="60" cy="43" r="10" fill="#fff"/>
      <!-- Pin pequeno secundario -->
      <path d="M92 70c7 0 12 5 12 12 0 8-12 20-12 20s-12-12-12-20c0-7 5-12 12-12z" fill="#34d399"/>
      <circle cx="92" cy="82" r="5" fill="#fff"/>
      <!-- Puntito decorativo -->
      <circle cx="24" cy="30" r="4" fill="#10b981"/>
    </svg>
  `,
};

@Component({
  selector: 'app-ajustes',
  imports: [CommonModule, RouterLink, PageHeaderComponent],
  templateUrl: './ajustes.component.html',
  styleUrl: './ajustes.component.scss'
})
export class AjustesComponent {
  ajustes: Ajuste[];

  constructor(private sanitizer: DomSanitizer) {
    this.ajustes = [
      { nombre: 'Configuración del negocio', descripcion: 'Nombre, logo, colores, RUC, dirección, horario', icono: this.svg('tienda'), ruta: '/ajustes/negocio' },
      { nombre: 'Sedes', descripcion: 'Administra tus sucursales: cada una con su propia caja, pedidos e inventario', icono: this.svg('sedes'), ruta: '/ajustes/sedes' },
      { nombre: 'Usuarios', descripcion: 'Agrega, actualiza o desactiva usuarios del sistema', icono: this.svg('usuario'), ruta: '/ajustes/usuarios' },
      { nombre: 'Permisos y accesos', descripcion: 'Elige qué módulos ve cada rol de acceso (Administrador, Coordinador, Trabajador)', icono: this.svg('permisos'), ruta: '/ajustes/permisos' },
      { nombre: 'Personal', descripcion: 'Visualiza, registra o elimina empleados', icono: this.svg('personal'), ruta: '/ajustes/personal' },
      { nombre: 'Cargos del personal', descripcion: 'Etiquetas de puesto para tu personal (ej: Lavandero, Cajero). No dan acceso al sistema', icono: this.svg('personal'), ruta: '/ajustes/roles-personal' },
      { nombre: 'Áreas de lavado', descripcion: 'Define las etapas del proceso y sus tiempos estándar', icono: this.svg('lavado'), ruta: '/ajustes/areas' },
      { nombre: 'Repartidores', descripcion: 'Administra a los motorizados que reparten los pedidos delivery', icono: this.svg('sedes'), ruta: '/ajustes/motorizados' },
      { nombre: 'Servicios y precios', descripcion: 'Registra, edita, activa/desactiva servicios del catálogo', icono: this.svg('servicios'), ruta: '/ajustes/servicios' },
      { nombre: 'Categorías', descripcion: 'Registra categorías para los servicios', icono: this.svg('categorias'), ruta: '/ajustes/categorias' },
      { nombre: 'Tipos de gasto', descripcion: 'Registra gastos recurrentes', icono: this.svg('gastos'), ruta: '/ajustes/tipos-gasto' },
      { nombre: 'Puntos y descuentos', descripcion: 'Fidelización por puntos (ganar y canjear) y tope de descuentos del personal', icono: this.svg('puntos'), ruta: '/ajustes/puntos' },
      { nombre: 'Ajuste de Cargo Extra (Impuesto)', descripcion: 'Actualiza el porcentaje de IGV', icono: this.svg('gastos'), ruta: '/ajustes/cargo-extra' },
      { nombre: 'Ajuste de Metas', descripcion: 'Actualiza el valor de la meta mensual de pedidos', icono: this.svg('metas'), ruta: '/ajustes/metas' },
      { nombre: 'Plantillas de WhatsApp', descripcion: 'Mensajes automáticos por etapa del proceso', icono: this.svg('whatsapp'), ruta: '/ajustes/plantillas-whatsapp' },
      { nombre: 'Facturación Electrónica', descripcion: 'Emite boletas y facturas electrónicas ante SUNAT directamente desde el sistema', icono: this.svg('factura'), ruta: '/ajustes/facturacion-electronica' },
      { nombre: 'Comprobantes emitidos', descripcion: 'Historial de boletas y facturas electrónicas emitidas', icono: this.svg('factura'), ruta: '/facturacion/comprobantes' },
      { nombre: 'Pagos online', descripcion: 'Prepara Izipay para cobrar de forma segura desde el portal de seguimiento', icono: this.svg('gastos'), ruta: '/ajustes/pagos' },
    ];
  }

  private svg(key: string): SafeHtml {
    return this.sanitizer.bypassSecurityTrustHtml(ILUSTRACIONES[key] ?? '');
  }
}
