import { Component } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-legal',
  standalone: true,
  templateUrl: './legal.component.html',
  styleUrl: './legal.component.scss'
})
export class LegalComponent {
  readonly tipo: 'privacidad' | 'terminos';

  constructor(route: ActivatedRoute) {
    this.tipo = route.snapshot.data['tipo'] === 'terminos' ? 'terminos' : 'privacidad';
  }
}
