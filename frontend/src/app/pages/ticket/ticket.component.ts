import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Pedido } from '../../core/models/models';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { PedidosService } from '../../core/services/pedidos.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-ticket',
  imports: [CommonModule, IconComponent],
  templateUrl: './ticket.component.html',
  styleUrl: './ticket.component.scss'
})
export class TicketComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(PedidosService);
  private readonly config = inject(ConfiguracionService);
  private readonly whatsapp = inject(WhatsappService);

  readonly pedido = signal<Pedido | null>(null);
  readonly error = signal<string | null>(null);
  readonly cargando = signal(true);

  readonly tipoTicket = signal<'CLIENTE' | 'PRODUCCION'>('CLIENTE');
  readonly mostrarDescripcion = signal(false);
  readonly celularEnvio = signal('');
  readonly generandoImagen = signal(false);
  readonly generandoPdf = signal(false);

  readonly negocio = computed(() => this.config.configuracion());

  // Logo optimizado para impresion (ver ConfiguracionService.logoImpresion).
  readonly logoImpresion = computed(() => ConfiguracionService.logoImpresion(this.negocio().logoUrl));

  // Direccion del negocio para el ticket: une las abreviaturas de direccion a la palabra que
  // sigue con un espacio duro, para que no queden colgadas al final de un renglon angosto
  // (ej. "..., Urb." arriba y "Distrito, Lima" abajo -> pasa junto sin salto extra).
  readonly direccionTicket = computed(() =>
    (this.negocio().direccion ?? '').replace(
      /\b(Urb|Mz|Lt|Cond|Res|A\.H|P\.J|Av|Jr|Ca|Cal|Psje|Pje)\.\s+/gi,
      m => m.replace(/\s+$/, String.fromCharCode(160))
    )
  );

  readonly marcaCorta = computed(() =>
    (this.negocio().nombreNegocio || 'LavanderÃ­a').replace(/^lavander[iÃ­]a\s+/i, '').trim().toUpperCase()
  );

  readonly condicionesLista = computed(() =>
    (this.negocio().condicionesServicio ?? '')
      .split('\n')
      .map(l => l.trim().replace(/^\d+\.\s*/, ''))
      .filter(l => l.length > 0)
  );

  readonly saldo = computed(() => {
    const p = this.pedido();
    return p ? Math.max(0, p.total - p.montoPagado) : 0;
  });

  private static readonly MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
    'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];

  /** Fecha en el estilo del ticket de referencia: "24 de agosto, 2026 / 10:11 am". */
  fechaLarga(f: string | Date | null | undefined): string {
    if (!f) return '';
    const d = new Date(f);
    if (isNaN(d.getTime())) return '';
    const mes = TicketComponent.MESES[d.getMonth()];
    let h = d.getHours();
    const min = d.getMinutes().toString().padStart(2, '0');
    const ampm = h < 12 ? 'am' : 'pm';
    h = h % 12; if (h === 0) h = 12;
    return `${d.getDate()} de ${mes}, ${d.getFullYear()} / ${h}:${min} ${ampm}`;
  }

  constructor() {}

  ngOnInit() {
    // Ancho de pagina segun configuracion (58 o 80mm)
    const ancho = this.negocio().anchoTicketMm || 80;
    this.aplicarAncho(ancho);
    this.whatsapp.cargar();

    const id = Number(this.route.snapshot.paramMap.get('id'));
    // Permite abrir directo el ticket de producciÃ³n con /ticket/:id?tipo=produccion
    if ((this.route.snapshot.queryParamMap.get('tipo') ?? '').toUpperCase() === 'PRODUCCION') {
      this.tipoTicket.set('PRODUCCION');
    }
    if (!id) {
      this.error.set('ID de pedido invÃ¡lido.');
      this.cargando.set(false);
      return;
    }

    this.service.obtener(id).subscribe({
      next: p => {
        this.pedido.set(p);
        this.celularEnvio.set(p.clienteCelular ?? '');
        this.cargando.set(false);
        // Auto lanzar el dialogo de imprimir despues de que se pinte todo.
        // Con ?print=0 se abre el ticket solo para previsualizar (sin dialogo).
        if (this.route.snapshot.queryParamMap.get('print') !== '0') {
          setTimeout(() => this.imprimir(), 400);
        }
      },
      error: () => {
        this.error.set('No se pudo cargar el pedido.');
        this.cargando.set(false);
      }
    });
  }

  imprimir() { window.print(); }

  enviarWhatsapp() {
    const p = this.pedido();
    if (!p) return;
    const celular = this.celularEnvio().trim();
    if (!celular) {
      this.error.set('Ingresa un celular para enviar el mensaje.');
      return;
    }

    const enviar = (link?: string) =>
      this.whatsapp.enviar(celular, this.whatsapp.mensajeIngreso(p, this.negocio(), link));

    if (p.modalidad === 'Recojo' || p.modalidad === 'Delivery') {
      this.service.linkSeguimiento(p.id).subscribe({ next: ({ token }) => enviar(`${window.location.origin}/seguimiento/${token}`), error: () => enviar() });
    } else {
      enviar();
    }
  }

  async compartirPdfWhatsapp() {
    const p = this.pedido();
    if (!p) return;
    const celular = this.celularEnvio().trim();
    if (!celular) {
      this.error.set('Ingresa un celular para compartir el PDF.');
      return;
    }

    this.generandoPdf.set(true);
    try {
      const blob = await this.generarPdfTicket();
      const file = new File([blob], `ticket-${p.numero}.pdf`, { type: 'application/pdf' });
      const nav = navigator as Navigator & {
        canShare?: (data: ShareData) => boolean;
        share?: (data: ShareData) => Promise<void>;
      };
      const shareData: ShareData = {
        files: [file],
        title: `Ticket ${p.numero}`,
        text: `Ticket #${p.numero} - ${this.negocio().nombreNegocio}`
      };

      if (nav.share && (!nav.canShare || nav.canShare(shareData))) {
        await nav.share(shareData);
        return;
      }

      this.descargarBlob(blob, `ticket-${p.numero}.pdf`);
      this.enviarWhatsapp();
    } catch {
      this.error.set('No se pudo generar el PDF del ticket.');
    } finally {
      this.generandoPdf.set(false);
    }
  }

  // No existe forma de adjuntar un archivo directo al mensaje de WhatsApp desde la web
  // (los enlaces wa.me solo prellenan texto). Como alternativa se descarga el ticket como
  // imagen para que el usuario la adjunte manualmente en WhatsApp.
  async descargarImagen() {
    const p = this.pedido();
    if (!p) return;

    this.generandoImagen.set(true);
    try {
      const canvas = await this.generarCanvasTicket();
      const url = canvas.toDataURL('image/png');
      const a = document.createElement('a');
      a.href = url;
      a.download = `ticket-${p.numero}.png`;
      a.click();
    } catch {
      this.error.set('No se pudo generar la imagen del ticket.');
    } finally {
      this.generandoImagen.set(false);
    }
  }

  cerrar() { window.close(); }

  private async generarCanvasTicket() {
    const el = document.querySelector('.ticket') as HTMLElement | null;
    if (!el) throw new Error('Ticket no encontrado.');
    const html2canvas = (await import('html2canvas')).default;
    return await html2canvas(el, { backgroundColor: '#ffffff', scale: 2 });
  }

  private async generarPdfTicket() {
    const canvas = await this.generarCanvasTicket();
    const { jsPDF } = await import('jspdf');
    // Mismo ancho útil que al imprimir, para que el PDF salga igual si lo mandan a la térmica.
    const anchoMm = TicketComponent.anchoUtilMm(this.negocio().anchoTicketMm || 80);
    const altoMm = Math.max(1, (canvas.height * anchoMm) / canvas.width);
    const pdf = new jsPDF({
      orientation: 'portrait',
      unit: 'mm',
      format: [anchoMm, altoMm]
    });

    pdf.addImage(canvas.toDataURL('image/png'), 'PNG', 0, 0, anchoMm, altoMm);
    return pdf.output('blob');
  }

  private descargarBlob(blob: Blob, nombre: string) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = nombre;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  /**
   * Ancho imprimible REAL del cabezal termico (para el PDF/imagen que se genera):
   * un papel de 80mm imprime ~72mm y uno de 58mm ~48mm. El PDF se dimensiona con
   * este ancho para que, si se manda a la termica, entre completo sin recortarse.
   * Nota: al IMPRIMIR por navegador (RawBT) el @page usa el ancho del papel real
   * (ver aplicarAncho) porque el navegador no respeta un @page mas angosto; ahi la
   * columna de montos se protege con el padding derecho del @media print.
   */
  static anchoUtilMm(anchoPapelMm: number): number {
    return anchoPapelMm <= 60 ? 48 : 72;
  }

  private aplicarAncho(mm: number) {
    // @page con el ancho del PAPEL real (80 o 58mm). El navegador/RawBT ignora un
    // @page mas angosto y maqueta al ancho del papel de todos modos, asi que aqui
    // somos honestos con la medida y dejamos que el padding derecho del @media print
    // mantenga el contenido dentro de la zona imprimible del cabezal.
    const anchoPapel = mm <= 60 ? 58 : 80;
    const styleId = 'ticket-page-size';
    document.getElementById(styleId)?.remove();
    const style = document.createElement('style');
    style.id = styleId;
    style.textContent = `@page { size: ${anchoPapel}mm auto; margin: 0; }`;
    document.head.appendChild(style);
  }
}
