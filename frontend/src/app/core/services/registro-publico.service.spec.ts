import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import {
  DisponibilidadSlug,
  RegistroPublicoService,
  planDesdeQuery,
  sugerirSlug
} from './registro-publico.service';

describe('RegistroPublicoService', () => {
  let service: RegistroPublicoService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(RegistroPublicoService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sugiere slugs sin acentos ni símbolos y limita el largo a 50 caracteres', () => {
    expect(sugerirSlug('  Lavandería Doña Memo & Más!  ')).toBe('lavanderia-dona-memo-mas');
    expect(sugerirSlug('$$$')).toBe('');
    expect(sugerirSlug(`${'a'.repeat(49)}-resto`)).toBe('a'.repeat(49));
    expect(sugerirSlug('a'.repeat(60))).toBe('a'.repeat(50));
  });

  it('lee los planes comerciales de la query y usa BASICO para valores desconocidos', () => {
    expect(planDesdeQuery('factura')).toBe('FACTURA');
    expect(planDesdeQuery('MULTISEDE')).toBe('MULTISEDE');
    expect(planDesdeQuery('basico')).toBe('BASICO');
    expect(planDesdeQuery('otro')).toBe('BASICO');
    expect(planDesdeQuery(null)).toBe('BASICO');
  });

  it('mapea la respuesta disponible', () => {
    let estado: DisponibilidadSlug | undefined;
    service.disponible('lavanderia-demo').subscribe(r => estado = r);

    const request = http.expectOne(r => r.url === `${environment.apiUrl}/registro/disponible`
      && r.params.get('slug') === 'lavanderia-demo');
    expect(request.request.method).toBe('GET');
    request.flush({ disponible: true });
    expect(estado).toEqual({ estado: 'disponible' });
  });

  it('mapea la respuesta ocupada y el mensaje por defecto', () => {
    let estado: DisponibilidadSlug | undefined;
    service.disponible('ocupado').subscribe(r => estado = r);
    http.expectOne(`${environment.apiUrl}/registro/disponible?slug=ocupado`)
      .flush({ disponible: false, mensaje: 'Reservado' });
    expect(estado).toEqual({ estado: 'ocupado', mensaje: 'Reservado' });

    service.disponible('otro').subscribe(r => estado = r);
    http.expectOne(`${environment.apiUrl}/registro/disponible?slug=otro`)
      .flush({ disponible: false });
    expect(estado).toEqual({ estado: 'ocupado', mensaje: 'Ese código ya está en uso.' });
  });

  it('devuelve desconocido si falla la consulta HTTP', () => {
    let estado: DisponibilidadSlug | undefined;
    service.disponible('lavanderia-demo').subscribe(r => estado = r);
    http.expectOne(`${environment.apiUrl}/registro/disponible?slug=lavanderia-demo`)
      .flush({ mensaje: 'Error' }, { status: 503, statusText: 'Service Unavailable' });
    expect(estado).toEqual({ estado: 'desconocido' });
  });
});
