import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { InteresadoDemo, InteresadosDemoService } from '../../core/services/interesados-demo.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({ selector: 'app-plataforma-interesados', imports: [CommonModule, PageHeaderComponent], templateUrl: './plataforma-interesados.component.html', styleUrl: './plataforma-interesados.component.scss' })
export class PlataformaInteresadosComponent implements OnInit {
  private readonly svc = inject(InteresadosDemoService);
  readonly items = signal<InteresadoDemo[]>([]); readonly cargando = signal(true);
  ngOnInit() { this.svc.listar().subscribe({ next: x => { this.items.set(x); this.cargando.set(false); }, error: () => this.cargando.set(false) }); }
  fecha(valor: string) { return new Intl.DateTimeFormat('es-PE', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(valor)); }
}
