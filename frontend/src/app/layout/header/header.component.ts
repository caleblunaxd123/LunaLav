import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter } from 'rxjs';
import { Sede } from '../../core/models/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { AlertasGlobalesService } from '../../core/services/alertas-globales.service';
import { SedesService } from '../../core/services/sedes.service';
import { DESARROLLADOR_CREDITO } from '../../core/util/marca';
import { IconComponent, IconName } from '../../shared/icon/icon.component';

interface SubLink {
  label: string;
  path: string;
  modulo?: string; // si difiere del módulo del padre (para permisos)
}

interface NavLink {
  label: string;
  path: string;
  modulo: string;
  icono: IconName;
  children?: SubLink[];
}

@Component({
  selector: 'app-header',
  imports: [CommonModule, RouterLink, RouterLinkActive, IconComponent],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss'
})
export class HeaderComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly config = inject(ConfiguracionService);
  private readonly sedesSvc = inject(SedesService);
  private readonly alertasSvc = inject(AlertasGlobalesService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly usuario = this.auth.usuario;
  readonly negocio = computed(() => this.config.configuracion());
  readonly desarrolladorCredito = DESARROLLADOR_CREDITO;

  readonly menuAbierto = signal(false);
  readonly urlActual = signal(this.router.url);
  // Módulo con el submenú expandido (acordeón: solo uno abierto a la vez).
  readonly expandido = signal<string | null>(null);

  // Badge de sede: siempre visible para saber en qué sucursal estás trabajando.
  readonly sedes = signal<Sede[]>([]);
  readonly sedeMenuAbierto = signal(false);
  readonly cambiandoSede = signal(false);
  readonly mostrarBadgeSede = computed(() => !!this.usuario()?.sedeNombre);
  readonly puedeCambiarSede = computed(() => this.usuario()?.rol === 'ADMIN' && this.sedes().length > 1);

  readonly atencionAbierta = signal(false);
  readonly cargandoAtencion = this.alertasSvc.cargando;
  readonly errorAtencion = this.alertasSvc.error;
  readonly actualizadoEnAtencion = this.alertasSvc.actualizadoEn;
  readonly puedeVerAtencion = computed(() => this.tieneModulo('INICIO'));
  readonly alertasAtencion = computed(() => this.alertasSvc.alertas().filter(alerta => alerta.ambito === 'negocio'));
  readonly totalAtencion = computed(() =>
    Math.min(99, this.alertasAtencion().reduce((total, alerta) => total + alerta.cantidad, 0))
  );

  constructor() {
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(e => {
      this.urlActual.set(e.urlAfterRedirects);
      this.atencionAbierta.set(false);
      this.abrirSeccionActiva();
    });
  }

  ngOnInit() {
    if (this.usuario()?.rol === 'ADMIN') {
      this.sedesSvc.listar().subscribe(list => this.sedes.set(list.filter(s => s.activo)));
    }
    if (this.puedeVerAtencion()) {
      this.alertasSvc.iniciar();
    }
    this.abrirSeccionActiva();
  }

  toggleAtencion() {
    const abrir = !this.atencionAbierta();
    this.menuAbierto.set(false);
    this.sedeMenuAbierto.set(false);
    this.atencionAbierta.set(abrir);
    if (abrir && !this.actualizadoEnAtencion() && !this.cargandoAtencion()) this.cargarAtencion();
  }

  cerrarAtencion() { this.atencionAbierta.set(false); }

  cargarAtencion(silencioso = false) {
    this.alertasSvc.refrescar(silencioso);
  }

  actualizadoAtencion(): string {
    const fecha = this.actualizadoEnAtencion();
    if (!fecha) return '';
    return new Intl.DateTimeFormat('es-PE', { hour: '2-digit', minute: '2-digit' }).format(fecha);
  }

  private tieneModulo(modulo: string): boolean {
    return this.usuario()?.rol === 'ADMIN' || (this.usuario()?.modulosPermitidos ?? []).includes(modulo);
  }

  /** Acordeón: abre la sección donde está el usuario (y cierra las demás). */
  private abrirSeccionActiva() {
    const activo = [...this.primaryLinksBase, ...this.secondaryLinksBase]
      .find(l => l.children?.length && this.esSeccionActiva(l));
    if (activo) this.expandido.set(activo.modulo);
  }

  toggleSedeMenu() { this.sedeMenuAbierto.update(v => !v); }

  elegirSede(sedeId: number) {
    if (sedeId === this.usuario()?.sedeId) { this.sedeMenuAbierto.set(false); return; }
    this.cambiandoSede.set(true);
    this.auth.cambiarSede(sedeId).subscribe({
      next: () => {
        this.cambiandoSede.set(false);
        this.sedeMenuAbierto.set(false);
        window.location.reload();
      },
      error: () => { this.cambiandoSede.set(false); }
    });
  }

  private readonly primaryLinksBase: NavLink[] = [
    { label: 'Dashboard', path: '/inicio', modulo: 'INICIO', icono: 'home' },
    {
      label: 'Lista de Pedidos', path: '/pedidos', modulo: 'PEDIDOS', icono: 'clipboard',
      children: [
        { label: 'Ver pedidos', path: '/pedidos' },
        { label: 'Registro antiguos', path: '/registro-antiguo', modulo: 'REGISTRAR' },
      ]
    },
    { label: 'Registrar', path: '/registrar', modulo: 'REGISTRAR', icono: 'plus' },
    {
      label: 'Cuadre de Caja', path: '/cuadre-caja', modulo: 'CAJA', icono: 'cash',
      children: [
        { label: 'Cuadre del día', path: '/cuadre-caja' },
        { label: 'Reporte de cuadres', path: '/reportes/cuadres-caja', modulo: 'CAJA' },
      ]
    },
  ];

  private readonly secondaryLinksBase: NavLink[] = [
    {
      label: 'Clientes', path: '/clientes', modulo: 'CLIENTES', icono: 'users',
      children: [
        { label: 'Lista de clientes', path: '/clientes' },
        { label: 'Fidelización (CRM)', path: '/clientes/crm' },
      ]
    },
    { label: 'Promociones', path: '/promociones', modulo: 'PROMOCIONES', icono: 'tag' },
    {
      label: 'Reportes', path: '/reportes', modulo: 'REPORTES', icono: 'chart',
      children: [
        { label: 'Todos los reportes', path: '/reportes' },
        { label: 'Vista gerencial', path: '/reportes/gerencial' },
        { label: 'Consolidado', path: '/reportes/consolidado' },
        { label: 'Cuadres diarios', path: '/reportes/cuadres-caja' },
      ]
    },
    { label: 'Inventario', path: '/inventario', modulo: 'INVENTARIO', icono: 'package' },
    { label: 'Comprobantes electrónicos', path: '/facturacion/comprobantes', modulo: 'PEDIDOS', icono: 'note' },
    {
      label: 'Ajustes', path: '/ajustes', modulo: 'AJUSTES', icono: 'settings',
      children: [
        { label: 'Ver todo', path: '/ajustes' },
        { label: 'Negocio y marca', path: '/ajustes/negocio' },
        { label: 'Servicios y precios', path: '/ajustes/servicios' },
        { label: 'Personal', path: '/ajustes/personal' },
        { label: 'Usuarios y permisos', path: '/ajustes/usuarios' },
        { label: 'Pagos en línea', path: '/ajustes/pagos' },
      ]
    },
  ];

  readonly primaryLinks = computed(() => this.filtrar(this.primaryLinksBase));
  readonly secondaryLinks = computed(() => this.filtrar(this.secondaryLinksBase));

  private filtrar(list: NavLink[]): NavLink[] {
    const modulos = this.usuario()?.modulosPermitidos ?? [];
    return list.filter(l => modulos.includes(l.modulo));
  }

  /** Hijos visibles según los permisos del usuario. */
  hijosVisibles(link: NavLink): SubLink[] {
    const modulos = this.usuario()?.modulosPermitidos ?? [];
    return (link.children ?? []).filter(c => modulos.includes(c.modulo ?? link.modulo));
  }

  /** La sección está activa si la ruta actual coincide con el padre o alguno de sus hijos. */
  esSeccionActiva(link: NavLink): boolean {
    const url = this.urlActual().split('?')[0];
    if (url === link.path) return true;
    return (link.children ?? []).some(c => url === c.path)
      // Caso especial: reportes/:key cae bajo la sección Reportes.
      || (link.path !== '/inicio' && url.startsWith(link.path + '/'));
  }

  estaExpandido(link: NavLink): boolean { return this.expandido() === link.modulo; }

  toggleGrupo(link: NavLink, ev: Event) {
    ev.preventDefault();
    ev.stopPropagation();
    // Acordeón tipo carrusel: al abrir uno se cierra el que estaba abierto.
    this.expandido.set(this.expandido() === link.modulo ? null : link.modulo);
  }

  toggleMenu() { this.menuAbierto.update(v => !v); }
  cerrarMenu() { this.menuAbierto.set(false); }
  logout() { this.cerrarMenu(); this.auth.logout(); }
}
