import { CommonModule } from '@angular/common';
import { Component, ElementRef, HostListener, Input, computed, inject, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { Pais, PAISES, PERU, paisPorPrefijo } from './paises';

/**
 * Selector de teléfono con combo de país (bandera + código) + número, estilo formulario
 * profesional. Es un ControlValueAccessor: se usa con [(ngModel)] igual que un <input>.
 *
 * Valor que entrega/recibe (compatibilidad con datos existentes):
 *  - País Perú  → guarda el número nacional pelado (ej. "987654321"), igual que siempre.
 *  - Otro país  → guarda "+<código><número>" en E.164 (ej. "+573001234567").
 * Al recibir un valor con '+', reconstruye el país por el prefijo más largo.
 */
@Component({
  selector: 'app-telefono-pais',
  standalone: true,
  imports: [CommonModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: TelefonoPaisComponent, multi: true }],
  template: `
    <div class="tp" [class.tp--disabled]="disabled">
      <button type="button" class="tp__pais" (click)="toggle()" [disabled]="disabled"
              [attr.aria-expanded]="abierto()" aria-haspopup="listbox" title="Código de país">
        <span class="tp__bandera" [style.background-image]="banderaUrl(pais().iso)"></span>
        <span class="tp__dial">+{{ pais().dial }}</span>
        <span class="tp__caret" aria-hidden="true">▾</span>
      </button>

      <input class="tp__num" type="tel" inputmode="numeric" [id]="inputId"
             [attr.maxlength]="18"
             [placeholder]="placeholderActual()"
             [value]="numero()" (input)="onNumero($any($event.target).value)" (blur)="onTouched()"
             [disabled]="disabled" />

      @if (abierto()) {
        <div class="tp__menu" role="listbox">
          <input #buscador class="tp__buscar" type="text" placeholder="Buscar país o código…"
                 [value]="busqueda()" (input)="busqueda.set($any($event.target).value)"
                 (click)="$event.stopPropagation()" autofocus />
          <ul class="tp__lista">
            @for (p of filtrados(); track p.iso + p.dial) {
              <li>
                <button type="button" class="tp__op" [class.is-sel]="p.iso === pais().iso" (click)="elegir(p)">
                  <span class="tp__bandera" [style.background-image]="banderaUrl(p.iso)"></span>
                  <span class="tp__op-nombre">{{ p.nombre }}</span>
                  <span class="tp__op-dial">+{{ p.dial }}</span>
                </button>
              </li>
            } @empty {
              <li class="tp__vacio">Sin resultados</li>
            }
          </ul>
        </div>
      }
    </div>
  `,
  styles: [`
    /* El componente reproduce el estilo estándar de campo de la app (borde, alto, radio),
       porque la encapsulación de Angular impide que el CSS de input de cada página llegue aquí. */
    .tp { position: relative; display: flex; gap: 6px; align-items: stretch; width: 100%; }
    :host { display: block; width: 100%; }
    :host(.invalid) .tp__num, :host(.invalid) .tp__pais { border-color: #e04b3f; background: #fff6f5; }
    .tp__pais {
      display: inline-flex; align-items: center; gap: 5px; padding: 0 11px; cursor: pointer;
      border: 1px solid var(--gris-borde, #cbd5e1); border-radius: 8px; background: #fff;
      font-size: 14px; white-space: nowrap; color: #1e293b; min-height: 40px;
    }
    .tp__pais:hover { border-color: #94a3b8; }
    .tp__bandera {
      display: inline-block; width: 22px; height: 16px; flex: 0 0 auto;
      background-size: cover; background-position: center; background-repeat: no-repeat;
      border-radius: 2px; box-shadow: 0 0 0 1px rgba(15,23,42,.10);
    }
    .tp__dial { font-weight: 600; }
    .tp__caret { font-size: 10px; color: #64748b; }
    .tp__num {
      flex: 1 1 auto; min-width: 0; width: 100%; box-sizing: border-box; min-height: 40px;
      padding: 9px 12px; border: 1px solid var(--gris-borde, #cbd5e1); border-radius: 8px;
      font-size: 14px; font-family: inherit; color: var(--ink, #1c2733); background: #fff;
    }
    .tp__num::placeholder { color: #94a3b8; }
    .tp--disabled { opacity: .6; }
    .tp__menu {
      position: absolute; z-index: 40; top: calc(100% + 4px); left: 0; width: min(320px, 92vw);
      background: #fff; border: 1px solid #e2e8f0; border-radius: 10px;
      box-shadow: 0 12px 28px rgba(15,23,42,.16); overflow: hidden;
    }
    .tp__buscar { width: 100%; border: none; border-bottom: 1px solid #eef2f6; padding: 10px 12px; font-size: 13.5px; outline: none; }
    .tp__lista { list-style: none; margin: 0; padding: 4px; max-height: 260px; overflow-y: auto; }
    .tp__op {
      display: flex; align-items: center; gap: 9px; width: 100%; padding: 8px 10px; cursor: pointer;
      border: none; background: transparent; border-radius: 7px; font-size: 13.5px; text-align: left; color: #1e293b;
    }
    .tp__op:hover { background: #f1f5f9; }
    .tp__op.is-sel { background: #eff6ff; font-weight: 600; }
    .tp__op-nombre { flex: 1 1 auto; }
    .tp__op-dial { color: #64748b; font-variant-numeric: tabular-nums; }
    .tp__vacio { padding: 12px; color: #94a3b8; font-size: 13px; text-align: center; }
  `]
})
export class TelefonoPaisComponent implements ControlValueAccessor {
  @Input() inputId = 'telefono';

