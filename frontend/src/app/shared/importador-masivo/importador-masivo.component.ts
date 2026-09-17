import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../../../environments/environment';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../icon/icon.component';

export interface ColumnaImport {
  clave: string;
  etiqueta: string;
  requerido?: boolean;
  tipo?: 'texto' | 'numero' | 'telefono' | 'dni';
  min?: number;
  max?: number;
}

export interface FilaImport {
  fila: number;
  valores: Record<string, string | number | null>;
  estado: 'ok' | 'error';
  motivo: string;
}

/**
 * Modal reutilizable de carga masiva: sirve para servicios, clientes y cualquier catálogo futuro.
 * Recibe el esquema de columnas y emite las filas válidas; el componente padre hace el POST.
 * Acepta archivo Excel (.xlsx, validado por el servidor), CSV o texto pegado desde Excel.
 */
@Component({
  selector: 'app-importador-masivo',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './importador-masivo.component.html',
  styleUrl: './importador-masivo.component.scss'
})
export class ImportadorMasivoComponent {
  private readonly http = inject(HttpClient);
  private readonly toast = inject(ToastService);

  @Input() titulo = 'Importar';
  @Input({ required: true }) columnas: ColumnaImport[] = [];
  /** Tipo de plantilla para /api/plantillas/{tipo} (ej. 'servicios', 'clientes'). */
  @Input() plantillaTipo?: string;
  /** Nombres ya existentes/clave para marcar duplicados en la previsualización (opcional). */
  @Input() existentes: Set<string> | null = null;
  /** Columna cuyo valor se compara contra "existentes" (por defecto la primera). */
  @Input() claveDuplicado?: string;
  @Input() procesando = false;

  @Output() cerrar = new EventEmitter<void>();
  @Output() confirmar = new EventEmitter<Record<string, string | number | null>[]>();

  readonly texto = signal('');
  readonly nombreArchivo = signal<string | null>(null);
  readonly descargando = signal(false);
  readonly leyendoArchivo = signal(false);

  readonly filas = computed<FilaImport[]>(() => {
    const raw = this.texto();
    if (!raw.trim()) return [];
    const tabla = this.parsearTabla(raw);
    const vistos = new Set<string>();
    return tabla.map((cols, i) => this.evaluarFila(cols, i, vistos));
  });
  readonly validas = computed(() => this.filas().filter(f => f.estado === 'ok'));
  readonly conError = computed(() => this.filas().filter(f => f.estado === 'error'));

  onArchivo(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) {
      this.toast.error('El archivo no puede superar los 5 MB.');
      input.value = '';
      return;
    }

