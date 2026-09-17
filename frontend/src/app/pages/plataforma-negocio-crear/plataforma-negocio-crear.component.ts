import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CrearNegocioRequest } from '../../core/models/models';
import { NegociosPlataformaService } from '../../core/services/negocios-plataforma.service';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { ErroresCampo } from '../../core/util/errores-campo';

@Component({
  selector: 'app-plataforma-negocio-crear',
  imports: [PageHeaderComponent, CommonModule, FormsModule, IconComponent],
  templateUrl: './plataforma-negocio-crear.component.html',
  styleUrl: './plataforma-negocio-crear.component.scss'
})
export class PlataformaNegocioCrearComponent {
  private readonly svc = inject(NegociosPlataformaService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  form: CrearNegocioRequest = this.formVacio();
  errorForm = signal<string | null>(null);
  readonly err = new ErroresCampo();
  guardando = signal(false);
  slugEditadoManualmente = false;

  // Tras crear, mostramos las credenciales (única vez que se ve la contraseña en claro).
  readonly creada = signal<{ nombre: string; slug: string; url: string; usuario: string; password: string } | null>(null);

  get urlAcceso() {
    const slug = this.form.slug || '<slug>';
    return `${window.location.origin}/${slug}/login`;
  }

  onNombreChange() {
    if (this.slugEditadoManualmente) return;
    this.form.slug = this.slugify(this.form.nombre);
  }

  onSlugChange() {
    this.slugEditadoManualmente = true;
    this.form.slug = this.slugify(this.form.slug);
  }

  private slugify(texto: string): string {
    return texto
      .normalize('NFD').replace(/[̀-ͯ]/g, '')
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 50);
  }

  guardar() {
    this.form = {
      ...this.form,
      nombre: this.form.nombre.trim(),
      slug: this.slugify(this.form.slug),
      rucEmpresa: this.normalizarOpcional(this.form.rucEmpresa),
      titularNombre: this.normalizarOpcional(this.form.titularNombre),
      titularEmail: this.normalizarOpcional(this.form.titularEmail),
      titularCelular: this.normalizarOpcional(this.form.titularCelular),
      sedeNombre: this.form.sedeNombre.trim(),
      adminUsuario: this.form.adminUsuario.trim(),
      adminNombreCompleto: this.form.adminNombreCompleto.trim(),
      adminEmail: this.normalizarOpcional(this.form.adminEmail)
    };

    const errs: Record<string, string> = {};
    if (!this.form.nombre?.trim()) errs['nombre'] = 'Ingresa el nombre del negocio.';
    if (!/^[a-z0-9][a-z0-9-]{1,49}$/i.test(this.form.slug)) errs['slug'] = 'Solo letras, números y guiones (2 a 50 caracteres).';
    if (!this.form.sedeNombre?.trim()) errs['sedeNombre'] = 'Ingresa el nombre de la sede.';
    if (this.form.rucEmpresa && !/^\d{11}$/.test(this.form.rucEmpresa)) errs['rucEmpresa'] = 'El RUC debe tener 11 dígitos (o déjalo vacío).';
    if (this.form.titularEmail && !this.emailValido(this.form.titularEmail)) errs['titularEmail'] = 'Correo no válido (o déjalo vacío).';
    if (!this.form.adminNombreCompleto?.trim()) errs['adminNombreCompleto'] = 'Ingresa el nombre del administrador.';
    if (!this.form.adminUsuario?.trim()) errs['adminUsuario'] = 'Ingresa el usuario del administrador.';
    else if (!/^[a-z0-9._-]{3,50}$/i.test(this.form.adminUsuario)) errs['adminUsuario'] = 'Solo letras, números, punto, guion y guion bajo (3 a 50).';
    if (this.form.adminEmail && !this.emailValido(this.form.adminEmail)) errs['adminEmail'] = 'Correo no válido (o déjalo vacío).';
    if (!this.form.adminPassword || this.form.adminPassword.length < 8) errs['adminPassword'] = 'Mínimo 8 caracteres.';

    this.err.set(errs);
    if (this.err.hay) { this.errorForm.set('Revisa los campos marcados en rojo.'); return; }

    this.guardando.set(true);
    this.errorForm.set(null);

    const passwordUsada = this.form.adminPassword;
    const usuarioUsado = this.form.adminUsuario;

    this.svc.crear(this.form).subscribe({
      next: creado => {
        this.guardando.set(false);
        this.toast.exito(`Empresa "${creado.nombre}" creada.`);
        this.creada.set({
          nombre: creado.nombre,
          slug: creado.slug,
          url: `${window.location.origin}/${creado.slug}/login`,
          usuario: usuarioUsado,
          password: passwordUsada
        });
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo crear la empresa.';
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  generarPassword() {
    // 9 chars: 3 sílabas (6) + 3 dígitos, garantiza el mínimo de 8 con letras y números.
    const s = ['la', 've', 'ro', 'mi', 'sa', 'to', 'ni', 'ba', 'lu', 'ca'];
    const azar = (max: number) => {
      const valor = new Uint32Array(1);
      crypto.getRandomValues(valor);
      return valor[0] % max;
    };
    const pick = () => s[azar(s.length)];
    const p = pick() + pick() + pick();
    this.form.adminPassword = p.charAt(0).toUpperCase() + p.slice(1) + (100 + azar(900));
  }

  copiar(texto: string) {
    navigator.clipboard?.writeText(texto).then(() => this.toast.info('Copiado'), () => {});
  }

  copiarTodo() {
    const c = this.creada();
    if (!c) return;
    this.copiar(`${c.nombre}\nAcceso: ${c.url}\nUsuario: ${c.usuario}\nContraseña: ${c.password}`);
  }

  irAlPanel() { this.router.navigate(['/plataforma']); }

  crearOtra() {
    this.creada.set(null);
    this.form = this.formVacio();
    this.slugEditadoManualmente = false;
    this.errorForm.set(null);
  }

  cancelar() { this.router.navigate(['/plataforma']); }

  private normalizarOpcional(valor: string | null | undefined): string | null {
    const limpio = valor?.trim();
    return limpio ? limpio : null;
  }

  private emailValido(valor: string | null | undefined) {
    return !valor || /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(valor);
  }

  private formVacio(): CrearNegocioRequest {
    return {
      nombre: '', slug: '', rucEmpresa: null, titularNombre: null, titularEmail: null, titularCelular: null,
      sedeNombre: 'Principal', adminUsuario: '', adminNombreCompleto: '', adminEmail: null, adminPassword: ''
    };
  }
}
