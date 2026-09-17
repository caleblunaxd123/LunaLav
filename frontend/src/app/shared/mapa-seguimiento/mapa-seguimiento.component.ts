import { AfterViewInit, Component, ElementRef, Input, OnChanges, OnDestroy, SimpleChanges, ViewChild } from '@angular/core';
import * as L from 'leaflet';

/**
 * Mapa de solo lectura para el seguimiento en vivo: pin fijo del destino y pin del repartidor
 * que se mueve. Usa divIcons (HTML) en vez de los marcadores por defecto de Leaflet para no
 * depender de imágenes externas que suelen dar 404 al empaquetar.
 */
@Component({
  selector: 'app-mapa-seguimiento',
  standalone: true,
  template: `<div #mapa class="mapa-seg"></div>`,
  styles: [`
    .mapa-seg { width: 100%; height: 100%; min-height: 240px; border-radius: 16px; overflow: hidden; z-index: 0; }
    :host { display: block; width: 100%; height: 100%; }
    :host ::ng-deep .pin-moto {
      display: grid; place-items: center; width: 40px; height: 40px; border-radius: 50% 50% 50% 2px;
      transform: rotate(45deg); box-shadow: 0 4px 12px rgba(0,0,0,.35); border: 3px solid #fff; font-size: 19px;
    }
    :host ::ng-deep .pin-moto > span { transform: rotate(-45deg); }
    :host ::ng-deep .pin-moto::after {
      content: ''; position: absolute; inset: -10px; border-radius: 50%;
      animation: pulso 1.8s ease-out infinite;
    }
    @keyframes pulso { 0% { box-shadow: 0 0 0 0 rgba(11,87,208,.45); } 100% { box-shadow: 0 0 0 16px rgba(11,87,208,0); } }
    :host ::ng-deep .pin-destino {
      display: grid; place-items: center; width: 34px; height: 34px; border-radius: 50% 50% 50% 2px;
      transform: rotate(45deg); background: #e11d48; border: 3px solid #fff; box-shadow: 0 4px 10px rgba(0,0,0,.3); font-size: 15px;
    }
    :host ::ng-deep .pin-destino > span { transform: rotate(-45deg); }
  `]
})
export class MapaSeguimientoComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('mapa', { static: true }) el!: ElementRef<HTMLDivElement>;
  @Input() destinoLat: number | null = null;
  @Input() destinoLng: number | null = null;
  @Input() motoLat: number | null = null;
  @Input() motoLng: number | null = null;
  @Input() colorMoto = '#053465';

  private mapa?: L.Map;
  private mDestino?: L.Marker;
  private mMoto?: L.Marker;
  private linea?: L.Polyline;
  private encuadrado = false;

  ngAfterViewInit(): void {
    this.mapa = L.map(this.el.nativeElement, { zoomControl: true, attributionControl: false });
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19 }).addTo(this.mapa);
    this.mapa.setView(this.tiene(this.destinoLat, this.destinoLng) ? [this.destinoLat!, this.destinoLng!] : [-12.0464, -77.0428], 14);
    setTimeout(() => this.mapa?.invalidateSize(), 0);
    this.render();
  }

  ngOnChanges(_: SimpleChanges): void {
    if (this.mapa) this.render();
  }

  ngOnDestroy(): void {
    this.mapa?.remove();
  }

  private tiene(a: number | null, b: number | null): boolean {
    return Number.isFinite(a) && Number.isFinite(b);
  }

  private render(): void {
    if (!this.mapa) return;

    // Pin del destino (fijo)
    if (this.tiene(this.destinoLat, this.destinoLng)) {
      const p: L.LatLngExpression = [this.destinoLat!, this.destinoLng!];
      if (!this.mDestino) {
        this.mDestino = L.marker(p, {
          icon: L.divIcon({ className: '', html: `<div class="pin-destino"><span>🏠</span></div>`, iconSize: [34, 34], iconAnchor: [17, 32] }),
          interactive: false
        }).addTo(this.mapa);
      } else {
        this.mDestino.setLatLng(p);
      }
    }

    // Pin del repartidor (se mueve)
    const hayMoto = this.tiene(this.motoLat, this.motoLng);
    if (hayMoto) {
      const p: L.LatLngExpression = [this.motoLat!, this.motoLng!];
      const html = `<div class="pin-moto" style="background:${this.colorMoto}"><span>🛵</span></div>`;
      if (!this.mMoto) {
        this.mMoto = L.marker(p, {
          icon: L.divIcon({ className: '', html, iconSize: [40, 40], iconAnchor: [20, 36] }),
          zIndexOffset: 1000, interactive: false
        }).addTo(this.mapa);
      } else {
        this.mMoto.setLatLng(p);
        this.mMoto.setIcon(L.divIcon({ className: '', html, iconSize: [40, 40], iconAnchor: [20, 36] }));
      }
    } else if (this.mMoto) {
      this.mMoto.remove();
      this.mMoto = undefined;
    }

    // Línea entre repartidor y destino
    if (hayMoto && this.tiene(this.destinoLat, this.destinoLng)) {
      const pts: L.LatLngExpression[] = [[this.motoLat!, this.motoLng!], [this.destinoLat!, this.destinoLng!]];
      if (!this.linea) {
        this.linea = L.polyline(pts, { color: this.colorMoto, weight: 4, opacity: .55, dashArray: '2 9', lineCap: 'round' }).addTo(this.mapa);
      } else {
        this.linea.setLatLngs(pts);
      }
      // Encuadra ambos puntos la primera vez; luego sigue al repartidor sin reencuadrar bruscamente.
      if (!this.encuadrado) {
        this.mapa.fitBounds(L.latLngBounds(pts as L.LatLngTuple[]).pad(0.4), { maxZoom: 16 });
        this.encuadrado = true;
      } else {
        this.mapa.panTo([this.motoLat!, this.motoLng!], { animate: true, duration: 0.8 });
      }
    }
  }
}
