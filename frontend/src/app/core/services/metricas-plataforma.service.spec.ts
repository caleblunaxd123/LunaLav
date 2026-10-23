import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { MetricasPlataformaService } from './metricas-plataforma.service';

describe('MetricasPlataformaService', () => {
  let servicio: MetricasPlataformaService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    servicio = TestBed.inject(MetricasPlataformaService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('consulta las métricas protegidas de plataforma', () => {
    let respuesta: unknown;
    servicio.obtener().subscribe(r => respuesta = r);
    const req = http.expectOne(`${environment.apiUrl}/plataforma/metricas`);
    expect(req.request.method).toBe('GET');
    req.flush({ negociosReales: 2, embudo: [], negocios: [], negociosExcluidos: [] });
    expect(respuesta).toEqual(jasmine.objectContaining({ negociosReales: 2 }));
  });
});
