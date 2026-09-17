import { TestBed } from '@angular/core/testing';
import { TenantContextService } from '../services/tenant-context.service';
import { TenantUrlSerializer } from './tenant-url-serializer';

describe('TenantUrlSerializer', () => {
  let serializer: TenantUrlSerializer;
  let tenant: TenantContextService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [TenantContextService] });
    tenant = TestBed.inject(TenantContextService);
    serializer = TestBed.runInInjectionContext(() => new TenantUrlSerializer());
  });

  it('conserva las rutas legales fuera del espacio de nombres de empresas', () => {
    expect(serializer.serialize(serializer.parse('/terminos/'))).toBe('/terminos/');
    expect(tenant.slug()).toBeNull();

    expect(serializer.serialize(serializer.parse('/privacidad/'))).toBe('/privacidad/');
    expect(tenant.slug()).toBeNull();
  });

  it('sigue interpretando el primer segmento desconocido como empresa', () => {
    expect(serializer.serialize(serializer.parse('/lavanderia-demo/login'))).toBe('/lavanderia-demo/login');
    expect(tenant.slug()).toBe('lavanderia-demo');
  });
});
