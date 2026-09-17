import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfiguracionNegocio } from '../../core/models/models';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { ToastService } from '../../core/services/toast.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-ajustes-negocio',
  imports: [PageHeaderComponent, CommonModule, FormsModule, IconComponent],
  templateUrl: './ajustes-negocio.component.html',
  styleUrl: './ajustes-negocio.component.scss'
})
export class AjustesNegocioComponent implements OnInit {
  private readonly svc = inject(ConfiguracionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly form = signal<ConfiguracionNegocio>({ ...this.svc.configuracion() });
  readonly guardando = signal(false);

  // --- Logo: se sube desde el equipo del usuario ---
  readonly subiendoLogo = signal(false);
  readonly errorLogo = signal<string | null>(null);
  readonly arrastrando = signal(false);

  onDragOver(e: DragEvent) {
    e.preventDefault();
    this.arrastrando.set(true);
  }

  onDrop(e: DragEvent) {
    e.preventDefault();
    this.arrastrando.set(false);
    const archivo = e.dataTransfer?.files?.[0];
    if (archivo) this.subirLogo(archivo);
  }

  onArchivoLogo(e: Event) {
    const input = e.target as HTMLInputElement;
    const archivo = input.files?.[0];
    if (archivo) this.subirLogo(archivo);
    input.value = '';   // permite volver a elegir el mismo archivo
  }

  quitarLogo() {
    this.actualizarCampo('logoUrl', null as never);
    this.errorLogo.set(null);
  }

  private subirLogo(archivo: File) {
    this.errorLogo.set(null);
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(archivo.type)) {
      this.errorLogo.set('Formato no permitido. Elige una imagen JPG, PNG o WEBP.');
      return;
    }
    if (archivo.size > 2 * 1024 * 1024) {
      this.errorLogo.set('La imagen pesa más de 2 MB. Usa una más liviana.');
      return;
    }
    this.subiendoLogo.set(true);
    this.svc.subirLogo(archivo).subscribe({
      next: r => {
        this.subiendoLogo.set(false);
        this.actualizarCampo('logoUrl', r.logoUrl as never);
        this.toast.exito('Logo cargado. Guarda los cambios para aplicarlo.');
      },
      error: (err: HttpErrorResponse) => {
        this.subiendoLogo.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo subir el logo.';
        this.errorLogo.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  // --- QR de Yape: se sube como el logo (mismo endpoint de imagen) y se guarda en yapeQrUrl ---
  readonly subiendoQr = signal(false);
  readonly errorQr = signal<string | null>(null);

  onArchivoQr(e: Event) {
    const input = e.target as HTMLInputElement;
    const archivo = input.files?.[0];
    if (archivo) this.subirQr(archivo);
    input.value = '';
  }

  quitarQr() {
    this.actualizarCampo('yapeQrUrl', null as never);
    this.errorQr.set(null);
  }

  private subirQr(archivo: File) {
    this.errorQr.set(null);
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(archivo.type)) {
      this.errorQr.set('Formato no permitido. Elige una imagen JPG, PNG o WEBP.');
      return;
    }
    if (archivo.size > 2 * 1024 * 1024) {
      this.errorQr.set('La imagen pesa más de 2 MB. Usa una más liviana.');
      return;
    }
    this.subiendoQr.set(true);
    this.svc.subirLogo(archivo).subscribe({
      next: r => {
        this.subiendoQr.set(false);
        this.actualizarCampo('yapeQrUrl', r.logoUrl as never);
        this.toast.exito('QR cargado. Guarda los cambios para aplicarlo.');
      },
      error: (err: HttpErrorResponse) => {
        this.subiendoQr.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo subir el QR.';
        this.errorQr.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  readonly previewStyle = computed(() => ({
    'background': `linear-gradient(90deg, ${this.form().colorPrimario} 0%, ${this.form().colorSecundario} 100%)`,
  }));

  // --- Contraste ---
  // El menú lateral y los botones pintan texto blanco sobre el color primario. Si el color
  // elegido es muy claro, ese texto deja de leerse: se avisa antes de guardar.
  // Fórmula de contraste de WCAG 2.1; 4.5:1 es el mínimo para texto normal.
  private static luminancia(hex: string): number {
    const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
    if (!m) return 0;
    const canal = (v: number) => {
      const s = v / 255;
      return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
    };
    const n = parseInt(m[1], 16);
    return 0.2126 * canal((n >> 16) & 255) + 0.7152 * canal((n >> 8) & 255) + 0.0722 * canal(n & 255);
  }

  /** Contraste del color primario contra el texto blanco del menú. */
  readonly contraste = computed(() => {
    const l = AjustesNegocioComponent.luminancia(this.form().colorPrimario);
    return (1.05) / (l + 0.05);   // (1.0 + 0.05) / (L + 0.05), el blanco es el más claro
  });

  readonly contrasteOk = computed(() => this.contraste() >= 4.5);
  readonly contrasteTexto = computed(() =>
    this.contrasteOk()
      ? `El texto blanco se lee bien sobre este color (contraste ${this.contraste().toFixed(1)}:1).`
      : `Este color es demasiado claro: el texto blanco del menú casi no se leerá ` +
        `(contraste ${this.contraste().toFixed(1)}:1, se recomienda 4.5:1 o más). Elige un tono más oscuro.`);

  ngOnInit() {
    // Aseguramos que traigamos la ultima config del backend
    this.svc.cargar().subscribe({
      next: c => this.form.set({ ...c }),
      error: () => this.toast.error('No se pudo cargar la configuración.')
    });
  }

  actualizarCampo<K extends keyof ConfiguracionNegocio>(campo: K, valor: ConfiguracionNegocio[K]) {
    this.form.update(f => ({ ...f, [campo]: valor }));
  }

  guardar() {
    this.guardando.set(true);
    this.svc.actualizar(this.form()).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.exito('Configuración guardada');
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar la configuración.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
