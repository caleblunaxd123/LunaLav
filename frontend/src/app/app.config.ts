import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import localeEsPe from '@angular/common/locales/es-PE';
import { ApplicationConfig, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling, UrlSerializer } from '@angular/router';

import { authInterceptor } from './core/interceptors/auth.interceptor';
import { actualizacionDatosInterceptor } from './core/interceptors/actualizacion-datos.interceptor';
import { TenantUrlSerializer } from './core/routing/tenant-url-serializer';
import { routes } from './app.routes';

registerLocaleData(localeEsPe, 'es-PE');

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top', anchorScrolling: 'enabled' })
    ),
    provideHttpClient(withInterceptors([authInterceptor, actualizacionDatosInterceptor])),
    { provide: LOCALE_ID, useValue: 'es-PE' },
    { provide: UrlSerializer, useClass: TenantUrlSerializer },
  ]
};
