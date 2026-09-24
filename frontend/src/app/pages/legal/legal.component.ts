import { Component } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-legal',
  standalone: true,
  templateUrl: './legal.component.html',
  styleUrl: './legal.component.scss'
})
export class LegalComponent {
  readonly tipo: 'privacidad' | 'terminos' | 'eliminar-cuenta';

  constructor(route: ActivatedRoute) {
    const tipo = route.snapshot.data['tipo'];
    this.tipo = tipo === 'terminos' || tipo === 'eliminar-cuenta' ? tipo : 'privacidad';
  }
}
