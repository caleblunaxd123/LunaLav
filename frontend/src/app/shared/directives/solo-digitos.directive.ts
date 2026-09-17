import { Directive, ElementRef, HostListener, Input, inject } from '@angular/core';
import { NgControl } from '@angular/forms';

/**
 * Fuerza que un input acepte solo dígitos (0-9) y respeta un máximo de caracteres.
 * Pensado para celulares/teléfonos: en móvil combínalo con `inputmode="numeric"` y
 * `type="tel"` para abrir el teclado numérico. Funciona con `[(ngModel)]`.
 *
 * Uso: <input appSoloDigitos [maxDigitos]="9" type="tel" inputmode="numeric" [(ngModel)]="celular">
 */
@Directive({ selector: '[appSoloDigitos]', standalone: true })
export class SoloDigitosDirective {
  private readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef);
  private readonly control = inject(NgControl, { optional: true });

  @Input() maxDigitos = 9;

  @HostListener('input')
  onInput(): void {
    const input = this.el.nativeElement;
    const limpio = (input.value || '').replace(/\D/g, '').slice(0, this.maxDigitos);
    if (limpio !== input.value) {
      input.value = limpio;
      // Propaga al modelo de ngModel (si existe); si no, el value del DOM ya quedó limpio.
      this.control?.control?.setValue(limpio);
    }
  }
}
