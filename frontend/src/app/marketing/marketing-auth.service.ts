import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface MarketingUser { id: number; usuario: string; nombre: string; rol: 'ADMINISTRADOR' | 'MARKETING' | 'SUPERVISOR'; email?: string; }
interface Session { accessToken: string; expira: string; refreshToken: string; usuario: MarketingUser; }
const KEY = 'lunalav.marketing.session';

@Injectable({ providedIn: 'root' }) export class MarketingAuthService {
  private http = inject(HttpClient); private router = inject(Router); private state = signal<Session | null>(this.restore());
  readonly usuario = computed(() => this.state()?.usuario ?? null); readonly autenticado = computed(() => !!this.state());
  login(usuario: string, password: string) { return this.http.post<Session>(`${environment.apiUrl}/marketing/auth/login`, { usuario, password }).pipe(tap(s => this.save(s))); }
  token() { return this.state()?.accessToken ?? null; } refreshToken() { return this.state()?.refreshToken ?? null; }
  refresh() { return this.http.post<Session>(`${environment.apiUrl}/marketing/auth/refresh`, { refreshToken: this.refreshToken() }).pipe(tap(s => this.save(s))); }
  logout() { const refreshToken = this.refreshToken(); localStorage.removeItem(KEY); this.state.set(null); if (refreshToken) this.http.post(`${environment.apiUrl}/marketing/auth/logout`, { refreshToken }).subscribe({ error: () => {} }); this.router.navigate(['/marketing/login']); }
  private save(s: Session) { localStorage.setItem(KEY, JSON.stringify(s)); this.state.set(s); }
  private restore(): Session | null { try { const raw = localStorage.getItem(KEY); return raw ? JSON.parse(raw) : null; } catch { localStorage.removeItem(KEY); return null; } }
}
