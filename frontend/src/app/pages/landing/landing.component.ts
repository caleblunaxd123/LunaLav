import { Component } from '@angular/core';

@Component({
  selector: 'app-landing',
  standalone: true,
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss'
})
export class LandingComponent {
  readonly anio = new Date().getFullYear();
  readonly demoUrl = 'https://demo.lunalav.pe/demo/login';
  menuAbierto = false;
  chatAbierto = false;
  chatRespuesta = '¡Hola! Soy el asistente de LunaLav. ¿Qué te gustaría conocer?';
  chatAccionTexto = '';
  chatAccionUrl = '';

  alternarMenu(): void {
    this.menuAbierto = !this.menuAbierto;
  }

  cerrarMenu(): void {
    this.menuAbierto = false;
  }

  alternarChat(): void {
    this.chatAbierto = !this.chatAbierto;
  }

  responderChat(tema: 'precios' | 'demo' | 'plan' | 'persona'): void {
    const respuestas = {
      precios: {
        texto: 'LunaLav Básico cuesta S/20 al mes. Si necesitas emitir comprobantes electrónicos, LunaLav Factura cuesta S/50 al mes.',
        accion: 'Ver planes',
        url: '#precios'
      },
      demo: {
        texto: 'Puedes recorrer una lavandería de prueba con datos ficticios. No necesitas registrar una tarjeta.',
        accion: 'Abrir demo',
        url: this.demoUrl
      },
      plan: {
        texto: 'Elige Básico para administrar pedidos y caja. Elige LunaLav Factura si también necesitas emitir boletas y facturas electrónicas.',
        accion: 'Comparar planes',
        url: '#precios'
      },
      persona: {
        texto: 'Cuéntanos cómo trabaja tu lavandería y te orientaremos sin compromiso.',
        accion: 'Escribir a LunaLav',
        url: 'mailto:hola@lunalav.pe?subject=Quiero conversar sobre LunaLav'
      }
    }[tema];

    this.chatRespuesta = respuestas.texto;
    this.chatAccionTexto = respuestas.accion;
    this.chatAccionUrl = respuestas.url;
  }
}
