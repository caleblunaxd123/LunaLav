import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { ConfiguracionNegocio, Pedido } from '../models/models';
import { numeroWhatsapp } from '../util/telefono';

export interface PlantillaWhatsappActiva {
  evento: string;
  mensaje: string;
}

@Injectable({ providedIn: 'root' })
export class WhatsappService {
  private readonly http = inject(HttpClient);
  private readonly plantillas = signal<PlantillaWhatsappActiva[]>([]);
  private cargado = false;

  cargar() {
    if (this.cargado) return;
    this.cargado = true;
    this.http.get<PlantillaWhatsappActiva[]>(`${environment.apiUrl}/plantillas-whatsapp`)
      .subscribe({ next: list => this.plantillas.set(list), error: () => {} });
  }

  mensaje(evento: string, vars: Record<string, string>, fallback: string): string {
    const plantilla = this.plantillas().find(p => p.evento === evento);
    let texto = plantilla?.mensaje ?? fallback;
    for (const [clave, valor] of Object.entries(vars)) {
      texto = texto.split(`{${clave}}`).join(valor);
    }
    return texto;
  }

  mensajeIngreso(pedido: Pedido, negocio: ConfiguracionNegocio, seguimientoUrl?: string): string {
    const cliente = (pedido.clienteNombre || 'CLIENTE').trim().toUpperCase();
    const marca = (negocio.nombreNegocio || 'LAVIXA')
      .replace(/^lavander[ií]a\s+/i, '')
      .trim()
      .toUpperCase();
    const items = pedido.items.map(item => {
      const cantidad = this.formatearCantidad(item.cantidad);
      const unidad = this.formatearUnidad(item.servicioUnidad);
      const nombre = item.servicioNombre || 'Servicio';
      const descripcion = item.descripcion?.trim() || 'Sin descripción';
      return `*• ${cantidad} ${unidad} de "${nombre}" - S/${item.total.toFixed(2)}*\n${descripcion}`;
    }).join('\n\n');
    const saldo = Math.max(0, pedido.total - pedido.montoPagado).toFixed(2);
    const entrega = pedido.fechaEntregaEst
      ? this.formatearFechaEntrega(pedido.fechaEntregaEst)
      : 'Por confirmar';
    const horario = this.formatearHorario(negocio.horarioAtencion);
    const destino = pedido.modalidad === 'Delivery' && pedido.direccionEntrega
      ? `*Destino de entrega:* ${pedido.direccionEntrega}${pedido.distritoEntrega ? `, ${pedido.distritoEntrega}` : ''}${pedido.referenciaEntrega ? `\n*Referencia:* ${pedido.referenciaEntrega}` : ''}`
      : '';
    const condiciones = [
      '1. Entrega solo con boleta. No se entregan prendas sin ella.',
      '2. Después de 45 días se cobra 20% por almacenamiento.',
      '3. Prendas no retiradas en 90 días serán donadas o rematadas.',
      '4. No nos responsabilizamos por daños en prendas frágiles, muy usadas o de mala confección.',
      '5. No aceptamos ropa interior; no nos responsabilizamos si es enviada.',
      '6. Mascarillas serán desechadas sin derecho a reclamo.',
      '7. No garantizamos la eliminación total de manchas difíciles.',
      '8. En casos fortuitos comprobados, no hay responsabilidad por prendas fuera de plazo.'
    ].join('\n');
    const seguimiento = seguimientoUrl ? `Sigue el estado de tu pedido aquí:\n${seguimientoUrl}` : '';

    const vars = {
      cliente,
      negocio: marca,
      numero: String(pedido.numero),
      items,
      total: pedido.total.toFixed(2),
      saldo,
      entrega,
      destino,
      horario,
      condiciones,
      seguimiento
    };
    const fallback = `¡Hola *${cliente}*!\nLe saluda la lavandería *${marca}*. Su orden es la *${pedido.numero}* con los siguientes ítems:\n\n${items}\n\nMonto total a pagar *S/${pedido.total.toFixed(2)}*, del cual falta pagar *S/${saldo}*.\nFecha de entrega: *${entrega}*.\n\n${destino ? destino + '\n\n' : ''}Nuestro horario de atención es:\n${horario}\n\n${seguimiento ? seguimiento + '\n\n' : ''}*CONDICIONES DEL SERVICIO - ${marca}*\n${condiciones}`;

    const plantilla = this.plantillas().find(p => p.evento === 'INGRESO')?.mensaje;
    let texto = plantilla?.includes('{items}')
      ? Object.entries(vars).reduce((actual, [clave, valor]) => actual.split(`{${clave}}`).join(valor), plantilla)
      : fallback;

    if (seguimientoUrl && !texto.includes(seguimientoUrl)) texto += `\n\n${seguimiento}`;
    if (destino && pedido.direccionEntrega && !texto.includes(pedido.direccionEntrega)) texto += `\n\n${destino}`;
    return texto;
  }

