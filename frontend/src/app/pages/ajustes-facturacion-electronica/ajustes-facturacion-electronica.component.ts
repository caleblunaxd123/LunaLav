import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfiguracionFacturacion, FacturacionService } from '../../core/services/facturacion.service';
import { ToastService } from '../../core/services/toast.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { InfoTooltipComponent } from '../../shared/info-tooltip/info-tooltip.component';

const VACIO: ConfiguracionFacturacion = {
  razonSocial: '',
  rucEmisor: '',
  ambiente: 'BETA',
  solUsuario: '',
  solClaveNueva: null,
  certificadoPfxBase64: null,
  certificadoPasswordNueva: null,
  serieBoleta: 'B001',
  serieFactura: 'F001',
  activo: false,
  soloBoletas: false,
  tieneCertificado: false,
  tieneCredencialesSol: false,
  proveedor: 'APISUNAT',
  apiSunatPersonaId: '',
  apiSunatTokenNuevo: null,
  tieneCredencialesApiSunat: false,
  direccionFiscal: '',
  ubigeo: '',
  codigoEstablecimiento: '0000',
  emailEmisor: '',
  correlativoBoleta: 0,
  correlativoFactura: 0,
  requiereCertificadoLocal: false
};

@Component({
  selector: 'app-ajustes-facturacion-electronica',
  imports: [PageHeaderComponent, CommonModule, FormsModule, InfoTooltipComponent],
  templateUrl: './ajustes-facturacion-electronica.component.html',
  styleUrl: './ajustes-facturacion-electronica.component.scss'
})
export class AjustesFacturacionElectronicaComponent implements OnInit {
  private readonly svc = inject(FacturacionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly form = signal<ConfiguracionFacturacion>({ ...VACIO });
  readonly cargando = signal(true);
  readonly guardando = signal(false);
  readonly probandoConexion = signal(false);
  readonly nombreCertificadoSeleccionado = signal<string | null>(null);

  ngOnInit() {
    this.svc.obtenerConfiguracion().subscribe({
      next: c => { this.form.set({ ...c, solClaveNueva: null, apiSunatTokenNuevo: null, certificadoPasswordNueva: null, certificadoPfxBase64: null }); this.cargando.set(false); },
      error: () => { this.cargando.set(false); this.toast.error('No se pudo cargar la configuración.'); }
    });
  }

  actualizarCampo<K extends keyof ConfiguracionFacturacion>(campo: K, valor: ConfiguracionFacturacion[K]) {
    this.form.update(f => ({ ...f, [campo]: valor }));
  }

  onCertificadoSeleccionado(event: Event) {
    const input = event.target as HTMLInputElement;
    const archivo = input.files?.[0];
    if (!archivo) return;
    if (!/\.(pfx|p12)$/i.test(archivo.name) || archivo.size > 2 * 1024 * 1024) {
      this.toast.error('Selecciona un certificado .pfx o .p12 de hasta 2 MB.');
      input.value = '';
      return;
    }
    this.nombreCertificadoSeleccionado.set(archivo.name);
    const lector = new FileReader();
    lector.onload = () => {
      const base64 = (lector.result as string).split(',')[1];
      this.actualizarCampo('certificadoPfxBase64', base64);
    };
    lector.readAsDataURL(archivo);
  }

  guardar() {
    this.guardando.set(true);
    this.svc.guardarConfiguracion(this.form()).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.exito('Configuración guardada');
        this.nombreCertificadoSeleccionado.set(null);
        this.ngOnInit();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar la configuración.');
      }
    });
  }

  probarConexion() {
    this.probandoConexion.set(true);
    this.svc.probarConfiguracion(this.form()).subscribe({
      next: resultado => {
        this.probandoConexion.set(false);
        if (resultado.exitoso) {
          const ambiente = resultado.produccion ? 'PRODUCCIÓN' : 'DESARROLLO';
          this.toast.exito(`${resultado.mensaje} Ambiente ${ambiente}.`);
        } else {
          this.toast.error(resultado.mensaje);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.probandoConexion.set(false);
        this.toast.desdeHttp(err, 'No se pudo probar la conexión.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
