import { TestBed } from '@angular/core/testing';
import { ActualizacionDatosService } from './actualizacion-datos.service';

describe('ActualizacionDatosService', () => {
  it('notifica únicamente a los módulos relacionados', () => {
    const service = TestBed.inject(ActualizacionDatosService);
    let pedidos = 0;
    let inventario = 0;

    const subPedidos = service.cambios('pedidos').subscribe(() => pedidos++);
    const subInventario = service.cambios('inventario').subscribe(() => inventario++);

    service.notificar(['pedidos', 'dashboard']);

    expect(pedidos).toBe(1);
    expect(inventario).toBe(0);
    subPedidos.unsubscribe();
    subInventario.unsubscribe();
  });

  it('trata una actualización general como invalidación de todos los módulos', () => {
    const service = TestBed.inject(ActualizacionDatosService);
    let recibidas = 0;
    const sub = service.cambios('caja').subscribe(() => recibidas++);

    service.notificar(['datos']);

    expect(recibidas).toBe(1);
    sub.unsubscribe();
  });
});
