import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, of, share, tap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LoginRequest, LoginResponse, UsuarioSesion } from '../models/models';
import { TenantContextService } from './tenant-context.service';

const STORAGE_KEY = 'lav.session';

interface SesionAlmacenada {
  accessToken: string;
  expira: string;
  refreshToken: string;
  usuario: UsuarioSesion;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly tenant = inject(TenantContextService);

  private readonly sesion = signal<SesionAlmacenada | null>(this.restaurar());

  readonly usuario = computed<UsuarioSesion | null>(() => this.sesion()?.usuario ?? null);
  // El access token dura poco (20 min) y el interceptor lo renueva solo contra /auth/refresh
  // cuando el servidor responde 401 — por eso "autenticado" ya no depende de si el access
  // token en si sigue fresco, solo de si hay una sesion guardada. Si el refresh token tambien
  // esta vencido/revocado, el interceptor termina llamando a logout() y esto pasa a false.
  readonly autenticado = computed<boolean>(() => this.sesion() !== null);

  login(req: LoginRequest) {
    const body: LoginRequest = { ...req, empresaSlug: this.tenant.slug() ?? undefined };
    return this.http.post<LoginResponse>(`${environment.apiUrl}/auth/login`, body).pipe(
      tap(res => this.guardar(res))
    );
  }

  /** Cambia la sede activa de la sesión (re-emite el JWT con el nuevo SedeId). */
  cambiarSede(sedeId: number) {
    return this.http.post<LoginResponse>(`${environment.apiUrl}/auth/seleccionar-sede`, {
      sedeId,
      refreshToken: this.obtenerRefreshToken()
    }).pipe(
      tap(res => this.guardar(res))
    );
  }

  /** Revoca el refresh token en el servidor (logout real) antes de limpiar la sesión local. */
  logout() {
    const refreshToken = this.sesion()?.refreshToken;
    this.limpiarSesionLocal();
    if (refreshToken) {
      this.http.post(`${environment.apiUrl}/auth/logout`, { refreshToken }).pipe(
        catchError(() => of(null))
      ).subscribe();
    }
    this.router.navigate(['/login']);
  }

  private limpiarSesionLocal() {
    localStorage.removeItem(STORAGE_KEY);
    this.sesion.set(null);
  }

  obtenerToken(): string | null {
    return this.sesion()?.accessToken ?? null;
  }

  obtenerRefreshToken(): string | null {
    return this.sesion()?.refreshToken ?? null;
  }

  private refrescoEnCurso$: Observable<LoginResponse> | null = null;

  /** Renueva el access token usando el refresh token guardado (llamado por el interceptor
   * cuando una request responde 401 por token vencido). Si ya hay una renovacion en vuelo
   * (varias requests vencieron a la vez), todas comparten la misma llamada en vez de disparar
   * un /auth/refresh por cada una — el refresh token rota en cada uso, asi que una segunda
   * llamada en paralelo con el mismo token ya consumido fallaria. */
  refrescarToken(): Observable<LoginResponse> {
    if (this.refrescoEnCurso$) return this.refrescoEnCurso$;

    const refreshToken = this.obtenerRefreshToken();
    if (!refreshToken) return throwError(() => new Error('No hay refresh token disponible.'));

    this.refrescoEnCurso$ = this.http.post<LoginResponse>(`${environment.apiUrl}/auth/refresh`, { refreshToken }).pipe(
      tap(res => this.guardar(res)),
      share(),
      finalize(() => { this.refrescoEnCurso$ = null; })
    );
    return this.refrescoEnCurso$;
  }

  esRol(rol: string): boolean {
    return this.usuario()?.rol === rol;
  }

  private guardar(res: LoginResponse) {
    const s: SesionAlmacenada = {
      accessToken: res.accessToken, expira: res.expira, refreshToken: res.refreshToken, usuario: res.usuario
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
    this.sesion.set(s);
  }

  private restaurar(): SesionAlmacenada | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as SesionAlmacenada;
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  }
}
