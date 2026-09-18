import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { InteresadoDemo, InteresadosDemoService } from '../../core/services/interesados-demo.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({ selector: 'app-plataforma-interesados', imports: [CommonModule, FormsModule, PageHeaderComponent], templateUrl: './plataforma-interesados.component.html', styleUrl: './plataforma-interesados.component.scss' })
export class PlataformaInteresadosComponent implements OnInit {
  private readonly svc = inject(InteresadosDemoService);
  readonly items = signal<InteresadoDemo[]>([]); readonly cargando = signal(true); readonly seleccion = signal<InteresadoDemo | null>(null); readonly guardando = signal(false);
  mensaje = ''; nota = ''; canal: 'WHATSAPP' | 'EMAIL' = 'WHATSAPP';
  ngOnInit() { this.recargar(); }
  recargar() { this.cargando.set(true); this.svc.listar().subscribe({ next: x => { this.items.set(x); this.cargando.set(false); }, error: () => this.cargando.set(false) }); }
  fecha(valor: string) { return new Intl.DateTimeFormat('es-PE', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(valor)); }
  pendientes() { return this.items().filter(i => i.estadoSeguimiento === 'NUEVO').length; }
  abrir(i: InteresadoDemo) { this.seleccion.set(i); this.canal = i.email ? 'EMAIL' : 'WHATSAPP'; this.nota = i.notaSeguimiento || ''; this.mensaje = i.mensajeAprobado || `Hola ${i.nombre}, somos LunaLav. Vimos que te interesó una prueba para ${i.negocio}. Te invitamos a conocer la demo: https://demo.lunalav.pe/demo. LunaLav ayuda a ordenar pedidos, caja, clientes e inventario desde S/20 al mes. ¿Te gustaría una demostración breve? Si prefieres no recibir más información, avísanos y no te contactaremos nuevamente.`; }
  cerrar() { this.seleccion.set(null); }
  aprobar() { const i = this.seleccion(); if (!i || !this.mensaje.trim()) return; this.guardando.set(true); this.svc.aprobar(i.id, this.canal, this.mensaje, this.nota).subscribe({ next: () => { this.items.update(xs => xs.map(x => x.id === i.id ? { ...x, estadoSeguimiento: 'APROBADO_PARA_ENVIO', mensajeAprobado: this.mensaje, notaSeguimiento: this.nota } : x)); this.guardando.set(false); this.cerrar(); }, error: () => this.guardando.set(false) }); }
}
