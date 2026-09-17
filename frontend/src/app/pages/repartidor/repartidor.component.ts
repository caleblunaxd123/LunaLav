import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, NgZone, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { EstadoRuta } from '../../core/services/pago-publico.service';
import { RepartidorPedido, RepartidorService } from '../../core/services/repartidor.service';
import { MapaSeguimientoComponent } from '../../shared/mapa-seguimiento/mapa-seguimiento.component';

const MS_ENTRE_ENVIOS = 10_000;

@Component({
  selector: 'app-repartidor',
  standalone: true,
  imports: [CommonModule, MapaSeguimientoComponent],
  templateUrl: './repartidor.component.html',
  styleUrl: './repartidor.component.scss'
})
export class RepartidorComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly svc = inject(RepartidorService);
  private readonly zone = inject(NgZone);

  private token = '';
  private watchId?: number;
  private ultimoEnvio = 0;
  private timerActualizacion?: ReturnType<typeof setInterval>;
  private readonly alRecuperarFoco = () => this.cargarSiVisible();

  readonly cargando = signal(true);
  readonly error = signal<string | null>(null);
  readonly data = signal<RepartidorPedido | null>(null);
  readonly estadoRuta = signal<EstadoRuta>('SIN_RUTA');
  readonly distanciaMetros = signal<number | null>(null);
  readonly etaMinutos = signal<number | null>(null);

  readonly compartiendo = signal(false);
  readonly gpsError = signal<string | null>(null);
  readonly procesando = signal(false);
  readonly mensaje = signal<string | null>(null);

  readonly miLat = signal<number | null>(null);
  readonly miLng = signal<number | null>(null);

  readonly enRuta = computed(() => {
    const e = this.estadoRuta();
    return e === 'EN_RUTA' || e === 'CERCA' || e === 'LLEGO';
  });

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    this.cargar();
    this.timerActualizacion = setInterval(() => this.cargarSiVisible(), 10_000);
    window.addEventListener('focus', this.alRecuperarFoco);
  }

  ngOnDestroy(): void {
    this.detenerGps();
    if (this.timerActualizacion) clearInterval(this.timerActualizacion);
    window.removeEventListener('focus', this.alRecuperarFoco);
  }

  private cargarSiVisible(): void {
    if (document.visibilityState === 'visible' && !this.procesando()) this.cargar();
  }

  private cargar(): void {
    this.svc.obtener(this.token).subscribe({
      next: d => {
        this.data.set(d);
        this.estadoRuta.set(d.estadoRuta);
        this.cargando.set(false);
        document.documentElement.style.setProperty('--marca', d.colorPrimario || '#053465');
        document.title = `Reparto · Pedido #${d.numeroPedido}`;
        // Si el reparto ya estaba en curso (recargó la pantalla), retoma el envío de GPS.
        if (this.enRuta()) this.iniciarGps();
      },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 404
          ? 'Este enlace no es válido. Pídele uno nuevo a la lavandería.'
          : 'No se pudo cargar el pedido. Revisa tu conexión.');
      }
    });
  }

  salirARuta(): void {
    this.mensaje.set(null);
    this.procesando.set(true);
    this.svc.iniciarRuta(this.token).subscribe({
      next: () => {
        this.procesando.set(false);
        this.estadoRuta.set('EN_RUTA');
        const d = this.data();
        if (d) this.data.set({ ...d, rutaIniciadaEn: new Date().toISOString(), estadoRuta: 'EN_RUTA' });
        this.iniciarGps();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.mensaje.set(err.error?.mensaje ?? 'No se pudo iniciar la ruta.');
      }
    });
  }

  private iniciarGps(): void {
    if (this.watchId != null) return;
    this.gpsError.set(null);

    if (!navigator.geolocation) {
      this.gpsError.set('Este dispositivo no permite compartir la ubicación.');
      return;
    }
    if (!window.isSecureContext) {
      // Sin HTTPS el navegador bloquea el GPS. El reparto sigue por hitos; el mapa en vivo
      // recién funcionará cuando la app esté publicada con dominio seguro.
      this.gpsError.set('El mapa en vivo necesita una conexión segura (HTTPS). Igual puedes marcar "Llegué / Entregué" al terminar.');
      return;
    }

    this.compartiendo.set(true);
    this.watchId = navigator.geolocation.watchPosition(
      pos => this.zone.run(() => this.onPosicion(pos)),
      err => this.zone.run(() => this.onGpsError(err)),
      { enableHighAccuracy: true, maximumAge: 4000, timeout: 20000 }
    );
  }

  private onPosicion(pos: GeolocationPosition): void {
    const lat = pos.coords.latitude;
    const lng = pos.coords.longitude;
    this.miLat.set(lat);
    this.miLng.set(lng);
    this.gpsError.set(null);

    const ahora = Date.now();
    if (ahora - this.ultimoEnvio < MS_ENTRE_ENVIOS) return;
    this.ultimoEnvio = ahora;

    this.svc.enviarUbicacion(this.token, +lat.toFixed(6), +lng.toFixed(6)).subscribe({
      next: r => {
        this.estadoRuta.set(r.estadoRuta);
        this.distanciaMetros.set(r.distanciaMetros ?? null);
        this.etaMinutos.set(r.etaMinutos ?? null);
      },
      error: () => { /* reintento en el siguiente tick del watch */ }
    });
  }

  private onGpsError(err: GeolocationPositionError): void {
    this.compartiendo.set(false);
    this.gpsError.set(err.code === err.PERMISSION_DENIED
      ? 'Bloqueaste el acceso a la ubicación. Actívalo en el navegador para compartir tu recorrido.'
      : 'No pudimos leer tu ubicación. Revisa el GPS del celular.');
  }

  private detenerGps(): void {
    if (this.watchId != null) {
      navigator.geolocation.clearWatch(this.watchId);
      this.watchId = undefined;
    }
    this.compartiendo.set(false);
  }

  entregar(): void {
    this.mensaje.set(null);
    this.procesando.set(true);
    this.svc.marcarEntregado(this.token).subscribe({
      next: () => {
        this.procesando.set(false);
        this.estadoRuta.set('ENTREGADO');
        this.detenerGps();
        const d = this.data();
        if (d) this.data.set({ ...d, entregado: true, estadoRuta: 'ENTREGADO' });
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.mensaje.set(err.error?.mensaje ?? 'No se pudo marcar como entregado.');
      }
    });
  }

  abrirEnMapsExterno(): void {
    const d = this.data();
    if (!d || d.latitudEntrega == null || d.longitudEntrega == null) return;
    window.open(`https://www.google.com/maps/dir/?api=1&destination=${d.latitudEntrega},${d.longitudEntrega}`, '_blank');
  }

  llamarCliente(): void {
    const cel = (this.data()?.clienteCelular ?? '').replace(/\D/g, '');
    if (cel) window.open(`tel:${cel}`, '_self');
  }

  textoDistancia(): string | null {
    const m = this.distanciaMetros();
    if (m == null) return null;
    return m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${m} m`;
  }
}
