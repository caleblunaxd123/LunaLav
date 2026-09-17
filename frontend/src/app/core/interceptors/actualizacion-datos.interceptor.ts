import { HttpEvent, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';
import { ActualizacionDatosService, CanalActualizacion } from '../services/actualizacion-datos.service';

export const actualizacionDatosInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method === 'GET' || req.method === 'HEAD' || req.method === 'OPTIONS') return next(req);

  const actualizaciones = inject(ActualizacionDatosService);
  const canales = canalesPara(req.url);
  return next(req).pipe(tap((evento: HttpEvent<unknown>) => {
    if (evento instanceof HttpResponse && evento.ok && canales.length > 0) {
      actualizaciones.notificar(canales);
    }
  }));
};

function canalesPara(url: string): CanalActualizacion[] {
  const ruta = url.toLowerCase();
  if (ruta.includes('/auth/')) return [];
  if (ruta.includes('/repartidor/')) return ['reparto', 'pedidos', 'dashboard'];
  if (ruta.includes('/pago-publico/')) return ['pedidos', 'dashboard', 'caja', 'clientes'];
  if (ruta.includes('/pedidos')) return ['pedidos', 'dashboard', 'caja', 'clientes', 'facturacion'];
  if (ruta.includes('/clientes')) return ['clientes', 'dashboard'];
  if (ruta.includes('/insumos')) return ['inventario', 'dashboard', 'caja'];
  if (ruta.includes('/caja')) return ['caja', 'dashboard'];
  if (ruta.includes('/facturacion')) return ['facturacion', 'dashboard'];
  if (ruta.includes('/negocios')) return ['plataforma'];
  if (ruta.includes('/servicios') || ruta.includes('/categorias') || ruta.includes('/areas-lavado') ||
      ruta.includes('/sedes') || ruta.includes('/motorizados') || ruta.includes('/personal') ||
      ruta.includes('/roles-personal') || ruta.includes('/tipos-gasto')) {
    return ['catalogos', 'datos'];
  }
  return ['datos'];
}
