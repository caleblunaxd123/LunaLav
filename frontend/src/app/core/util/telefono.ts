/** Celular nacional (Perú): solo dígitos, sin exigir una cantidad fija. */
export const CELULAR_PERU = /^\d{4,20}$/;

/**
 * Número internacional: '+' seguido del código de país y el número. Se acepta cualquier
 * longitud razonable (4 a 20 dígitos). Ej: +573001234567 (Colombia), +61437622958 (Australia).
 */
export const CELULAR_INTERNACIONAL = /^\+\d{4,20}$/;

/** True si el número tiene formato válido: Perú (9 dígitos) o internacional (+código...). */
function formatoValido(c: string): boolean {
  return CELULAR_PERU.test(c) || CELULAR_INTERNACIONAL.test(c);
}

/** True si el celular está vacío (opcional) o tiene un formato válido (Perú o internacional). */
export function esCelularValido(cel: string | null | undefined): boolean {
  const c = (cel ?? '').trim();
  return c.length === 0 || formatoValido(c);
}

/** True solo si es un celular válido y no vacío (para campos obligatorios). */
export function esCelularObligatorioValido(cel: string | null | undefined): boolean {
  return formatoValido((cel ?? '').trim());
}

/**
 * Devuelve el número listo para el enlace de WhatsApp (solo dígitos, con código de país):
 *  - Si viene con '+', ya trae el código de país → se usa tal cual.
 *  - Si ya empieza con 51 y es largo, se respeta (compatibilidad).
 *  - En cualquier otro caso se asume Perú y se antepone 51.
 */
export function numeroWhatsapp(cel: string | null | undefined): string {
  const raw = (cel ?? '').trim();
  const d = raw.replace(/\D/g, '');
  if (!d) return '';
  if (raw.startsWith('+')) return d;
  if (d.startsWith('51') && d.length >= 11) return d;
  return '51' + d;
}
