/**
 * Lista de países con su código telefónico (dial code) para el selector de teléfono.
 * Formato compacto: [nombre, ISO-2, dial]. La bandera se calcula del ISO-2 en runtime.
 * Perú va primero (es el país por defecto del sistema).
 */
export interface Pais {
  nombre: string;
  iso: string;   // ISO-3166 alpha-2, en mayúsculas
  dial: string;  // código telefónico sin el '+'
}

// [nombre, iso2, dial]
const DATA: [string, string, string][] = [
  ['Perú', 'PE', '51'],
  ['Argentina', 'AR', '54'],
  ['Bolivia', 'BO', '591'],
  ['Brasil', 'BR', '55'],
  ['Chile', 'CL', '56'],
  ['Colombia', 'CO', '57'],
  ['Costa Rica', 'CR', '506'],
  ['Cuba', 'CU', '53'],
  ['Ecuador', 'EC', '593'],
  ['El Salvador', 'SV', '503'],
  ['España', 'ES', '34'],
  ['Estados Unidos', 'US', '1'],
  ['Guatemala', 'GT', '502'],
  ['Honduras', 'HN', '504'],
  ['México', 'MX', '52'],
  ['Nicaragua', 'NI', '505'],
  ['Panamá', 'PA', '507'],
  ['Paraguay', 'PY', '595'],
  ['Puerto Rico', 'PR', '1'],
  ['República Dominicana', 'DO', '1'],
  ['Uruguay', 'UY', '598'],
  ['Venezuela', 'VE', '58'],
  ['Canadá', 'CA', '1'],
  ['Alemania', 'DE', '49'],
  ['Francia', 'FR', '33'],
  ['Italia', 'IT', '39'],
  ['Portugal', 'PT', '351'],
  ['Reino Unido', 'GB', '44'],
  ['Países Bajos', 'NL', '31'],
  ['Bélgica', 'BE', '32'],
  ['Suiza', 'CH', '41'],
  ['Austria', 'AT', '43'],
  ['Suecia', 'SE', '46'],
  ['Noruega', 'NO', '47'],
  ['Dinamarca', 'DK', '45'],
  ['Finlandia', 'FI', '358'],
  ['Irlanda', 'IE', '353'],
  ['Polonia', 'PL', '48'],
  ['Rusia', 'RU', '7'],
  ['Ucrania', 'UA', '380'],
  ['Rumania', 'RO', '40'],
  ['Grecia', 'GR', '30'],
  ['Turquía', 'TR', '90'],
  ['China', 'CN', '86'],
  ['Japón', 'JP', '81'],
  ['Corea del Sur', 'KR', '82'],
  ['India', 'IN', '91'],
  ['Indonesia', 'ID', '62'],
  ['Filipinas', 'PH', '63'],
  ['Tailandia', 'TH', '66'],
  ['Vietnam', 'VN', '84'],
  ['Malasia', 'MY', '60'],
  ['Singapur', 'SG', '65'],
  ['Australia', 'AU', '61'],
  ['Nueva Zelanda', 'NZ', '64'],
  ['Sudáfrica', 'ZA', '27'],
  ['Egipto', 'EG', '20'],
  ['Marruecos', 'MA', '212'],
  ['Nigeria', 'NG', '234'],
  ['Israel', 'IL', '972'],
  ['Arabia Saudita', 'SA', '966'],
  ['Emiratos Árabes Unidos', 'AE', '971'],
  ['Qatar', 'QA', '974'],
  ['Líbano', 'LB', '961'],
  ['Andorra', 'AD', '376'],
  ['Albania', 'AL', '355'],
  ['Argelia', 'DZ', '213'],
  ['Angola', 'AO', '244'],
  ['Armenia', 'AM', '374'],
  ['Azerbaiyán', 'AZ', '994'],
  ['Bangladés', 'BD', '880'],
  ['Bielorrusia', 'BY', '375'],
  ['Bulgaria', 'BG', '359'],
  ['Camboya', 'KH', '855'],
  ['Camerún', 'CM', '237'],
  ['Croacia', 'HR', '385'],
  ['Chipre', 'CY', '357'],
  ['Chequia', 'CZ', '420'],
  ['Estonia', 'EE', '372'],
  ['Etiopía', 'ET', '251'],
  ['Georgia', 'GE', '995'],
  ['Ghana', 'GH', '233'],
  ['Hungría', 'HU', '36'],
  ['Islandia', 'IS', '354'],
  ['Irán', 'IR', '98'],
  ['Irak', 'IQ', '964'],
  ['Jamaica', 'JM', '1'],
  ['Jordania', 'JO', '962'],
  ['Kazajistán', 'KZ', '7'],
  ['Kenia', 'KE', '254'],
  ['Kuwait', 'KW', '965'],
  ['Letonia', 'LV', '371'],
  ['Lituania', 'LT', '370'],
  ['Luxemburgo', 'LU', '352'],
  ['Macedonia del Norte', 'MK', '389'],
  ['Malta', 'MT', '356'],
  ['Moldavia', 'MD', '373'],
  ['Mónaco', 'MC', '377'],
  ['Montenegro', 'ME', '382'],
  ['Nepal', 'NP', '977'],
  ['Omán', 'OM', '968'],
  ['Pakistán', 'PK', '92'],
  ['Palestina', 'PS', '970'],
  ['Serbia', 'RS', '381'],
  ['Eslovaquia', 'SK', '421'],
  ['Eslovenia', 'SI', '386'],
  ['Sri Lanka', 'LK', '94'],
  ['Sudán', 'SD', '249'],
  ['Siria', 'SY', '963'],
  ['Taiwán', 'TW', '886'],
  ['Tanzania', 'TZ', '255'],
  ['Túnez', 'TN', '216'],
  ['Uganda', 'UG', '256'],
  ['Uzbekistán', 'UZ', '998'],
  ['Yemen', 'YE', '967'],
  ['Zambia', 'ZM', '260'],
  ['Zimbabue', 'ZW', '263'],
  ['Belice', 'BZ', '501'],
  ['Guyana', 'GY', '592'],
  ['Surinam', 'SR', '597'],
  ['Trinidad y Tobago', 'TT', '1'],
  ['Haití', 'HT', '509'],
  ['Bahamas', 'BS', '1'],
  ['Barbados', 'BB', '1'],
];

/** Quita el sufijo interno (p.ej. 'QA2') que solo evita ISO duplicados en la data. */
function isoLimpio(iso: string): string {
  return iso.replace(/[0-9]/g, '');
}

/** Bandera emoji a partir del ISO-2 (usa símbolos indicadores regionales). */
export function banderaDe(iso: string): string {
  const code = isoLimpio(iso).toUpperCase();
  if (code.length !== 2) return '🏳️';
  const base = 0x1f1e6;
  return String.fromCodePoint(base + (code.charCodeAt(0) - 65), base + (code.charCodeAt(1) - 65));
}

export const PAISES: Pais[] = DATA.map(([nombre, iso, dial]) => ({ nombre, iso: isoLimpio(iso), dial }));

export const PERU: Pais = PAISES[0];

/**
 * Dado un número en E.164 sin '+' (solo dígitos con código de país), devuelve el país cuyo
 * dial code calza con el prefijo más largo. Útil para reconstruir el país al editar.
 */
export function paisPorPrefijo(digitosConCodigo: string): Pais | null {
  let mejor: Pais | null = null;
  for (const p of PAISES) {
    if (digitosConCodigo.startsWith(p.dial)) {
      if (!mejor || p.dial.length > mejor.dial.length) mejor = p;
    }
  }
  return mejor;
}
