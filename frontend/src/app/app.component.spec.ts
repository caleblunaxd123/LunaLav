import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { AppComponent } from './app.component';

describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideHttpClient(), provideRouter([])],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the global toaster and router outlet', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).not.toBeNull();
    expect(compiled.querySelector('app-toaster')).not.toBeNull();
  });

  it('normalizes tenant routes even if the tenant context changed during navigation', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance as unknown as { quitarSlug(url: string): string };

    expect(app.quitarSlug('/demo/seleccionar-sede')).toBe('/seleccionar-sede');
    expect(app.quitarSlug('/demo/ticket/123?interno=1')).toBe('/ticket/123?interno=1');
    expect(app.quitarSlug('/seguimiento/token-publico')).toBe('/seguimiento/token-publico');
  });
});
