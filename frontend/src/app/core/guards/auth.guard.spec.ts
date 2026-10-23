import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { guestGuard } from './auth.guard';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';

describe('guestGuard', () => {
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    auth = TestBed.inject(AuthService);
  });

  const correr = (query: Record<string, string>) => TestBed.runInInjectionContext(() =>
    guestGuard({ queryParamMap: convertToParamMap(query) } as ActivatedRouteSnapshot, {} as RouterStateSnapshot));

  it('sin sesión deja ver el login', () => {
    expect(correr({})).toBeTrue();
  });

  it('con sesión de otra lavandería y cuentaNueva=1 cierra esa sesión y deja ver el login', () => {
    spyOn(auth, 'autenticado').and.returnValue(true);
    const cerrar = spyOn(auth, 'cerrarSesion');
    expect(correr({ cuentaNueva: '1' })).toBeTrue();
    expect(cerrar).toHaveBeenCalled();
  });

  it('con sesión y sin cuentaNueva redirige al panel (comportamiento anterior)', () => {
    spyOn(auth, 'autenticado').and.returnValue(true);
    const cerrar = spyOn(auth, 'cerrarSesion');
    const resultado = correr({});
    expect(resultado instanceof UrlTree).toBeTrue();
    expect(TestBed.inject(Router).serializeUrl(resultado as UrlTree)).toContain('seleccionar-sede');
    expect(cerrar).not.toHaveBeenCalled();
  });
});
