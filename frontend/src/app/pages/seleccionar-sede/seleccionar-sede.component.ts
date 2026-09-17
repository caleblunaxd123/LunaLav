import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Sede } from '../../core/models/models';
import { AuthService } from '../../core/services/auth.service';
import { SedesService } from '../../core/services/sedes.service';

@Component({
  selector: 'app-seleccionar-sede',
  imports: [CommonModule],
  templateUrl: './seleccionar-sede.component.html',
  styleUrl: './seleccionar-sede.component.scss'
})
export class SeleccionarSedeComponent implements OnInit {
  private readonly sedesSvc = inject(SedesService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly sedes = signal<Sede[]>([]);
  readonly cargando = signal(true);
  readonly seleccionando = signal<number | null>(null);
  readonly error = signal<string | null>(null);

  ngOnInit() {
    // Si el usuario ya tiene una sede fija asignada, esta pantalla no aplica.
    if (this.auth.usuario()?.sedeId) {
      this.router.navigate(['/inicio']);
      return;
    }
    this.sedesSvc.listar().subscribe({
      next: list => {
        this.sedes.set(list.filter(s => s.activo));
        this.cargando.set(false);
      },
      error: () => {
        this.cargando.set(false);
        this.error.set('No se pudo cargar la lista de sedes.');
      }
    });
  }

  elegir(sede: Sede) {
    this.seleccionando.set(sede.id);
    this.auth.cambiarSede(sede.id).subscribe({
      next: () => this.router.navigate(['/inicio']),
      error: (err: HttpErrorResponse) => {
        this.seleccionando.set(null);
        this.error.set(err.error?.mensaje ?? 'No se pudo seleccionar la sede.');
      }
    });
  }

  cerrarSesion() { this.auth.logout(); }
}
