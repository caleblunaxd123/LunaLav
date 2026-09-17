import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { TenantContextService } from '../../core/services/tenant-context.service';
import { DESARROLLADOR_CREDITO, PRODUCTO_LOGO_COLOR, PRODUCTO_NOMBRE, PRODUCTO_TAGLINE } from '../../core/util/marca';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-login',
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly config = inject(ConfiguracionService);
  private readonly tenant = inject(TenantContextService);

  // Con slug (/demo/login) mostramos la marca de ESA lavandería; sin slug (/login,
  // acceso del propietario/plataforma) la pantalla es NEUTRAL: marca del PRODUCTO
  // (LunaLav), sin nombre ni logo de ningún negocio en particular.
  readonly productoNombre = PRODUCTO_NOMBRE;
  readonly productoTagline = PRODUCTO_TAGLINE;
  readonly logoColor = PRODUCTO_LOGO_COLOR;
  readonly desarrolladorCredito = DESARROLLADOR_CREDITO;
  readonly tieneSlug = signal(false);
  readonly nombreNegocio = computed(() =>
    this.tieneSlug() ? (this.config.configuracion().nombreNegocio || PRODUCTO_NOMBRE) : PRODUCTO_NOMBRE);
  readonly logoUrl = computed(() =>
    this.tieneSlug() ? this.config.configuracion().logoUrl : null);
  readonly subtitulo = computed(() =>
    this.tieneSlug() ? 'Ingresa tus credenciales' : 'Panel de administración');

  usuario = '';
  password = '';
  cargando = signal(false);
  error = signal<string | null>(null);
  mostrarPassword = signal(false);

  ngOnInit() {
    // Solo cargamos la marca de una empresa cuando la URL trae su slug (/demo/login).
    // Sin slug no cargamos ninguna config de negocio → la pantalla queda neutral.
    const slug = this.tenant.slug();
    this.tieneSlug.set(!!slug);
    if (slug) {
      this.config.cargarPorSlug(slug).subscribe({ error: () => {} });
    } else {
      // Login neutral: el título de la pestaña es el del PRODUCTO, no de un negocio.
      document.title = `${PRODUCTO_NOMBRE} — Gestión de lavanderías`;
    }
  }

  submit() {
    if (!this.usuario.trim() || !this.password) {
      this.error.set('Completa usuario y contraseña.');
      return;
    }
    this.cargando.set(true);
    this.error.set(null);

    this.auth.login({ usuario: this.usuario.trim(), password: this.password }).subscribe({
      next: () => {
        // La marca (logo/nombre/colores) se cargó antes del login con el negocio "por defecto";
        // hay que refrescarla ahora que el token trae el negocio real del usuario.
        this.config.cargar().subscribe({ error: () => {} });
        const usuario = this.auth.usuario();
        const destino = usuario?.rol === 'PROPIETARIO'
          ? '/plataforma'
          : (usuario?.sedeId ? '/inicio' : '/seleccionar-sede');
        this.router.navigate([destino]);
      },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.error?.mensaje ?? 'No se pudo iniciar sesión. Verifica el servidor.');
      }
    });
  }
}
