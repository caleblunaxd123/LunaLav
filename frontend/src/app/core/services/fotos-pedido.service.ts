import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';

export type MomentoFoto = 'RECEPCION' | 'ENTREGA' | 'OTRO';

export interface FotoPedido {
  id: number;
  momento: MomentoFoto;
  fecha: string;
  tamanoBytes: number;
}

@Injectable({ providedIn: 'root' })
export class FotosPedidoService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  listar(pedidoId: number): Observable<FotoPedido[]> {
    return this.http.get<FotoPedido[]>(`${this.base}/pedidos/${pedidoId}/fotos`);
  }

  subir(pedidoId: number, imagen: Blob, momento: MomentoFoto): Observable<FotoPedido> {
    const fd = new FormData();
    fd.append('archivo', imagen, 'foto.jpg');
    fd.append('momento', momento);
    return this.http.post<FotoPedido>(`${this.base}/pedidos/${pedidoId}/fotos`, fd);
  }

  eliminar(pedidoId: number, fotoId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/pedidos/${pedidoId}/fotos/${fotoId}`);
  }

  /**
   * Descarga la imagen (con el token de sesión, vía XHR) y devuelve un blob URL para usar en
   * un <img>. Un <img src> normal no manda el header de autorización, por eso se baja así.
   */
  urlArchivo(pedidoId: number, fotoId: number): Observable<string> {
    return this.http
      .get(`${this.base}/pedidos/${pedidoId}/fotos/${fotoId}/archivo`, { responseType: 'blob' })
      .pipe(map(blob => URL.createObjectURL(blob)));
  }

  /**
   * Comprime/redimensiona la foto en el navegador ANTES de subirla: baja una foto de celular
   * de varios MB a ~200-400 KB. Ahorra datos, disco del servidor y hace la subida instantánea.
   */
  async comprimir(archivo: File, ladoMax = 1600, calidad = 0.72): Promise<Blob> {
    // Si ya es pequeña y jpeg, se sube tal cual.
    const bitmap = await this.cargarImagen(archivo);
    const escala = Math.min(1, ladoMax / Math.max(bitmap.width, bitmap.height));
    const ancho = Math.round(bitmap.width * escala);
    const alto = Math.round(bitmap.height * escala);

    const canvas = document.createElement('canvas');
    canvas.width = ancho;
    canvas.height = alto;
    const ctx = canvas.getContext('2d');
    if (!ctx) return archivo;
    ctx.drawImage(bitmap, 0, 0, ancho, alto);
    if ('close' in bitmap) (bitmap as ImageBitmap).close?.();

    return new Promise<Blob>(resolve => {
      canvas.toBlob(
        blob => resolve(blob ?? archivo),
        'image/jpeg',
        calidad
      );
    });
  }

  private async cargarImagen(archivo: File): Promise<ImageBitmap | HTMLImageElement> {
    if ('createImageBitmap' in window) {
      try { return await createImageBitmap(archivo); } catch { /* fallback abajo */ }
    }
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = reject;
      img.src = URL.createObjectURL(archivo);
    });
  }
}
