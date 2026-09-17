import { Component, inject } from '@angular/core';
import { DemoPreviewService } from '../../core/services/demo-preview.service';
import { DemoBienvenidaComponent } from '../demo-bienvenida/demo-bienvenida.component';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [DemoBienvenidaComponent],
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss'
})
export class LandingComponent {
  private readonly demo = inject(DemoPreviewService);
  readonly anio = new Date().getFullYear();
  readonly demoUrl = 'https://demo.lunalav.pe/demo';
  readonly esDemo = this.demo.esDemoPublica();
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
        texto: 'LunaLav Básico cuesta S/20, LunaLav Factura S/50 y Multisede empieza en S/80 al mes.',
        accion: 'Ver planes',
        url: '#precios'
      },
      demo: {
        texto: 'Puedes recorrer una lavandería de prueba con datos ficticios. No necesitas registrar una tarjeta.',
        accion: 'Abrir demo',
        url: this.demoUrl
      },
      plan: {
        texto: 'Elige Básico para pedidos y caja; Factura para emitir comprobantes electrónicos; o Multisede si administras dos o más locales.',
        accion: 'Comparar planes',
        url: '#precios'
      },
      persona: {
        texto: 'Cuéntanos cómo trabaja tu lavandería y te orientaremos sin compromiso.',
        accion: 'Escribir a LunaLav',
        url: 'mailto:contacto@lunalav.pe?subject=Quiero conversar sobre LunaLav'
      }
    }[tema];

    this.chatRespuesta = respuestas.texto;
    this.chatAccionTexto = respuestas.accion;
    this.chatAccionUrl = respuestas.url;
  }
}
