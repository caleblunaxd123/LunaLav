import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { MarketingAuthService } from '../../marketing/marketing-auth.service';

// El propio login/refresh/logout nunca debe intentar "renovarse a si mismo" en un 401 (login
// invalido y refresh vencido SI deben responder 401 tal cual, no entrar en un loop).
const RUTAS_SIN_REINTENTO = ['/auth/login', '/auth/refresh', '/auth/logout'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const marketing = inject(MarketingAuthService);
  const esMarketing = req.url.includes('/marketing/');
  const token = auth.obtenerToken();

  // ngrok-skip-browser-warning: cuando el sistema se sirve tras un túnel ngrok gratuito,
  // ngrok intercala una pantalla de advertencia. Sin este header, las llamadas XHR reciben
  // ese HTML en vez del JSON de la API y el login/datos se rompen. Es inofensivo fuera de ngrok.
  const headers: Record<string, string> = { 'ngrok-skip-browser-warning': 'true' };
  if (esMarketing && marketing.token()) headers['Authorization'] = `Bearer ${marketing.token()}`;
  else if (token) headers['Authorization'] = `Bearer ${token}`;

  const clonado = req.clone({ setHeaders: headers });

  const esRutaAuth = RUTAS_SIN_REINTENTO.some(r => req.url.includes(r)) || req.url.includes('/marketing/auth/');

  return next(clonado).pipe(
    catchError((err: HttpErrorResponse) => {
      const refreshToken = esMarketing ? marketing.refreshToken() : auth.obtenerRefreshToken();
      if (err.status !== 401 || esRutaAuth || !refreshToken) {
        if (err.status === 401) esMarketing ? marketing.logout() : auth.logout();
        return throwError(() => err);
      }

      // Access token vencido (dura poco a proposito): se renueva en silencio contra
      // /auth/refresh y se reintenta esta misma request una sola vez con el token nuevo.
      // Ambos servicios devuelven accessToken, pero sus DTOs concretos son distintos.
      // Normalizamos aquí para conservar el contrato HttpEvent del interceptor.
      const renovacion: any = esMarketing ? marketing.refresh() : auth.refrescarToken();
      return renovacion.pipe(
        switchMap((res: { accessToken: string }) => {
          const reintento = req.clone({ setHeaders: { 'ngrok-skip-browser-warning': 'true', Authorization: `Bearer ${res.accessToken}` } });
          return next(reintento);
        }),
        catchError(errorRefresh => {
          esMarketing ? marketing.logout() : auth.logout();
          return throwError(() => errorRefresh);
        })
      ) as any;
    })
  ) as any;
};
