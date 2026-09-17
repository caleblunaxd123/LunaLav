/**
 * Fechas en la zona horaria del equipo, no en UTC.
 *
 * `new Date().toISOString()` devuelve la fecha en UTC. En Perú (UTC-5) eso adelanta
 * el día a partir de las 7:00 pm: a las 8 pm del 31 de agosto, `toISOString()` ya dice
 * "1 de septiembre". Usado para filtros de "hoy" o "este mes", hacía que las pantallas
 * abrieran en un día (o un mes) sin datos justo en el horario de cierre de la lavandería.
 */

/** 'YYYY-MM-DD' del día local (para <input type="date"> y filtros por día). */
export function fechaLocalIso(fecha: Date = new Date()): string {
  const anio = fecha.getFullYear();
  const mes = String(fecha.getMonth() + 1).padStart(2, '0');
  const dia = String(fecha.getDate()).padStart(2, '0');
  return `${anio}-${mes}-${dia}`;
}

/** 'YYYY-MM' del mes local (para <input type="month">). */
export function mesLocalIso(fecha: Date = new Date()): string {
  return fechaLocalIso(fecha).slice(0, 7);
}
