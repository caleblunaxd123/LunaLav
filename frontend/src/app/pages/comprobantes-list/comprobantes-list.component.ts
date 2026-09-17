import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Comprobante, FacturacionService, GuiaRemisionPayload, KpiComprobantesMes } from '../../core/services/facturacion.service';
import { ToastService } from '../../core/services/toast.service';
import { fechaLocalIso } from '../../core/util/fecha-local';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { Subscription, interval } from 'rxjs';

@Component({
  selector: 'app-comprobantes-list',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './comprobantes-list.component.html',
  styleUrl: './comprobantes-list.component.scss'
})
export class ComprobantesListComponent implements OnInit, OnDestroy {
  private readonly svc = inject(FacturacionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private poll?: Subscription;

  readonly comprobantes = signal<Comprobante[]>([]);
  readonly total = signal(0);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly descargandoId = signal<number | null>(null);
  readonly reenviandoId = signal<number | null>(null);
  readonly confirmarAnular = signal<Comprobante | null>(null);
  readonly motivoAnulacion = signal('');
  readonly detalle = signal<Comprobante | null>(null);

  // ---------- Nota de crédito (catálogo 09 de SUNAT) ----------
  readonly notaCredito = signal<Comprobante | null>(null);
  readonly ncMotivoCodigo = signal('01');
  readonly ncMotivo = signal('');
  readonly emitiendoNc = signal(false);
  readonly motivosNc = [
    { codigo: '01', texto: 'Anulación de la operación' },
    { codigo: '02', texto: 'Anulación por error en el RUC' },
    { codigo: '03', texto: 'Corrección por error en la descripción' },
    { codigo: '06', texto: 'Devolución total' },
    { codigo: '07', texto: 'Devolución por ítem' },
    { codigo: '05', texto: 'Descuento global' },
    { codigo: '10', texto: 'Otros conceptos' },
  ];

  // ---------- Nota de débito (catálogo 10 de SUNAT) ----------
  readonly notaDebito = signal<Comprobante | null>(null);
  readonly ndMotivoCodigo = signal('01');
  readonly ndMotivo = signal('');
  readonly emitiendoNd = signal(false);
  readonly motivosNd = [
    { codigo: '01', texto: 'Intereses por mora' },
    { codigo: '02', texto: 'Aumento en el valor' },
    { codigo: '03', texto: 'Penalidades u otros conceptos' },
  ];

  // ---------- Guía de remisión (GRE, catálogo 20 de traslado) ----------
  readonly guia = signal<Comprobante | null>(null);
  readonly emitiendoGuia = signal(false);
  readonly motivosTraslado = [
    { codigo: '01', texto: 'Venta' },
    { codigo: '14', texto: 'Venta sujeta a confirmación' },
    { codigo: '02', texto: 'Compra' },
    { codigo: '04', texto: 'Traslado entre establecimientos de la misma empresa' },
    { codigo: '18', texto: 'Traslado emisor itinerante' },
    { codigo: '19', texto: 'Traslado a zona primaria' },
    { codigo: '13', texto: 'Otros' },
  ];
  guiaForm: GuiaRemisionPayload = this.guiaVacia();
  private guiaVacia(): GuiaRemisionPayload {
    return {
      motivoCodigo: '01', motivoDescripcion: '', pesoBrutoTotal: 1, unidadPeso: 'KGM', numeroBultos: 1,
      fechaInicioTraslado: fechaLocalIso(), modalidadTransporte: '02',
      partidaUbigeo: '', partidaDireccion: '', llegadaUbigeo: '', llegadaDireccion: '',
      transportistaNumDoc: '', transportistaRazonSocial: '',
      vehiculoPlaca: '', conductorTipoDoc: '1', conductorNumDoc: '', conductorNombres: '', conductorLicencia: ''
    };
  }
  readonly busqueda = signal('');
  readonly tipo = signal('');
  readonly estado = signal('');
  readonly desde = signal('');
  readonly hasta = signal('');

  // ---------- KPI mensual de boletas/facturas ----------
  readonly kpi = signal<KpiComprobantesMes[]>([]);
  readonly mesActual = computed(() => this.kpi().at(-1) ?? null);
  readonly maxMontoSerie = computed(() => Math.max(1, ...this.kpi().map(m => m.totalMonto)));
  private readonly nombresMes = ['', 'Ene', 'Feb', 'Mar', 'Abr', 'May', 'Jun', 'Jul', 'Ago', 'Sep', 'Oct', 'Nov', 'Dic'];
  nombreMes(mes: number): string { return this.nombresMes[mes] ?? ''; }

  ngOnInit() {
    this.cargar();
    this.cargarRespaldoInfo();
    this.svc.kpiMensual(6).subscribe({ next: k => this.kpi.set(k), error: () => {} });
    this.poll = interval(15_000).subscribe(() => {
      const pendientes = this.comprobantes().filter(c => c.estado === 'PENDIENTE' && !c.esSimulado).slice(0, 5);
      pendientes.forEach(c => this.svc.sincronizar(c.id).subscribe({ next: actualizado => {
        this.comprobantes.update(lista => lista.map(x => x.id === actualizado.id ? actualizado : x));
      }, error: () => {} }));
    });
  }

  ngOnDestroy() { this.poll?.unsubscribe(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.svc.listarComprobantes(this.pagina(), this.tamanoPagina(), {
      busqueda: this.busqueda(), tipo: this.tipo(), estado: this.estado(), desde: this.desde(), hasta: this.hasta()
    }).subscribe({
      next: r => { this.comprobantes.set(r.items); this.total.set(r.total); this.cargando.set(false); },
      error: () => { this.cargando.set(false); this.error.set('No se pudo cargar el listado de comprobantes.'); }
    });
  }

  cambiarPagina(p: number) { this.pagina.set(p); this.cargar(); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); this.cargar(); }

  verPdf(c: Comprobante) {
    this.descargandoId.set(c.id);
    this.svc.descargarPdf(c.id).subscribe({
      next: blob => {
        this.descargandoId.set(null);
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
      },
      error: () => { this.descargandoId.set(null); this.toast.error('No se pudo generar el PDF.'); }
    });
  }

  // ---- Respaldo local (SUNAT) ----
  readonly carpetaRespaldo = signal<string>('');
  readonly respaldando = signal(false);

  cargarRespaldoInfo() {
    this.svc.respaldoInfo().subscribe({ next: r => this.carpetaRespaldo.set(r.carpeta), error: () => {} });
  }

  respaldarTodos() {
    this.respaldando.set(true);
    this.svc.respaldarTodos().subscribe({
      next: r => {
        this.respaldando.set(false);
        this.carpetaRespaldo.set(r.carpeta);
        this.toast.exito(`${r.respaldados} comprobante(s) respaldado(s) en disco.`);
      },
      error: () => { this.respaldando.set(false); this.toast.error('No se pudo generar el respaldo.'); }
    });
  }

  reenviar(c: Comprobante) {
    this.reenviandoId.set(c.id);
    this.svc.reenviar(c.id).subscribe({
      next: actualizado => {
        this.reenviandoId.set(null);
        if (actualizado.estado === 'ACEPTADO') this.toast.exito('SUNAT aceptó el comprobante.');
        else this.toast.advertencia(actualizado.descripcionRespuestaSunat ?? 'SUNAT volvió a rechazar el comprobante.');
        this.cargar();
      },
      error: (err) => {
        this.reenviandoId.set(null);
        this.toast.desdeHttp(err, 'No se pudo reenviar el comprobante.');
      }
    });
  }

  pedirAnular(c: Comprobante) { this.motivoAnulacion.set(''); this.confirmarAnular.set(c); }

  confirmarAnularOk() {
    const c = this.confirmarAnular();
    if (!c) return;
    const motivo = this.motivoAnulacion().trim();
    if (motivo.length < 3) { this.toast.advertencia('Indica un motivo de al menos 3 caracteres.'); return; }
    this.svc.anular(c.id, motivo).subscribe({
      next: actualizado => { this.toast.info(`Anulación: ${actualizado.estadoAnulacion ?? actualizado.estado}`); this.confirmarAnular.set(null); this.cargar(); },
      error: () => { this.toast.error('No se pudo anular el comprobante.'); this.confirmarAnular.set(null); }
    });
  }

  // ---------- Nota de crédito ----------
  pedirNotaCredito(c: Comprobante) {
    this.notaCredito.set(c);
    this.ncMotivoCodigo.set('01');
    this.ncMotivo.set(this.motivosNc[0].texto);
  }

  elegirMotivoNc(codigo: string) {
    this.ncMotivoCodigo.set(codigo);
    const m = this.motivosNc.find(x => x.codigo === codigo);
    if (m) this.ncMotivo.set(m.texto);
  }

  confirmarNotaCredito() {
    const c = this.notaCredito();
    if (!c) return;
    const motivo = this.ncMotivo().trim();
    if (motivo.length < 3) { this.toast.advertencia('Indica el motivo de la nota de crédito.'); return; }
    this.emitiendoNc.set(true);
    this.svc.emitirNotaCredito(c.id, this.ncMotivoCodigo(), motivo).subscribe({
      next: nc => {
        this.emitiendoNc.set(false);
        this.notaCredito.set(null);
        this.toast.exito(`Nota de crédito ${nc.numeroCompleto} emitida (${nc.estado}).`);
        this.cargar();
      },
      error: e => { this.emitiendoNc.set(false); this.toast.desdeHttp(e, 'No se pudo emitir la nota de crédito.'); }
    });
  }

  // ---------- Nota de débito ----------
  pedirNotaDebito(c: Comprobante) {
    this.notaDebito.set(c);
    this.ndMotivoCodigo.set('01');
    this.ndMotivo.set(this.motivosNd[0].texto);
  }

  elegirMotivoNd(codigo: string) {
    this.ndMotivoCodigo.set(codigo);
    const m = this.motivosNd.find(x => x.codigo === codigo);
    if (m) this.ndMotivo.set(m.texto);
  }

  confirmarNotaDebito() {
    const c = this.notaDebito();
    if (!c) return;
    const motivo = this.ndMotivo().trim();
    if (motivo.length < 3) { this.toast.advertencia('Indica el motivo de la nota de débito.'); return; }
    this.emitiendoNd.set(true);
    this.svc.emitirNotaDebito(c.id, this.ndMotivoCodigo(), motivo).subscribe({
      next: nd => {
        this.emitiendoNd.set(false);
        this.notaDebito.set(null);
        this.toast.exito(`Nota de débito ${nd.numeroCompleto} emitida (${nd.estado}).`);
        this.cargar();
      },
      error: e => { this.emitiendoNd.set(false); this.toast.desdeHttp(e, 'No se pudo emitir la nota de débito.'); }
    });
  }

  // ---------- Guía de remisión ----------
  pedirGuia(c: Comprobante) {
    this.guiaForm = this.guiaVacia();
    this.guia.set(c);
  }

  confirmarGuia() {
    const c = this.guia();
    if (!c) return;
    const f = this.guiaForm;
    if (f.motivoCodigo === '13' && !(f.motivoDescripcion ?? '').trim()) { this.toast.advertencia('Indica la descripción del motivo de traslado.'); return; }
    if (!(f.pesoBrutoTotal > 0)) { this.toast.advertencia('Indica el peso bruto total (mayor a 0).'); return; }
    if (!f.partidaUbigeo.trim() || !f.partidaDireccion.trim() || !f.llegadaUbigeo.trim() || !f.llegadaDireccion.trim()) {
      this.toast.advertencia('Completa ubigeo y dirección de partida y de llegada.'); return;
    }
    if (f.modalidadTransporte === '01' && (!(f.transportistaNumDoc ?? '').trim() || !(f.transportistaRazonSocial ?? '').trim())) {
      this.toast.advertencia('El transporte público requiere RUC y razón social del transportista.'); return;
    }
    if (f.modalidadTransporte === '02' && (!(f.vehiculoPlaca ?? '').trim() || !(f.conductorNumDoc ?? '').trim() || !(f.conductorNombres ?? '').trim() || !(f.conductorLicencia ?? '').trim())) {
      this.toast.advertencia('El transporte privado requiere placa y datos del conductor (documento, nombres, licencia).'); return;
    }
    this.emitiendoGuia.set(true);
    this.svc.emitirGuiaRemision(c.id, f).subscribe({
      next: g => {
        this.emitiendoGuia.set(false);
        this.guia.set(null);
        this.toast.exito(`Guía de remisión ${g.numeroCompleto} registrada.`);
        this.cargar();
      },
      error: e => { this.emitiendoGuia.set(false); this.toast.desdeHttp(e, 'No se pudo registrar la guía de remisión.'); }
    });
  }

  esNotaCredito(c: Comprobante): boolean { return c.tipo === 'NOTA_CREDITO'; }
  esNotaDebito(c: Comprobante): boolean { return c.tipo === 'NOTA_DEBITO'; }
  esNota(c: Comprobante): boolean { return c.tipo === 'NOTA_CREDITO' || c.tipo === 'NOTA_DEBITO'; }
  esGuia(c: Comprobante): boolean { return c.tipo === 'GUIA_REMISION'; }
  esVenta(c: Comprobante): boolean { return c.tipo === 'BOLETA' || c.tipo === 'FACTURA'; }
  etiquetaTipo(t: string): string {
    return t === 'NOTA_CREDITO' ? 'N. CRÉDITO' : t === 'NOTA_DEBITO' ? 'N. DÉBITO' : t === 'GUIA_REMISION' ? 'GUÍA' : t;
  }

  claseEstado(estado: string): string {
    switch (estado) {
      case 'ACEPTADO': return 'badge--verde';
      case 'RECHAZADO': case 'ERROR': return 'badge--rojo';
      case 'ANULADO': return 'badge--gris';
      case 'SIMULADO': return 'badge--gris';
      default: return 'badge--amarillo';
    }
  }

  volver() { this.router.navigate(['/pedidos']); }

  aplicarFiltros() { this.pagina.set(1); this.cargar(); }
  limpiarFiltros() { this.busqueda.set(''); this.tipo.set(''); this.estado.set(''); this.desde.set(''); this.hasta.set(''); this.aplicarFiltros(); }
  verDetalle(c: Comprobante) {
    this.svc.obtenerComprobante(c.id).subscribe({ next: x => this.detalle.set(x), error: e => this.toast.desdeHttp(e, 'No se pudo abrir el detalle.') });
  }

  irAlPedido(c: Comprobante) { this.router.navigate(['/pedidos', c.pedidoId]); }
  sincronizar(c: Comprobante) {
    this.svc.sincronizar(c.id).subscribe({ next: x => { this.toast.info(`Estado actualizado: ${x.estado}`); this.cargar(); }, error: e => this.toast.desdeHttp(e, 'No se pudo sincronizar.') });
  }
  descargarArchivo(c: Comprobante, tipo: 'xml' | 'cdr') {
    const solicitud = tipo === 'xml' ? this.svc.descargarXml(c.id) : this.svc.descargarCdr(c.id);
    solicitud.subscribe({ next: blob => {
      const url = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = url;
      a.download = `${c.numeroCompleto}.${tipo === 'xml' ? 'xml' : 'zip'}`; a.click(); URL.revokeObjectURL(url);
    }, error: e => this.toast.desdeHttp(e, `El ${tipo.toUpperCase()} aún no está disponible.`) });
  }
}
