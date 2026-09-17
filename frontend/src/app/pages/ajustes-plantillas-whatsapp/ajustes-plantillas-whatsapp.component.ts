import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { PlantillaWhatsappEditable, PlantillasWhatsappAdminService } from '../../core/services/plantillas-whatsapp-admin.service';
import { ToastService } from '../../core/services/toast.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface EventoInfo { titulo: string; descripcion: string; placeholders: string[]; }

const EVENTOS: Record<string, EventoInfo> = {
  'INGRESO': { titulo: 'Pedido registrado', descripcion: 'Incluye detalle, saldo, horario, condiciones y seguimiento cuando corresponde.', placeholders: ['cliente', 'numero', 'negocio', 'items', 'total', 'saldo', 'entrega', 'destino', 'horario', 'seguimiento', 'condiciones'] },
  'CAMBIO_AREA': { titulo: 'Cambio de etapa', descripcion: 'Se envía cuando el pedido avanza de área en el proceso de lavado.', placeholders: ['cliente', 'numero', 'area', 'tiempoRestante'] },
  'LISTO': { titulo: 'Pedido listo para recoger', descripcion: 'Se envía cuando el pedido termina el proceso.', placeholders: ['cliente', 'numero', 'negocio'] },
  'EN_RUTA': { titulo: 'Pedido va en camino (Delivery)', descripcion: 'Aviso para el cliente cuando el repartidor sale a ruta. Incluye el link de seguimiento en vivo.', placeholders: ['cliente', 'numero', 'negocio', 'seguimiento'] },
  'DEMORA': { titulo: 'Cambio de fecha de entrega', descripcion: 'Se envía al reprogramar la hora de entrega o recojo.', placeholders: ['cliente', 'numero', 'entrega'] },
  'ENTREGADO': { titulo: 'Pedido entregado', descripcion: 'Se envía cuando el pedido se entrega al cliente.', placeholders: ['cliente', 'numero', 'total'] },
};

@Component({
  selector: 'app-ajustes-plantillas-whatsapp',
  imports: [PageHeaderComponent, CommonModule, FormsModule],
  templateUrl: './ajustes-plantillas-whatsapp.component.html',
  styleUrl: './ajustes-plantillas-whatsapp.component.scss'
})
export class AjustesPlantillasWhatsappComponent implements OnInit {
  private readonly svc = inject(PlantillasWhatsappAdminService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly plantillas = signal<PlantillaWhatsappEditable[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly guardandoId = signal<number | null>(null);

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.svc.listar().subscribe({
      next: list => { this.plantillas.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar las plantillas.'));
      }
    });
  }

  info(evento: string): EventoInfo {
    return EVENTOS[evento] ?? { titulo: evento, descripcion: '', placeholders: [] };
  }

  guardar(p: PlantillaWhatsappEditable) {
    if (!p.mensaje.trim()) {
      this.toast.advertencia('El mensaje no puede estar vacío.');
      return;
    }
    this.guardandoId.set(p.id);
    this.svc.actualizar(p.id, { mensaje: p.mensaje.trim(), activa: p.activa }).subscribe({
      next: () => {
        this.guardandoId.set(null);
        this.toast.exito('Plantilla actualizada');
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoId.set(null);
        this.toast.desdeHttp(err, 'No se pudo guardar la plantilla.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
