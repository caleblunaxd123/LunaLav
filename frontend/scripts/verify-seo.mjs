import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const html = readFileSync(new URL('../src/index.html', import.meta.url), 'utf8');
const blocks = [...html.matchAll(/<script\b[^>]*\btype=["']application\/ld\+json["'][^>]*>([\s\S]*?)<\/script>/gi)];

assert.equal(blocks.length, 1, 'index.html debe contener un solo bloque JSON-LD');

const data = JSON.parse(blocks[0][1]);
const entries = data['@graph'] ?? [data];
const application = entries.find((entry) => entry['@type'] === 'SoftwareApplication');

assert.ok(application, 'Falta SoftwareApplication en el JSON-LD');
assert.deepEqual(
  application.offers.map((offer) => Number(offer.price)).sort((a, b) => a - b),
  [20, 50, 80]
);
assert.ok(application.offers.every((offer) => offer.priceCurrency === 'PEN'));

console.log('JSON-LD válido: SoftwareApplication con ofertas de 20, 50 y 80 PEN.');
