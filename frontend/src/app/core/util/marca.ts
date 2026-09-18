// ============================================================
// Marca del PRODUCTO (el SaaS en sí), NO de una lavandería.
// ------------------------------------------------------------
// "LunaLav" es el nombre comercial del sistema que se ofrece
// a las lavanderías. Cada lavandería (tenant) tiene su propia
// marca (nombre/logo/colores) que se muestra dentro de /{slug}/...
//
// Este nombre aparece en el login neutral (/login, acceso del
// propietario), el panel de plataforma y el título del navegador.
//
// 👉 Si algún día se decide otro nombre comercial, se cambia
//    SOLO aquí y se actualiza en toda la app.
// ============================================================

/** Nombre comercial del producto SaaS. */
export const PRODUCTO_NOMBRE = 'LunaLav';

/** Bajada / descripción corta del producto. */
export const PRODUCTO_TAGLINE = 'Tu lavandería, clara y en movimiento';

/** Logo completo a color (para fondos claros). */
export const PRODUCTO_LOGO = 'lunalav-logo-color.svg?v=brand-20260918';
/** Logo completo en blanco (para fondos oscuros: login, plataforma). */
export const PRODUCTO_LOGO_BLANCO = 'lunalav-logo-white.svg?v=brand-20260918';
/** Solo la marca (nube) en blanco, para encabezados compactos. */
export const PRODUCTO_ICONO_BLANCO = 'lunalav-icon.svg?v=brand-20260918';
/** Marca (nube+plancha) a color adaptada para fondo navy: plancha blanca, nube cian/teal. */
export const PRODUCTO_MARCA_NAVY = 'lunalav-icon.svg?v=brand-20260918';
/** Logo OFICIAL completo (marca + wordmark + bajada) del archivo de marca, sobre fondo navy. */
export const PRODUCTO_LOGO_LOGIN = 'lunalav-logo-white.svg?v=brand-20260918';
/** Logo OFICIAL a color (para fondos claros: panel izquierdo blanco del login). */
export const PRODUCTO_LOGO_COLOR = 'lunalav-logo-color.svg?v=brand-20260918';

/** Crédito del desarrollador (aparece en el login y en el pie del sidebar). */
export const DESARROLLADOR_CREDITO = 'Desarrollado por Caleb Luna · LunaIT Solution';