  /**
   * Mensaje "en camino" para el botón de seguimiento de un pedido Delivery: avisa al cliente
   * que el repartidor va en camino, con el link de seguimiento en vivo, el saldo pendiente y
   * los datos de Yape/Plin del negocio para que pueda pagar.
   */
  mensajeEnCamino(pedido: Pedido, negocio: ConfiguracionNegocio, seguimientoUrl?: string): string {
    const cliente = (pedido.clienteNombre || 'Cliente').trim();
    const marca = (negocio.nombreNegocio || 'Mi lavandería').trim();
    const saldo = Math.max(0, pedido.total - pedido.montoPagado);
    const yapeNumero = (negocio.yapeNumero || negocio.telefono || '').trim();
    const yapeTitular = (negocio.yapeTitular || '').trim();

    let texto = `¡Hola ${cliente}! Tu pedido #${pedido.numero} de ${marca} ya va en camino a tu dirección.`;
    if (seguimientoUrl) texto += ` Sigue al repartidor en tiempo real aquí:\n${seguimientoUrl}`;
    if (saldo > 0.009) texto += `\n\nRecuerda que tienes un monto pendiente: S/.${saldo.toFixed(2)} .`;
    if (yapeNumero) texto += `\n\nSi vas a pagar por Yape/Plin a este número: ${yapeNumero}${yapeTitular ? ` - ${yapeTitular}` : ''}`;
    return texto;
  }

  /**
   * Abre WhatsApp con el mensaje listo para enviar. Si se pasa `ventana` (una pestaña abierta
   * durante el clic del usuario), se reutiliza para evitar el bloqueo de pop-ups que ocurre al
   * abrir ventanas después de una operación asíncrona (ej. crear el pedido).
   */
  enviar(celular: string, mensaje: string, ventana?: Window | null) {
    const numero = numeroWhatsapp(celular);
    const url = `https://wa.me/${numero}?text=${encodeURIComponent(mensaje)}`;
    if (ventana && !ventana.closed) ventana.location.href = url;
    else window.open(url, '_blank');
  }

  private formatearCantidad(valor: number): string {
    return Number.isInteger(valor) ? valor.toFixed(0) : valor.toFixed(2).replace(/0+$/, '').replace(/\.$/, '');
  }

  private formatearUnidad(unidad?: string | null): string {
    const valor = (unidad || 'u').trim().toLowerCase();
    if (['unidad', 'unidades', 'und', 'prenda', 'pieza'].includes(valor)) return 'u';
    if (['kilogramo', 'kilogramos'].includes(valor)) return 'kg';
    return valor;
  }

  private formatearFechaEntrega(fecha: string): string {
    const valor = new Intl.DateTimeFormat('es-PE', {
      weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
      hour: '2-digit', minute: '2-digit', hour12: true
    }).format(new Date(fecha));
    const ultimaComa = valor.lastIndexOf(',');
    return ultimaComa >= 0 ? `${valor.slice(0, ultimaComa)} -${valor.slice(ultimaComa + 1)}` : valor;
  }

  private formatearHorario(horario?: string | null): string {
    const lineas = (horario?.trim() || 'Lun a Sáb: 8:30 am - 7:30 pm\nDom: 8:30 am - 3:00 pm')
      .split('\n').map(linea => linea.trim()).filter(Boolean);
    return lineas.map(linea => `*• ${linea.replace(/^[•*-]\s*/, '')}*`).join('\n');
  }
}
