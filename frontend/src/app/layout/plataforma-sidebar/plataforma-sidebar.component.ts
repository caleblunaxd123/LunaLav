import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { DESARROLLADOR_CREDITO } from '../../core/util/marca';
import { IconComponent, IconName } from '../../shared/icon/icon.component';

interface NavLink { label: string; path: string; icono: IconName; exact?: boolean; }

/**
 * Navegación del panel de propietario, con el mismo patrón que el sidebar del cliente:
 * sidebar fijo en escritorio y cajón deslizable (hamburguesa) en móvil.
 */
@Component({
  selector: 'app-plataforma-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, IconComponent],
  templateUrl: './plataforma-sidebar.component.html',
  styleUrl: './plataforma-sidebar.component.scss'
})
export class PlataformaSidebarComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly usuario = this.auth.usuario;
  readonly desarrolladorCredito = DESARROLLADOR_CREDITO;
  readonly menuAbierto = signal(false);

  readonly general: NavLink[] = [
    { label: 'Empresas', path: '/plataforma', icono: 'bank', exact: true },
    { label: 'Nueva empresa', path: '/plataforma/nueva', icono: 'plus' },
  ];
  readonly administracion: NavLink[] = [
    { label: 'Ajustes de plataforma', path: '/plataforma/ajustes', icono: 'settings' },
  ];

  constructor() {
    // Al navegar, cierra el cajón móvil.
    this.router.events.pipe(filter(e => e instanceof NavigationEnd))
      .subscribe(() => this.menuAbierto.set(false));
  }

  toggleMenu() { this.menuAbierto.update(v => !v); }
  cerrarMenu() { this.menuAbierto.set(false); }
  logout() { this.cerrarMenu(); this.auth.logout(); }
}
