import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/services/auth.service';
import { EmpezarComponent } from './empezar.component';

describe('EmpezarComponent', () => {
  let component: EmpezarComponent;
  let fixture: ReturnType<typeof TestBed.createComponent<EmpezarComponent>>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EmpezarComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ plan: 'FACTURA' }) } } }
      ]
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EmpezarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('elige inicialmente el plan del parámetro ?plan=', () => {
    expect(component.form.plan).toBe('FACTURA');
    const elegido = fixture.nativeElement.querySelector('input[name="plan"]:checked') as HTMLInputElement;
    expect(elegido.closest('label')?.textContent).toContain('Factura');
  });

  it('no habilita "Crear mi lavandería" con un código no numérico', () => {
    component.paso.set('codigo');
    component.form.codigo = 'abcdef';
    fixture.detectChanges();
    expect(component.codigoValido()).toBeFalse();
    const crear = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(crear.disabled).toBeTrue();
    component.crearLavanderia();
    http.expectNone(`${environment.apiUrl}/registro/prueba`);
  });

  it('rechaza en el formulario los valores más largos que los límites de la API', () => {
    component.form = {
      ...component.form,
      nombreResponsable: 'a'.repeat(121), email: 'ana@example.com', celular: '987654321',
      usuario: 'ana', password: 'x'.repeat(101), aceptaTerminos: true
    };
    const errores = component.erroresCuenta();
    expect(errores.some(e => e.includes('120'))).toBeTrue();
    expect(errores.some(e => e.includes('100'))).toBeTrue();
    component.enviarCodigo();
    http.expectNone(`${environment.apiUrl}/registro/codigo`);
  });

  it('ignora una respuesta de disponibilidad que llega para un código anterior', fakeAsync(() => {
    component.form.slug = 'lavanderia-uno';
    component.alCambiarSlug();
    tick(350);
    const vieja = http.expectOne(r => r.url === `${environment.apiUrl}/registro/disponible`);
    component.form.slug = 'lavanderia-dos';
    component.alCambiarSlug();
    vieja.flush({ disponible: true });
    expect(component.disponibilidad()).toBeNull();
    tick(350);
    http.expectOne(r => r.url === `${environment.apiUrl}/registro/disponible` && r.params.get('slug') === 'lavanderia-dos')
      .flush({ disponible: false, mensaje: 'Ese código de empresa ya está en uso.' });
    expect(component.disponibilidad()?.estado).toBe('ocupado');
  }));

  it('muestra la fecha de fin legible y la dirección completa en el mismo origen fuera de lunalav.pe', () => {
    component.resultado.set({ negocioId: 9, slug: 'lav-x', pruebaHasta: '2026-11-06', diasPrueba: 14 });
    expect(component.fechaFinTexto()).toContain('noviembre');
    expect(component.fechaFinTexto()).toContain('2026');
    expect(component.fechaFinTexto()).toMatch(/^6 /);
    expect(component.urlIngreso()).toBe(`${window.location.origin}/lav-x/login`);
    expect(component.urlLogin()).toBe(`${window.location.origin}/login`);
  });

  it('con una sesión de otra lavandería abierta, "Entrar" la cierra y navega al login de la nueva', () => {
    const auth = TestBed.inject(AuthService);
    spyOn(auth, 'autenticado').and.returnValue(true);
    const cerrar = spyOn(auth, 'cerrarSesion');
    const navegar = spyOn(component, 'navegar');
    component.resultado.set({ negocioId: 9, slug: 'lav-nueva', pruebaHasta: '2026-11-06', diasPrueba: 14 });
    const evento = new MouseEvent('click', { cancelable: true });
    component.entrar(evento);
    expect(cerrar).toHaveBeenCalled();
    expect(evento.defaultPrevented).toBeTrue();
    expect(navegar).toHaveBeenCalledWith(`${window.location.origin}/lav-nueva/login`);
  });

  it('sin sesión abierta, "Entrar" deja que el enlace funcione normalmente', () => {
    const auth = TestBed.inject(AuthService);
    const cerrar = spyOn(auth, 'cerrarSesion');
    const evento = new MouseEvent('click', { cancelable: true });
    component.entrar(evento);
    expect(cerrar).not.toHaveBeenCalled();
    expect(evento.defaultPrevented).toBeFalse();
  });

  it('no continúa con un slug inválido', () => {
    component.form.nombreNegocio = 'Lavandería Demo';
    component.form.slug = 'Slug inválido';
    component.irACuenta();
    fixture.detectChanges();

    expect(component.paso()).toBe('negocio');
    expect((fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('envía el código, registra el payload completo y muestra listo', () => {
    component.form = {
      nombreNegocio: '  Lavandería Demo  ', slug: 'lavanderia-demo', plan: 'FACTURA',
      sedeNombre: '  ', nombreResponsable: '  Ana Pérez  ', email: '  ana@example.com  ',
      celular: '  987654321  ', usuario: '  ANA  ', password: 'secreto123',
      aceptaTerminos: true, codigo: ' 123456 '
    };
    component.disponibilidad.set({ estado: 'disponible' });

    component.irACuenta();
    expect(component.paso()).toBe('cuenta');
    component.enviarCodigo();
    const codigo = http.expectOne(`${environment.apiUrl}/registro/codigo`);
    expect(codigo.request.method).toBe('POST');
    expect(codigo.request.body).toEqual({ email: 'ana@example.com' });
    codigo.flush({ enviado: true, minutosVigencia: 10 });
    expect(component.paso()).toBe('codigo');
    component.crearLavanderia();

    const prueba = http.expectOne(`${environment.apiUrl}/registro/prueba`);
    expect(prueba.request.method).toBe('POST');
    expect(prueba.request.body).toEqual({
      nombreNegocio: 'Lavandería Demo', slug: 'lavanderia-demo', nombreResponsable: 'Ana Pérez',
      email: 'ana@example.com', celular: '987654321', usuario: 'ana', password: 'secreto123',
      plan: 'FACTURA', sedeNombre: 'Principal', aceptaTerminos: true, codigoVerificacion: '123456'
    });
    prueba.flush({ negocioId: 42, slug: 'lavanderia-demo', pruebaHasta: '2026-11-06', diasPrueba: 14 });
    expect(component.paso()).toBe('listo');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.listo h2')?.textContent)
      .toContain('Lavandería Demo');
  });

  it('muestra el mensaje de límite de intentos ante un 429', () => {
    component.form.nombreNegocio = 'Lavandería Demo';
    component.form.slug = 'lavanderia-demo';
    component.form.nombreResponsable = 'Ana Pérez';
    component.form.email = 'ana@example.com';
    component.form.celular = '987654321';
    component.form.usuario = 'ana';
    component.form.password = 'secreto123';
    component.form.aceptaTerminos = true;
    component.irACuenta();
    component.enviarCodigo();
    http.expectOne(`${environment.apiUrl}/registro/codigo`)
      .flush({ mensaje: 'Too many requests' }, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    expect(component.paso()).toBe('cuenta');
    expect(component.enviando()).toBeFalse();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent)
      .toContain('Hiciste muchos intentos seguidos.');
  });
});
