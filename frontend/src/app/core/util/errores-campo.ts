import { signal } from '@angular/core';

/**
 * Manejo de errores de validación por campo, reutilizable en cualquier formulario.
 * Permite mostrar un mensaje claro debajo de cada input y resaltarlo en rojo,
 * en vez de un único mensaje genérico.
 *
 * Uso en un componente:
 *   readonly err = new ErroresCampo();
 *   // al validar:
 *   const errs: Record<string,string> = {};
 *   if (!nombre) errs['nombre'] = 'Ingresa el nombre.';
 *   this.err.set(errs);
 *   if (this.err.hay) return;
 *
 * En la plantilla:
 *   <input [class.invalid]="err.get('nombre')" (ngModelChange)="err.limpiar('nombre')" ...>
 *   @if (err.get('nombre')) { <small class="campo-error">{{ err.get('nombre') }}</small> }
 */
export class ErroresCampo {
  private readonly _errores = signal<Record<string, string>>({});

  /** Mensaje del campo (o undefined si está bien). Usar en [class.invalid] y en el <small>. */
  get(campo: string): string | undefined { return this._errores()[campo]; }

  /** true si hay al menos un error. */
  get hay(): boolean { return Object.keys(this._errores()).length > 0; }

  /** Reemplaza todos los errores (lo típico al validar en el submit). */
  set(errores: Record<string, string>): void { this._errores.set(errores); }

  /** Marca un único campo (p. ej. al mapear un error del backend). */
  marcar(campo: string, mensaje: string): void {
    this._errores.set({ ...this._errores(), [campo]: mensaje });
  }

  /** Borra el error de un campo (llamar en (ngModelChange) para que desaparezca al corregir). */
  limpiar(campo: string): void {
    const e = this._errores();
    if (e[campo] !== undefined) { const { [campo]: _omit, ...resto } = e; this._errores.set(resto); }
  }

  /** Limpia todos los errores (al abrir/cerrar el formulario o tras guardar bien). */
  limpiarTodo(): void { this._errores.set({}); }
}

/** Expresiones regulares comunes, iguales a las del backend. */
export const RE = {
  email: /^[^@\s]+@[^@\s]+\.[^@\s]+$/,
  // Celular: solo números (Perú o extranjero con + y código de país), sin cantidad fija.
  celular: /^\+?\d{4,20}$/,
  passwordSegura: /^(?=.*[A-Za-z])(?=.*\d).{8,}$/,
};
