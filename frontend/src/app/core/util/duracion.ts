/**
 * Formatea una duración (en minutos) a la unidad MÁS LEGIBLE según su tamaño:
 *   < 1 h   → "45 min"
 *   < 1 día → "6 h"
 *   < 1 sem → "3 días"
 *   < 1 mes → "2 semanas"
 *   resto   → "3 meses"
 *
 * Pensado para tiempos largos (ej. pedidos estancados): en vez de "211 h 9 min"
 * muestra "8 días", que es lo que el operador entiende de un vistazo.
 */
export function formatearDuracion(minutos: number): string {
  if (!Number.isFinite(minutos) || minutos < 0) minutos = 0;
  minutos = Math.round(minutos);

  if (minutos < 60) return `${minutos} min`;

  const horas = Math.floor(minutos / 60);
  if (horas < 24) return `${horas} h`;

  const dias = Math.floor(horas / 24);
  if (dias < 7) return `${dias} día${dias === 1 ? '' : 's'}`;

  if (dias < 30) {
    const semanas = Math.floor(dias / 7);
    return `${semanas} semana${semanas === 1 ? '' : 's'}`;
  }

  const meses = Math.floor(dias / 30);
  return `${meses} mes${meses === 1 ? '' : 'es'}`;
}
