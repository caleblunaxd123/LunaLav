import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.autenticado()) return true;
  router.navigate(['/login']);
  return false;
};

/** Evita mostrar el formulario de acceso junto con una sesion ya iniciada. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.autenticado()) return true;

  const usuario = auth.usuario();
  if (usuario?.rol === 'PROPIETARIO') return router.createUrlTree(['/plataforma']);
  return router.createUrlTree([usuario?.sedeId ? '/inicio' : '/seleccionar-sede']);
};

export const rolGuard = (rolesPermitidos: string[]): CanActivateFn => () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.autenticado()) {
    router.navigate(['/login']);
    return false;
  }
  const rol = auth.usuario()?.rol;
  if (rol && rolesPermitidos.includes(rol)) return true;
  router.navigate(['/inicio']);
  return false;
};

export const moduloGuard = (modulo: string | string[]): CanActivateFn => () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.autenticado()) {
    router.navigate(['/login']);
    return false;
  }
  const usuario = auth.usuario();
  const modulos = usuario?.modulosPermitidos;
  // Sesión guardada antes de existir este campo (o corrupta): forzar re-login en vez de
  // redirigir a una ruta que también podría estar protegida (evita loops de navegación).
  if (!modulos) {
    auth.logout();
    return false;
  }
  // Admin con acceso a varias sedes que aún no eligió con cuál trabajar.
  if (!usuario?.sedeId) {
    router.navigate(['/seleccionar-sede']);
    return false;
  }
  // Acepta uno o varios módulos: pasa si el usuario tiene CUALQUIERA de ellos.
  const requeridos = Array.isArray(modulo) ? modulo : [modulo];
  if (usuario?.rol === 'ADMIN' || requeridos.some(m => modulos.includes(m))) return true;
  if (!requeridos.includes('INICIO')) {
    router.navigate(['/inicio']);
  } else {
    auth.logout();
  }
  return false;
};
