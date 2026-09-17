import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CajaService, CuadreCajaGuardado } from '../../core/services/caja.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-cuadre-imprimir',
  imports: [CommonModule, IconComponent],
  templateUrl: './cuadre-imprimir.component.html',
  styleUrl: './cuadre-imprimir.component.scss'
})
export class CuadreImprimirComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly cajaSvc = inject(CajaService);
  private readonly config = inject(ConfiguracionService);

  readonly cuadre = signal<CuadreCajaGuardado | null>(null);
  readonly error = signal<string | null>(null);
  readonly cargando = signal(true);
  readonly negocio = computed(() => this.config.configuracion());
  // Logo monocromo para impresión (ver ConfiguracionService.logoImpresion).
  readonly logoImpresion = computed(() => ConfiguracionService.logoImpresion(this.negocio().logoUrl));

  readonly estado = computed<'SOBRA' | 'CUADRA' | 'FALTA'>(() => {
    const c = this.cuadre();
    if (!c) return 'CUADRA';
    if (Math.abs(c.diferencia) < 0.01) return 'CUADRA';
    return c.diferencia > 0 ? 'SOBRA' : 'FALTA';
  });

  ngOnInit() {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!id) {
      this.error.set('ID de cuadre inválido.');
      this.cargando.set(false);
      return;
    }
    this.cajaSvc.obtenerCuadre(id).subscribe({
      next: c => {
        this.cuadre.set(c);
        this.cargando.set(false);
        // Con ?print=0 se abre solo para previsualizar (sin diálogo de impresión).
        if (this.route.snapshot.queryParamMap.get('print') !== '0') {
          setTimeout(() => this.imprimir(), 500);
        }
      },
      error: () => {
        this.error.set('No se pudo cargar el cuadre.');
        this.cargando.set(false);
      }
    });
  }

  imprimir() { window.print(); }
  cerrar() { window.close(); }

  /** Resumen del cuadre en texto plano, para compartir por WhatsApp o correo. */
  private resumenTexto(): string {
    const c = this.cuadre();
    if (!c) return '';
    const n = this.negocio();
    const s = (v: number) => 'S/ ' + (v ?? 0).toFixed(2);
    const enCaja = c.cajaInicial + c.pedidosPagadosEfect - c.gastos;
    const fecha = new Date(c.fecha).toLocaleDateString('es-PE', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
    const estado = this.estado() === 'CUADRA' ? 'CUADRA (exacto)'
      : this.estado() === 'SOBRA' ? `SOBRA ${s(c.diferencia)}`
      : `FALTA ${s(Math.abs(c.diferencia))}`;

    const lineas = [
      `*${n.nombreNegocio} — Cuadre de Caja N° ${c.id}*`,
      `Fecha: ${fecha}`,
      `Responsable: ${c.usuarioNombre || '—'}`,
      ``,
      `Resultado: ${estado}`,
      ``,
      `Movimientos del día`,
      `• Caja inicial: ${s(c.cajaInicial)}`,
      `• Pedidos pagados (efectivo): ${s(c.pedidosPagadosEfect)}`,
      `• Gastos en efectivo: ${s(c.gastos)}`,
      `• En caja debería haber: ${s(enCaja)}`,
      ``,
      `Conteo físico`,
      `• Total contado: ${s(c.totalContado)}`,
      `• Diferencia: ${s(c.diferencia)}`,
      ``,
      `Cierre`,
      `• Corte (efectivo entregado): ${s(c.corte)}`,
      `• Caja final: ${s(c.cajaFinal)}`,
    ];
    if (c.ingresosDigital > 0 || c.ingresosTarjeta > 0) {
      lineas.push(``, `Ingresos digitales`, `• Yape/Plin/Transf.: ${s(c.ingresosDigital)}`, `• Tarjeta/POS: ${s(c.ingresosTarjeta)}`);
    }
    return lineas.join('\n');
  }

  enviarWhatsapp() {
    window.open(`https://wa.me/?text=${encodeURIComponent(this.resumenTexto())}`, '_blank');
  }

  readonly generandoPdf = signal(false);

  /**
   * Correo:
   *  - PC de escritorio → abre el correo con asunto + resumen en texto (mailto no adjunta archivos).
   *  - Tablet / celular → "compartir nativo" del sistema con el PDF del cuadre adjunto: eliges
   *    Gmail/Outlook/WhatsApp y das Enviar.
   */
  async enviarCorreo() {
    const c = this.cuadre();
    const asunto = `Cuadre de Caja N° ${c?.id ?? ''} — ${this.negocio().nombreNegocio}`;
    const texto = this.resumenTexto();
    const nav = navigator as unknown as { share?: (d: unknown) => Promise<void>; canShare?: (d: unknown) => boolean };

    if (this.puedeCompartirArchivo()) {
      try {
        this.generandoPdf.set(true);
        const blob = await this.generarPdfCuadre();
        this.generandoPdf.set(false);
        const archivo = new File([blob], `cuadre-${c?.id ?? ''}.pdf`, { type: 'application/pdf' });
        if (nav.canShare!({ files: [archivo] })) {
          await nav.share!({ files: [archivo], title: asunto, text: texto });
          return; // compartido (o cancelado): no abrir el correo de texto
        }
      } catch {
        this.generandoPdf.set(false);
        return; // el usuario canceló o falló el compartir: no forzar el mailto
      }
    }

    // PC de escritorio (o sin compartir de archivos): correo con texto.
    window.location.href = `mailto:?subject=${encodeURIComponent(asunto)}&body=${encodeURIComponent(texto)}`;
  }

  /** True solo en tablet/celular con soporte de compartir archivos (no en PC de escritorio). */
  puedeCompartirArchivo(): boolean {
    try {
      const nav = navigator as unknown as { share?: unknown; canShare?: (d: unknown) => boolean };
      if (typeof nav.share !== 'function' || typeof nav.canShare !== 'function') return false;
      if (!this.esMovilOTablet()) return false;
      const test = new File(['x'], 't.pdf', { type: 'application/pdf' });
      return nav.canShare({ files: [test] });
    } catch { return false; }
  }

  private esMovilOTablet(): boolean {
    const n = navigator as unknown as { userAgent?: string; platform?: string; maxTouchPoints?: number };
    const ua = n.userAgent ?? '';
    const uaMovil = /Android|iPhone|iPad|iPod|IEMobile|Opera Mini|Mobile/i.test(ua);
    const iPadOS = n.platform === 'MacIntel' && (n.maxTouchPoints ?? 0) > 1; // iPad moderno se hace pasar por Mac
    return uaMovil || iPadOS;
  }

  private async generarPdfCuadre(): Promise<Blob> {
    const el = document.querySelector('.doc') as HTMLElement | null;
    if (!el) throw new Error('Documento no encontrado.');
    const html2canvas = (await import('html2canvas')).default;
    const canvas = await html2canvas(el, { backgroundColor: '#ffffff', scale: 2 });
    const { jsPDF } = await import('jspdf');
    const pdf = new jsPDF({ orientation: 'portrait', unit: 'pt', format: 'a4' });
    const pw = pdf.internal.pageSize.getWidth();
    const ph = pdf.internal.pageSize.getHeight();
    const margen = 24;
    let w = pw - margen * 2;
    let h = (canvas.height * w) / canvas.width;
    const maxH = ph - margen * 2;
    if (h > maxH) { h = maxH; w = (canvas.width * h) / canvas.height; }
    pdf.addImage(canvas.toDataURL('image/jpeg', 0.95), 'JPEG', (pw - w) / 2, margen, w, h);
    return pdf.output('blob');
  }
}
