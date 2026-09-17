import { AfterViewInit, Component, ElementRef, EventEmitter, Input, NgZone, OnChanges, OnDestroy, Output, SimpleChanges, ViewChild, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import * as L from 'leaflet';
import { environment } from '../../../environments/environment';
import { IconComponent } from '../icon/icon.component';

export interface UbicacionMapa {
  latitud: number;
  longitud: number;
  /** Dirección aproximada resuelta desde el mapa (reverse geocoding). */
  direccion?: string;
  /** Distrito resuelto desde el mapa. */
  distrito?: string;
  /** Forma en que el operador confirmó el destino. */
  origen: 'busqueda' | 'mapa' | 'gps';
  /** Etiqueta completa devuelta por el geocodificador, útil para validación visual. */
  etiqueta?: string;
}

interface ResultadoDireccion {
  id: string;
  latitud: number;
  longitud: number;
  direccion?: string;
  distrito?: string;
  etiqueta: string;
}

@Component({
  selector: 'app-mapa-ubicacion',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './mapa-ubicacion.component.html',
  styleUrl: './mapa-ubicacion.component.scss'
})
export class MapaUbicacionComponent implements AfterViewInit, OnChanges, OnDestroy {
  private readonly zone = inject(NgZone);
  private readonly http = inject(HttpClient);

  @ViewChild('mapa', { static: true }) mapaElement!: ElementRef<HTMLDivElement>;
  @Input() latitud: number | null = null;
  @Input() longitud: number | null = null;
  @Input() direccion = '';
  @Input() distrito = '';
  @Input() confirmada = false;
  @Output() ubicacionChange = new EventEmitter<UbicacionMapa | null>();

  ubicando = false;
  geocodificando = false;
  buscandoDireccion = false;
  errorUbicacion = '';
  resultados: ResultadoDireccion[] = [];

  private mapa?: L.Map;
  private marcador?: L.CircleMarker;

  ngAfterViewInit(): void {
    this.mapa = L.map(this.mapaElement.nativeElement, {
      center: this.tieneCoordenadas() ? [this.latitud!, this.longitud!] : [-12.0464, -77.0428],
      zoom: this.tieneCoordenadas() ? 17 : 12,
      zoomControl: true
    });

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(this.mapa);

    this.mapa.on('click', ({ latlng }: L.LeafletMouseEvent) => {
      this.zone.run(() => this.establecerPunto(latlng.lat, latlng.lng, true, 'mapa'));
    });

    this.actualizarMarcador(false);
    setTimeout(() => this.mapa?.invalidateSize(), 0);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['latitud'] || changes['longitud']) && this.mapa) {
      this.actualizarMarcador(false);
    }
  }

  usarMiUbicacion(): void {
    this.errorUbicacion = '';
    if (!navigator.geolocation) {
      this.errorUbicacion = 'Este dispositivo no permite obtener la ubicación.';
      return;
    }
    // La geolocalización del navegador solo funciona en un contexto seguro (HTTPS)
    // o en localhost. Desde el celular por IP local (http://192.168...) queda bloqueada.
    if (!window.isSecureContext) {
      this.errorUbicacion = 'La ubicación automática necesita una conexión segura (HTTPS). Por ahora marca el punto tocando el mapa.';
      return;
    }

    this.ubicando = true;
    navigator.geolocation.getCurrentPosition(
      posicion => this.zone.run(() => {
        this.ubicando = false;
        this.establecerPunto(posicion.coords.latitude, posicion.coords.longitude, true, 'gps');
      }),
      (err: GeolocationPositionError) => this.zone.run(() => {
        this.ubicando = false;
        this.errorUbicacion = err.code === err.PERMISSION_DENIED
          ? 'Bloqueaste el acceso a la ubicación. Habilítalo en el navegador o marca el punto en el mapa.'
          : err.code === err.TIMEOUT
            ? 'La ubicación tardó demasiado. Intenta de nuevo o marca el punto en el mapa.'
            : 'No pudimos obtener la ubicación. Actívala o marca el punto en el mapa.';
      }),
      { enableHighAccuracy: true, timeout: 10000, maximumAge: 30000 }
    );
  }

  limpiar(): void {
    this.latitud = null;
    this.longitud = null;
    this.marcador?.remove();
    this.marcador = undefined;
    this.resultados = [];
    this.errorUbicacion = '';
    this.ubicacionChange.emit(null);
  }

  async buscarDireccion(): Promise<void> {
    const direccion = this.direccion.trim();
    const distrito = this.distrito.trim();
    this.errorUbicacion = '';
    this.resultados = [];

    if (direccion.length < 4 || !distrito) {
      this.errorUbicacion = 'Escribe una dirección y selecciona el distrito antes de buscar.';
      return;
    }

    this.buscandoDireccion = true;
    try {
      const params = new HttpParams().set('direccion', direccion).set('distrito', distrito);
      this.resultados = await firstValueFrom(this.http.get<ResultadoDireccion[]>(
        `${environment.apiUrl}/geocodificacion/buscar`, { params }
      ));
      if (this.resultados.length === 0) {
        this.errorUbicacion = 'El mapa no reconoció esa dirección. Revisa calle, número y distrito, o marca el punto manualmente.';
      }
    } catch {
      this.errorUbicacion = 'No pudimos consultar el mapa. Intenta nuevamente o marca el punto manualmente.';
    } finally {
      this.buscandoDireccion = false;
    }
  }

  seleccionarResultado(resultado: ResultadoDireccion): void {
    this.latitud = resultado.latitud;
    this.longitud = resultado.longitud;
    this.errorUbicacion = '';
    this.resultados = [];
    this.actualizarMarcador(true);
    this.ubicacionChange.emit({
      latitud: resultado.latitud,
      longitud: resultado.longitud,
      direccion: this.direccion.trim(),
      distrito: this.distrito.trim(),
      etiqueta: resultado.etiqueta,
      origen: 'busqueda'
    });
  }

  ngOnDestroy(): void {
    this.mapa?.remove();
  }

  private tieneCoordenadas(): boolean {
    return Number.isFinite(this.latitud) && Number.isFinite(this.longitud);
  }

  private establecerPunto(
    latitud: number,
    longitud: number,
    emitir: boolean,
    origen: 'mapa' | 'gps' = 'mapa'
  ): void {
    this.latitud = Number(latitud.toFixed(6));
    this.longitud = Number(longitud.toFixed(6));
    this.errorUbicacion = '';
    this.actualizarMarcador(true);
    if (!emitir) return;

    // Al elegir un punto, resolvemos dirección y distrito para autocompletar los campos.
    const lat = this.latitud, lon = this.longitud;
    this.geocodificando = true;
    this.reverseGeocode(lat, lon).then(dir => {
      this.zone.run(() => {
        this.geocodificando = false;
        if (!dir?.etiqueta) {
          this.errorUbicacion = 'El punto quedó marcado, pero el mapa no pudo reconocer su dirección. Intenta nuevamente.';
        }
        this.ubicacionChange.emit({
          latitud: lat, longitud: lon, direccion: dir?.direccion,
          distrito: dir?.distrito, etiqueta: dir?.etiqueta, origen
        });
      });
    }).catch(() => {
      this.zone.run(() => {
        this.geocodificando = false;
        this.errorUbicacion = 'El punto quedó marcado, pero el mapa no pudo reconocer su dirección. Intenta nuevamente.';
        this.ubicacionChange.emit({ latitud: lat, longitud: lon, origen });
      });
    });
  }

  /** Reverse geocoding gratuito con Nominatim (OpenStreetMap) — sin API key. */
  private async reverseGeocode(lat: number, lon: number): Promise<{ direccion?: string; distrito?: string; etiqueta?: string } | null> {
    const params = new HttpParams().set('latitud', lat).set('longitud', lon);
    return firstValueFrom(this.http.get<ResultadoDireccion>(
      `${environment.apiUrl}/geocodificacion/reversa`, { params }
    ));
  }

  private actualizarMarcador(centrar: boolean): void {
    if (!this.mapa || !this.tieneCoordenadas()) return;
    const punto: L.LatLngExpression = [this.latitud!, this.longitud!];
    if (!this.marcador) {
      this.marcador = L.circleMarker(punto, {
        radius: 9,
        color: '#ffffff',
        weight: 3,
        fillColor: '#053465',
        fillOpacity: 1
      }).addTo(this.mapa);
    } else {
      this.marcador.setLatLng(punto);
    }
    if (centrar) this.mapa.setView(punto, Math.max(this.mapa.getZoom(), 17));
  }
}