    if (/\.xlsx$/i.test(file.name)) {
      this.cargarExcel(file);
    } else if (/\.(csv|txt)$/i.test(file.name)) {
      this.nombreArchivo.set(file.name);
      const reader = new FileReader();
      reader.onload = () => this.texto.set(String(reader.result ?? ''));
      reader.onerror = () => this.toast.error('No se pudo leer el archivo.');
      reader.readAsText(file, 'utf-8');
    } else {
      this.toast.error('Formato no compatible. Usa un archivo .xlsx, .csv o .txt.');
    }
    input.value = '';
  }

  private cargarExcel(file: File) {
    if (this.leyendoArchivo()) return;
    const datos = new FormData();
    datos.append('archivo', file, file.name);
    this.leyendoArchivo.set(true);

    this.http.post<{ texto: string }>(`${environment.apiUrl}/plantillas/leer-excel`, datos).subscribe({
      next: respuesta => {
        this.leyendoArchivo.set(false);
        this.nombreArchivo.set(file.name);
        this.texto.set(respuesta.texto ?? '');
        if (!respuesta.texto?.trim()) this.toast.info('El Excel no contiene filas para importar.');
      },
      error: (error: HttpErrorResponse) => {
        this.leyendoArchivo.set(false);
        this.nombreArchivo.set(null);
        this.texto.set('');
        this.toast.error(error.error?.mensaje ?? 'No se pudo leer el Excel. Verifica que sea un .xlsx valido.');
      }
    });
  }

  descargarPlantilla() {
    if (!this.plantillaTipo || this.descargando()) return;
    this.descargando.set(true);
    this.http.get(`${environment.apiUrl}/plantillas/${this.plantillaTipo}`, { responseType: 'blob' }).subscribe({
      next: blob => {
        this.descargando.set(false);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `plantilla-${this.plantillaTipo}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => { this.descargando.set(false); this.toast.error('No se pudo descargar la plantilla.'); }
    });
  }

  emitirConfirmar() {
    const validas = this.validas();
    if (validas.length === 0) {
      this.toast.info('No hay filas válidas para importar.');
      return;
    }
    this.confirmar.emit(validas.map(f => f.valores));
  }

  // ---------- Parseo y validación ----------
  private parsearTabla(texto: string): string[][] {
    const lineas = texto.replace(/\r/g, '').split('\n').filter(l => l.trim().length > 0);
    if (lineas.length === 0) return [];
    // Separador: tab (pegado desde Excel), ; (CSV con configuración regional es-PE) o , (CSV estándar).
    const sep = lineas[0].includes('\t') ? '\t' : lineas[0].includes(';') ? ';' : ',';
    const filas = lineas.map(l => l.split(sep).map(c => c.trim().replace(/^"|"$/g, '')));

    // Se ancla en la fila de ENCABEZADO (la que trae los títulos de columna) y se toma solo lo que
    // va debajo. Así se descartan de raíz el título, las instrucciones y cualquier fila previa de la
    // plantilla — sin heurísticas frágiles que podían dejar pasar filas basura (ej. importar un
    // cliente llamado "Plantilla de clientes"). Si no hay encabezado (pegado directo sin títulos),
    // se usan todas las filas y la validación por fila marca lo que esté mal.
    const primeraEtiqueta = this.normalizar(this.columnas[0]?.etiqueta ?? '');
    const alias = ['nombre', 'servicio', 'cliente'];
    const idxHeader = filas.findIndex(cols => {
      const c0 = this.normalizar(cols[0] ?? '');
      return c0.length > 0 && (c0 === primeraEtiqueta || alias.includes(c0));
    });
    const datos = idxHeader >= 0 ? filas.slice(idxHeader + 1) : filas;

    // Ignora filas totalmente vacías (celdas de relleno de la plantilla).
    return datos.filter(cols => cols.some(v => (v ?? '').trim().length > 0));
  }

  private parsearNumero(valor: string): number {
    let s = (valor ?? '').replace(/[^0-9.,-]/g, '').trim();
    if (!s) return NaN;
    const coma = s.includes(','), punto = s.includes('.');
    if (coma && punto) s = s.replace(/\./g, '').replace(',', '.');
    else if (coma) s = s.replace(',', '.');
    const n = Number(s);
    return Number.isFinite(n) ? n : NaN;
  }

  private normalizar(v: string): string {
    return v.normalize('NFD').replace(/[̀-ͯ]/g, '').trim().toLowerCase();
  }

  private evaluarFila(cols: string[], indice: number, vistos: Set<string>): FilaImport {
    const valores: Record<string, string | number | null> = {};
    let motivo = '';

    for (let i = 0; i < this.columnas.length; i++) {
      const col = this.columnas[i];
      const bruto = (cols[i] ?? '').trim();

      if (col.tipo === 'numero') {
        const n = this.parsearNumero(bruto);
        valores[col.clave] = Number.isFinite(n) ? n : null;
        if (col.requerido || bruto.length > 0) {
          if (!Number.isFinite(n)) motivo ||= `${col.etiqueta} inválido`;
          else if ((col.min != null && n < col.min) || (col.max != null && n > col.max)) motivo ||= `${col.etiqueta} fuera de rango`;
        }
      } else if (col.tipo === 'telefono') {
        const dig = bruto.replace(/\D/g, '');
        valores[col.clave] = dig || null;
        if (dig && !/^\d{4,20}$/.test(dig)) motivo ||= `${col.etiqueta} inválido`;
        if (col.requerido && !dig) motivo ||= `Falta ${col.etiqueta}`;
      } else if (col.tipo === 'dni') {
        const dig = bruto.replace(/\D/g, '');
        valores[col.clave] = dig || null;
        if (dig && !/^\d{8}$/.test(dig)) motivo ||= `${col.etiqueta} inválido (8 dígitos)`;
      } else {
        valores[col.clave] = bruto || null;
        if (col.requerido && bruto.length < 2) motivo ||= `Falta ${col.etiqueta}`;
      }
    }

    // Duplicado dentro del archivo o contra los existentes SOLO por la clave que el padre indique
    // explícitamente (ej. servicios usa "nombre", que sí es único). Para clientes no se pasa clave:
    // dos personas pueden llamarse igual, así que el aviso de duplicado lo resuelve el backend por
    // celular/DNI y aquí no se marca un falso positivo por nombre.
    const claveDup = this.claveDuplicado;
    const valorDup = claveDup ? valores[claveDup] : null;
    if (!motivo && valorDup != null && String(valorDup).length > 0) {
      const k = this.normalizar(String(valorDup));
      if (vistos.has(k)) motivo = 'Repetido en el archivo';
      else {
        vistos.add(k);
        if (this.existentes?.has(k)) motivo = 'Ya existe';
      }
    }

    return { fila: indice + 1, valores, estado: motivo ? 'error' : 'ok', motivo };
  }
}
