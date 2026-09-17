import { Routes } from '@angular/router';
import { authGuard, guestGuard, moduloGuard, rolGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'plataforma',
    canActivate: [authGuard, rolGuard(['PROPIETARIO'])],
    children: [
      {
        path: '',
        loadComponent: () => import('./pages/plataforma-negocios/plataforma-negocios.component').then(m => m.PlataformaNegociosComponent)
      },
      {
        path: 'nueva',
        loadComponent: () => import('./pages/plataforma-negocio-crear/plataforma-negocio-crear.component').then(m => m.PlataformaNegocioCrearComponent)
      },
      {
        path: 'empresa/:id',
        loadComponent: () => import('./pages/plataforma-negocio-detalle/plataforma-negocio-detalle.component').then(m => m.PlataformaNegocioDetalleComponent)
      },
      {
        path: 'ajustes',
        loadComponent: () => import('./pages/plataforma-ajustes/plataforma-ajustes.component').then(m => m.PlataformaAjustesComponent)
      }
    ]
  },
  {
    // Recibo imprimible de un pago de suscripción (pantalla completa, sin nav).
    path: 'recibo-suscripcion/:negocioId/:pagoId',
    canActivate: [authGuard, rolGuard(['PROPIETARIO'])],
    loadComponent: () => import('./pages/plataforma-recibo/plataforma-recibo.component').then(m => m.PlataformaReciboComponent)
  },
  {
    path: 'ticket/:id',
    canActivate: [authGuard, moduloGuard('PEDIDOS')],
    loadComponent: () => import('./pages/ticket/ticket.component').then(m => m.TicketComponent)
  },
  {
    path: 'cuadre-caja/imprimir/:id',
    canActivate: [authGuard, moduloGuard('CAJA')],
    loadComponent: () => import('./pages/cuadre-imprimir/cuadre-imprimir.component').then(m => m.CuadreImprimirComponent)
  },
  {
    path: 'seleccionar-sede',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/seleccionar-sede/seleccionar-sede.component').then(m => m.SeleccionarSedeComponent)
  },
  {
    // Publica a proposito: el cliente la abre desde un link de WhatsApp, sin sesion de empleado.
    path: 'seguimiento/:token',
    loadComponent: () => import('./pages/seguimiento-pago/seguimiento-pago.component').then(m => m.SeguimientoPagoComponent)
  },
  {
    // Publica: el repartidor la abre en su celular desde el link que le pasa el negocio.
    path: 'repartidor/:token',
    loadComponent: () => import('./pages/repartidor/repartidor.component').then(m => m.RepartidorComponent)
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'inicio', pathMatch: 'full' },
      {
        path: 'inicio',
        canActivate: [moduloGuard('INICIO')],
        loadComponent: () => import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent)
      },
      {
        path: 'pedidos',
        canActivate: [moduloGuard('PEDIDOS')],
        loadComponent: () => import('./pages/pedidos-list/pedidos-list.component').then(m => m.PedidosListComponent)
      },
      {
        // Página dedicada del pedido: progreso, cobro, delivery, fotos e historial.
        path: 'pedidos/:id',
        canActivate: [moduloGuard('PEDIDOS')],
        loadComponent: () => import('./pages/pedido-detalle/pedido-detalle.component').then(m => m.PedidoDetalleComponent)
      },
      {
        path: 'registrar',
        canActivate: [moduloGuard('REGISTRAR')],
        loadComponent: () => import('./pages/registrar/registrar.component').then(m => m.RegistrarComponent)
      },
      {
        path: 'registro-antiguo',
        canActivate: [moduloGuard('REGISTRAR')],
        loadComponent: () => import('./pages/registro-antiguo/registro-antiguo.component').then(m => m.RegistroAntiguoComponent)
      },
      {
        path: 'cuadre-caja',
        canActivate: [moduloGuard('CAJA')],
        loadComponent: () => import('./pages/cuadre-caja/cuadre-caja.component').then(m => m.CuadreCajaComponent)
      },
      {
        path: 'clientes',
        canActivate: [moduloGuard('CLIENTES')],
        loadComponent: () => import('./pages/clientes/clientes.component').then(m => m.ClientesComponent)
      },
      {
        path: 'clientes/crm',
        canActivate: [moduloGuard('CLIENTES')],
        loadComponent: () => import('./pages/crm/crm.component').then(m => m.CrmComponent)
      },
      {
        path: 'promociones',
        canActivate: [moduloGuard('PROMOCIONES')],
        loadComponent: () => import('./pages/promociones/promociones.component').then(m => m.PromocionesComponent)
      },
      {
        path: 'reportes',
        canActivate: [moduloGuard('REPORTES')],
        loadComponent: () => import('./pages/reportes/reportes.component').then(m => m.ReportesComponent)
      },
      {
        // Debe ir antes de 'reportes/:key' (ruta con parametro) para que Angular no la confunda
        // con la clave de un reporte generico.
        path: 'reportes/gerencial',
        canActivate: [moduloGuard('REPORTES')],
        loadComponent: () => import('./pages/vista-gerencial/vista-gerencial.component').then(m => m.VistaGerencialComponent)
      },
      {
        path: 'reportes/consolidado',
        canActivate: [moduloGuard('REPORTES')],
        loadComponent: () => import('./pages/consolidado/consolidado.component').then(m => m.ConsolidadoComponent)
      },
      {
        // Pantalla dedicada de cuadres diarios (antes de 'reportes/:key').
        path: 'reportes/cuadres-caja',
        canActivate: [moduloGuard(['CAJA', 'REPORTES'])],
        loadComponent: () => import('./pages/reporte-cuadres-diarios/reporte-cuadres-diarios.component').then(m => m.ReporteCuadresDiariosComponent)
      },
      {
        path: 'inventario',
        canActivate: [moduloGuard('INVENTARIO')],
        loadComponent: () => import('./pages/inventario/inventario.component').then(m => m.InventarioComponent)
      },
      {
        path: 'reportes/:key',
        canActivate: [moduloGuard('REPORTES')],
        loadComponent: () => import('./pages/reporte-detalle/reporte-detalle.component').then(m => m.ReporteDetalleComponent)
      },
      {
        path: 'ajustes',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes/ajustes.component').then(m => m.AjustesComponent)
      },
      {
        path: 'ajustes/negocio',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-negocio/ajustes-negocio.component').then(m => m.AjustesNegocioComponent)
      },
      {
        path: 'ajustes/servicios',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-servicios/ajustes-servicios.component').then(m => m.AjustesServiciosComponent)
      },
      {
        path: 'ajustes/usuarios',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-usuarios/ajustes-usuarios.component').then(m => m.AjustesUsuariosComponent)
      },
      {
        path: 'ajustes/permisos',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-permisos/ajustes-permisos.component').then(m => m.AjustesPermisosComponent)
      },
      {
        path: 'ajustes/categorias',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-categorias/ajustes-categorias.component').then(m => m.AjustesCategoriasComponent)
      },
      {
        path: 'ajustes/tipos-gasto',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-tipos-gasto/ajustes-tipos-gasto.component').then(m => m.AjustesTiposGastoComponent)
      },
      {
        path: 'ajustes/areas',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-areas/ajustes-areas.component').then(m => m.AjustesAreasComponent)
      },
      {
        path: 'ajustes/motorizados',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-motorizados/ajustes-motorizados.component').then(m => m.AjustesMotorizadosComponent)
      },
      {
        path: 'ajustes/personal',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-personal/ajustes-personal.component').then(m => m.AjustesPersonalComponent)
      },
      {
        path: 'ajustes/roles-personal',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-rol-personal/ajustes-rol-personal.component').then(m => m.AjustesRolPersonalComponent)
      },
      {
        path: 'ajustes/plantillas-whatsapp',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-plantillas-whatsapp/ajustes-plantillas-whatsapp.component').then(m => m.AjustesPlantillasWhatsappComponent)
      },
      {
        path: 'ajustes/puntos',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-puntos/ajustes-puntos.component').then(m => m.AjustesPuntosComponent)
      },
      {
        path: 'ajustes/cargo-extra',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-cargo-extra/ajustes-cargo-extra.component').then(m => m.AjustesCargoExtraComponent)
      },
      {
        path: 'ajustes/metas',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-metas/ajustes-metas.component').then(m => m.AjustesMetasComponent)
      },
      {
        path: 'ajustes/sedes',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-sedes/ajustes-sedes.component').then(m => m.AjustesSedesComponent)
      },
      {
        path: 'ajustes/facturacion-electronica',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-facturacion-electronica/ajustes-facturacion-electronica.component').then(m => m.AjustesFacturacionElectronicaComponent)
      },
      {
        path: 'ajustes/pagos',
        canActivate: [moduloGuard('AJUSTES')],
        loadComponent: () => import('./pages/ajustes-pagos/ajustes-pagos.component').then(m => m.AjustesPagosComponent)
      },
      {
        path: 'facturacion/comprobantes',
        canActivate: [moduloGuard('PEDIDOS')],
        loadComponent: () => import('./pages/comprobantes-list/comprobantes-list.component').then(m => m.ComprobantesListComponent)
      },
    ]
  },
  { path: '**', redirectTo: 'inicio' },
];