  private readonly host = inject(ElementRef);

  readonly pais = signal<Pais>(PERU);
  readonly numero = signal<string>('');   // solo dígitos nacionales
  readonly abierto = signal(false);
  readonly busqueda = signal('');
  disabled = false;

  readonly placeholderActual = computed(() => this.pais().dial === '51' ? '987654321' : 'Número');

  /** Ruta absoluta de la bandera (SVG servido desde /flags por la propia app). */
  banderaUrl(iso: string): string {
    return `url(/flags/${iso.toLowerCase()}.svg)`;
  }

  readonly filtrados = computed(() => {
    const q = this.busqueda().trim().toLowerCase();
    if (!q) return PAISES;
    const qDial = q.replace(/[^0-9]/g, '');
    return PAISES.filter(p =>
      p.nombre.toLowerCase().includes(q) || (qDial.length > 0 && p.dial.includes(qDial)));
  });

  private onChange: (v: string) => void = () => {};
  onTouched: () => void = () => {};

  writeValue(value: string | null): void {
    const raw = (value ?? '').trim();
    if (raw.startsWith('+')) {
      const dig = raw.replace(/\D/g, '');
      const p = paisPorPrefijo(dig);
      if (p) { this.pais.set(p); this.numero.set(dig.slice(p.dial.length)); return; }
      this.pais.set(PERU); this.numero.set(dig);
      return;
    }
    // Sin '+': número peruano histórico (o vacío).
    this.pais.set(PERU);
    this.numero.set(raw.replace(/\D/g, ''));
  }
  registerOnChange(fn: (v: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(v: boolean): void { this.disabled = v; }

  private emitir(): void {
    const nac = this.numero().replace(/\D/g, '');
    if (!nac) { this.onChange(''); return; }
    this.onChange(this.pais().dial === '51' ? nac : '+' + this.pais().dial + nac);
  }

  onNumero(valor: string): void {
    // Sin cantidad fija de dígitos: se acepta cualquier número (Perú o extranjero).
    this.numero.set(valor.replace(/\D/g, '').slice(0, 18));
    this.emitir();
  }

  elegir(p: Pais): void {
    this.pais.set(p);
    this.abierto.set(false);
    this.busqueda.set('');
    this.numero.set(this.numero().slice(0, 18));
    this.emitir();
    this.onTouched();
  }

  toggle(): void { if (!this.disabled) this.abierto.update(a => !a); }

  @HostListener('document:click', ['$event'])
  fueraClick(e: MouseEvent): void {
    if (this.abierto() && !this.host.nativeElement.contains(e.target as Node)) this.abierto.set(false);
  }
}
