import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ClienteAnalitica, ClienteCumpleanos, ClientesService } from '../../core/services/clientes.service';
import { PromocionesService } from '../../core/services/promociones.service';
import { ToastService } from '../../core/services/toast.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-crm',
  imports: [PageHeaderComponent, CommonModule, IconComponent],
  templateUrl: './crm.component.html',
  styleUrl: './crm.component.scss'
})
export class CrmComponent implements OnInit {
  private readonly svc = inject(ClientesService);
  private readonly promociones = inject(PromocionesService);
  private readonly whatsapp = inject(WhatsappService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly generandoCumpleId = signal<number | null>(null);

  readonly cargando = signal(true);
  readonly analitica = signal<ClienteAnalitica[]>([]);
  readonly cumpleanos = signal<ClienteCumpleanos[]>([]);
  readonly diasMinimoRecompra = signal(21);

  readonly candidatosRecompra = () => this.analitica().filter(c => c.diasSinComprar >= this.diasMinimoRecompra());
  readonly clientesConDeuda = () => this.analitica().filter(c => c.deudaTotal > 0.01).sort((a, b) => b.deudaTotal - a.deudaTotal);
  readonly deudaTotalNegocio = () => this.clientesConDeuda().reduce((acc, c) => acc + c.deudaTotal, 0);

  ngOnInit() {
    this.whatsapp.cargar();
    this.cargando.set(true);
    this.svc.analitica().subscribe({
      next: a => { this.analitica.set(a); this.cargando.set(false); },
      error: () => this.cargando.set(false)
    });
    this.svc.cumpleanosProximos(30).subscribe({ next: c => this.cumpleanos.set(c), error: () => {} });
  }

  volver() { this.router.navigate(['/clientes']); }

  avisarRecompra(c: ClienteAnalitica) {
    if (!c.celular) { this.toast.advertencia('Este cliente no tiene celular registrado.'); return; }
    const mensaje = `Hola ${c.nombre}, ¡te extrañamos! Ya pasaron ${c.diasSinComprar} días desde tu última visita. Te esperamos pronto 😊`;
    this.whatsapp.enviar(c.celular, mensaje);
  }

  cobrarDeuda(c: ClienteAnalitica) {
    if (!c.celular) { this.toast.advertencia('Este cliente no tiene celular registrado.'); return; }
    const mensaje = `Hola ${c.nombre}, te recordamos que tienes un saldo pendiente de S/ ${c.deudaTotal.toFixed(2)} en tus pedidos. Puedes acercarte a cancelarlo cuando gustes. ¡Gracias!`;
    this.whatsapp.enviar(c.celular, mensaje);
  }

  saludarCumpleanos(c: ClienteCumpleanos) {
    if (!c.celular) { this.toast.advertencia('Este cliente no tiene celular registrado.'); return; }
    const mensaje = c.diasParaCumpleanos === 0
      ? `¡Feliz cumpleaños, ${c.nombre}! 🎉 Te esperamos con un detalle especial en tu próxima visita.`
      : `Hola ${c.nombre}, sabemos que tu cumpleaños se acerca. ¡Te esperamos con un detalle especial cuando vengas! 🎉`;
    this.whatsapp.enviar(c.celular, mensaje);
  }

  /** Genera un código de descuento de cumpleaños y abre WhatsApp con el mensaje + código. */
  generarCodigoCumple(c: ClienteCumpleanos) {
    this.generandoCumpleId.set(c.clienteId);
    this.promociones.generar({ origen: 'CUMPLE', clienteId: c.clienteId }).subscribe({
      next: res => {
        this.generandoCumpleId.set(null);
        this.toast.exito(`Código ${res.promocion.codigo} generado`);
        if (c.celular) this.whatsapp.enviar(c.celular, res.mensajeWhatsapp);
        else this.toast.advertencia('Cliente sin celular: cópialo desde Promociones.');
      },
      error: (err: { error?: { mensaje?: string } }) => {
        this.generandoCumpleId.set(null);
        this.toast.error(err.error?.mensaje ?? 'No se pudo generar el código.');
      }
    });
  }

  etiquetaDias(dias: number): string {
    if (dias === 0) return 'Hoy';
    if (dias === 1) return 'Mañana';
    return `En ${dias} días`;
  }
}
