import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { MetricasPlataformaService } from '../../core/services/metricas-plataforma.service';
import { PlataformaMetricasComponent } from './plataforma-metricas.component';

describe('PlataformaMetricasComponent', () => {
  const datos = {
    embudo: [{ codigo: 'inicio_registro', total: 3 }, { codigo: 'primer_pago', total: 1 }],
    excluidos: 1, registrosConfirmadosAutonomos: 1, suscripcionesTest: 0,
    negociosReales: 2, onboardingCompleto: 1, primerPedido: 1,
    activosEnPrueba: 1, pagando: 1, mrr: 99, conversionPruebaPago: 50,
    negocios: [{ slug: 'lav-real', fechaCreacion: '2026-10-01', plan: 'PRO', estado: 'ACTIVA',
      diasRestantesPrueba: null, primerLogin: true, pedidosTotales: 4,
      ultimoPedidoFecha: '2026-10-20', motivoAbandono: 'convertido' }],
    negociosExcluidos: [{ slug: 'qa-interno', motivo: 'qa' }]
  };

  it('muestra embudo, indicadores y razones sin correos', async () => {
    await TestBed.configureTestingModule({
      imports: [PlataformaMetricasComponent],
      providers: [{ provide: MetricasPlataformaService, useValue: { obtener: () => of(datos) } }]
    }).compileComponents();
    const fixture = TestBed.createComponent(PlataformaMetricasComponent);
    fixture.detectChanges();
    const texto = fixture.nativeElement.textContent as string;
    expect(texto).toContain('Inicio de registro');
    expect(texto).toContain('S/ 99.00');
    expect(texto).toContain('lav-real');
    expect(texto).toContain('Convertido');
    expect(texto).toContain('qa-interno');
    expect(texto).toContain('QA');
    expect(texto).not.toContain('@');
  });

  it('permite reintentar tras un error', async () => {
    let falla = true;
    await TestBed.configureTestingModule({
      imports: [PlataformaMetricasComponent],
      providers: [{ provide: MetricasPlataformaService, useValue: {
        obtener: () => falla ? throwError(() => new Error('falló')) : of(datos)
      } }]
    }).compileComponents();
    const fixture = TestBed.createComponent(PlataformaMetricasComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    falla = false;
    fixture.componentInstance.cargar();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('lav-real');
  });
});
