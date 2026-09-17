import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CajaService, UsuarioDelDia } from '../../core/services/caja.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { fechaLocalIso } from '../../core/util/fecha-local';
import { MovimientoCaja, TipoGasto } from '../../core/models/models';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface Denominacion {
  valor: number;
  cantidad: number;
}

@Component({
  selector: 'app-cuadre-caja',
  imports: [CommonModule, FormsModule, RouterLink, IconComponent, PageHeaderComponent],
  templateUrl: './cuadre-caja.component.html',
  styleUrl: './cuadre-caja.component.scss'
})
export class CuadreCajaComponent implements OnInit {
  private readonly cajaSvc = inject(CajaService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private versionMovimientos = 0;
  private versionUsuarios = 0;
  /** Solo se elige colaborador solo en la carga inicial y al cambiar de fecha. */
  private autoSeleccionPendiente = true;

  fecha = fechaLocalIso(new Date());
  readonly fechaMaxima = fechaLocalIso(new Date());
  guardado = false;

  // Cuadre por colaborador
  readonly usuariosDelDia = signal<UsuarioDelDia[]>([]);
  readonly cargandoUsuarios = signal(false);
  readonly usuarioSeleccionadoId = signal<number | null>(null);
  readonly usuarioSeleccionadoNombre = computed(() =>
    this.usuariosDelDia().find(u => u.id === this.usuarioSeleccionadoId())?.nombreCompleto ?? '');

  denominaciones = signal<Denominacion[]>([
    { valor: 100, cantidad: 0 },
    { valor: 50, cantidad: 0 },
    { valor: 20, cantidad: 0 },
    { valor: 10, cantidad: 0 },
    { valor: 5, cantidad: 0 },
    { valor: 2, cantidad: 0 },
    { valor: 1, cantidad: 0 },
    { valor: 0.5, cantidad: 0 },
    { valor: 0.2, cantidad: 0 },
    { valor: 0.1, cantidad: 0 },
  ]);

  cajaInicial = signal(0);  // signal para que enCajaDeberiaHaber/diferencia recalculen al tipear
  corte = 0;        // efectivo que se entrega/retira al cierre
  nota = '';        // observación libre del cuadre

  // Cómo se cuenta el efectivo de la caja: 'rapido' = escribes el total directo (por defecto,
  // lo más ágil); 'detallado' = cuentas billete por billete y el total se calcula solo.
  readonly modoConteo = signal<'rapido' | 'detallado'>('rapido');
  readonly totalContadoManual = signal(0);
  // Total con el que se CERRÓ este turno (si ya estaba guardado). Se usa para avisar en el
  // modo "billete por billete": como solo se guarda el total (no el desglose de billetes),
  // al reabrir un cierre en ese modo la tabla sale en 0 y parecía que no se había grabado.
  readonly totalCerradoGuardado = signal<number | null>(null);
  // true cuando el desglose mostrado NO es el detalle real guardado, sino una reconstrucción
  // del total (cierres antiguos que se guardaron solo con el monto). Sirve para avisarlo suave.
  readonly desgloseReconstruido = signal(false);
  // El detalle "Ver cada cobro del día" lo abre/cierra el usuario y se queda así (no se
  // colapsa solo con el refresco automático de cada 15s).
  readonly mostrarCobros = signal(false);
  // false = solo los movimientos del colaborador seleccionado; true = toda la caja del día (todos).
  readonly verTodos = signal(false);
  readonly sugerenciaCajaInicial = signal<{ monto: number; usuarioNombre?: string; fecha: string } | null>(null);

  readonly movimientos = signal<MovimientoCaja[]>([]);
  readonly cargandoMovimientos = signal(false);
  readonly tiposGasto = signal<TipoGasto[]>([]);

  // Formulario de nuevo gasto
  readonly modalGasto = signal(false);
  gastoMonto = 0;
  gastoTipoId: number | '' = '';
  gastoMetodo: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';
  gastoDescripcion = '';
  guardandoGasto = signal(false);

  /** True si el usuario ya empezó a contar/ajustar la caja y no ha guardado el cierre. */
  private hayCierreEnProgreso(): boolean {
    if (this.guardado || this.guardando()) return false;
    return this.totalContado() > 0 || this.cajaInicial() > 0 || this.corte > 0;
  }

  // Avisa antes de perder un conteo de caja a medio hacer (F5, cerrar pestaña, navegar fuera).
  @HostListener('window:beforeunload', ['$event'])
  avisarSalidaConConteo(evento: BeforeUnloadEvent): void {
    if (this.hayCierreEnProgreso()) {
      evento.preventDefault();
      evento.returnValue = '';
    }
  }

  ngOnInit() {
    // Si venimos del reporte con ?fecha=YYYY-MM-DD, cuadrar ese día.
    const fechaQp = this.route.snapshot.queryParamMap.get('fecha');
    if (fechaQp) this.fecha = fechaQp;
    this.cajaSvc.tiposGasto().subscribe(t => this.tiposGasto.set(t));
    // Por defecto, el usuario actualmente logueado es el seleccionado
    this.usuarioSeleccionadoId.set(this.auth.usuario()?.id ?? null);
    this.cargarUsuariosDelDia();
    this.cargarMovimientos();
    this.cargarSugerenciaCajaInicial();
    this.cargarCuadreExistente();
    // Sin auto-refresco: esta pantalla NO se actualiza sola (ni por tiempo ni por foco).
    // El usuario recarga con el botón / al reabrir la pantalla.
  }

  cambiarFecha(fecha: string) {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(fecha) || fecha > this.fechaMaxima) {
      this.toast.advertencia('Selecciona una fecha válida que no esté en el futuro.');
      return;
    }
    this.fecha = fecha;
    this.reiniciarConteo();
    this.autoSeleccionPendiente = true;
    this.cargarUsuariosDelDia();
    this.cargarMovimientos();
    this.cargarSugerenciaCajaInicial();
    this.cargarCuadreExistente();
  }

  cargarUsuariosDelDia() {
    const version = ++this.versionUsuarios;
    this.cargandoUsuarios.set(true);
    this.cajaSvc.usuariosDelDia(this.fecha).subscribe({
      next: list => {
        if (version !== this.versionUsuarios) return;
        this.usuariosDelDia.set(list);
        this.cargandoUsuarios.set(false);
        this.resolverUsuarioSeleccionado(list);
      },
      error: () => { if (version === this.versionUsuarios) this.cargandoUsuarios.set(false); }
    });
  }

  /**
   * Decide de quién se muestra el turno. Prefiere al usuario logueado, pero SOLO si tuvo
   * movimientos o cuadre ese día; si no (caso típico: el dueño revisando el turno de una
   * trabajadora), cae al primer colaborador de la lista.
   *
   * Antes, cuando el seleccionado no estaba en la lista se reasignaba el id del propio
   * usuario logueado — el mismo que ya no estaba — así que la pantalla quedaba filtrada
   * por alguien sin movimientos y TODO salía en S/ 0.00, aunque el turno de la
   * trabajadora estuviera cerrado y con dinero. La reselección automática solo corre en
   * la carga inicial y al cambiar de fecha, para no arrebatarle la pantalla a quien está
   * contando la caja cuando el refresco de 15s trae la lista.
   */
  private resolverUsuarioSeleccionado(list: UsuarioDelDia[]) {
    const sel = this.usuarioSeleccionadoId();
    if (sel && list.some(u => u.id === sel)) { this.autoSeleccionPendiente = false; return; }
    if (!this.autoSeleccionPendiente) return;

    const yo = this.auth.usuario()?.id ?? null;
    const destino = yo != null && list.some(u => u.id === yo) ? yo : (list[0]?.id ?? null);
    if (destino == null) return;

    this.autoSeleccionPendiente = false;
    this.seleccionarUsuario(destino);
  }

  seleccionarUsuario(id: number) {
    if (this.usuarioSeleccionadoId() === id) return;
    this.usuarioSeleccionadoId.set(id);
    this.reiniciarConteo();
    this.cajaInicial.set(0);
    this.nota = '';
    this.guardado = false;
    this.cargarMovimientos();
    this.cargarCuadreExistente();
  }

  cargarCuadreExistente() {
    const uid = this.usuarioSeleccionadoId();
    if (!uid) return;
    this.cajaSvc.cuadreDelUsuario(this.fecha, uid).subscribe({
      next: c => {
        this.cajaInicial.set(c.cajaInicial);
        this.corte = c.corte ?? 0;
        this.nota = c.nota ?? '';
        // Recupera también lo que se contó al cerrar. Sin esto, al reabrir un turno ya
        // cuadrado el campo "¿Cuánto contaste en la caja?" volvía a 0 y el cierre parecía
        // vacío, aunque en la base estuviera guardado el monto real.
        // Si el cierre se guardó billete por billete, reconstruye el desglose exacto para
        // mostrarlo igual (cualquier día). Si se guardó por total directo, abre en modo rápido.
        this.totalContadoManual.set(c.totalContado ?? 0);
        this.totalCerradoGuardado.set(c.totalContado ?? 0);
        this.desgloseReconstruido.set(false);
        const total = c.totalContado ?? 0;
        let mapa: Record<string, number> | null = null;
        if (c.detalleConteo) {
          try { mapa = JSON.parse(c.detalleConteo) as Record<string, number>; } catch { mapa = null; }
        }
        // El desglose SIEMPRE se muestra al reabrir un cierre: si no se guardó el detalle real
        // (cierres antiguos), se reconstruye del total para que nunca aparezca en 0 (un 0 asusta
        // y parece que el cierre se borró, aunque el monto sí está guardado).
        if (!mapa && total > 0) {
          mapa = this.reconstruirDesglose(total);
          this.desgloseReconstruido.set(true);
        }
        if (mapa) {
          const m = mapa;
          this.denominaciones.update(list => list.map(d => ({ ...d, cantidad: m[String(d.valor)] ?? 0 })));
          this.modoConteo.set('detallado');
        } else {
          this.modoConteo.set('rapido');
        }
        this.guardado = true;
      },
      error: () => { this.guardado = false; this.totalCerradoGuardado.set(null); }
    });
  }

  /**
   * Reparte un monto en billetes/monedas (mayor a menor) para mostrar SIEMPRE un desglose
   * cuando el cierre se guardó solo con el total. Trabaja en céntimos para evitar errores de
   * coma flotante. La suma del desglose es exactamente el total (el efectivo peruano es
   * múltiplo de 0.10, así que no queda resto).
   */
  private reconstruirDesglose(total: number): Record<string, number> {
    let cent = Math.round(total * 100);
    const out: Record<string, number> = {};
    for (const v of [100, 50, 20, 10, 5, 2, 1, 0.5, 0.2, 0.1]) {
      const vc = Math.round(v * 100);
      const n = Math.floor(cent / vc);
      if (n > 0) { out[String(v)] = n; cent -= n * vc; }
    }
    return out;
  }

  cargarSugerenciaCajaInicial() {
    this.sugerenciaCajaInicial.set(null);
    this.cajaSvc.obtenerUltimoAnterior(this.fecha).subscribe({
      next: c => this.sugerenciaCajaInicial.set({ monto: c.cajaFinal, usuarioNombre: c.usuarioNombre, fecha: c.fecha }),
      error: () => {}
    });
  }

  usarSugerenciaCajaInicial() {
    const s = this.sugerenciaCajaInicial();
    if (s) this.cajaInicial.set(s.monto);
  }

  /** La caja inicial escrita no coincide con el cierre anterior (causa #1 de descuadre). */
  readonly cajaInicialNoCoincide = computed(() => {
    const s = this.sugerenciaCajaInicial();
    if (!s || this.guardado) return false;
    return Math.abs(this.cajaInicial() - s.monto) > 0.01;
  });

  alternarVerTodos() {
    this.verTodos.set(!this.verTodos());
    this.cargarMovimientos();
  }

  cargarMovimientos() {
    const version = ++this.versionMovimientos;
    this.cargandoMovimientos.set(true);
    const uid = this.verTodos() ? undefined : (this.usuarioSeleccionadoId() ?? undefined);
    this.cajaSvc.movimientos(this.fecha, uid).subscribe({
      next: list => {
        if (version !== this.versionMovimientos) return;
        this.movimientos.set(list);
        this.cargandoMovimientos.set(false);
      },
      error: () => { if (version === this.versionMovimientos) this.cargandoMovimientos.set(false); }
    });
  }

  gastosDelDia = computed(() => this.movimientos().filter(m => m.tipo === 'GASTO'));
  gastosTotal = computed(() => this.gastosDelDia().reduce((acc, m) => acc + m.monto, 0));
  // Solo los gastos EN EFECTIVO salen del cajón físico; los digitales (Yape/Plin) no.
  gastosEfectivo = computed(() =>
    this.gastosDelDia().filter(m => m.metodoPago === 'EFECTIVO').reduce((acc, m) => acc + m.monto, 0)
  );

  ingresosDelDia = computed(() => this.movimientos().filter(m => m.tipo === 'INGRESO'));
  ingresosTotal = computed(() => this.ingresosDelDia().reduce((acc, m) => acc + m.monto, 0));

  ingresosPorMetodo = computed(() => {
    const acc = new Map<string, { monto: number; cantidad: number }>();
    for (const m of this.ingresosDelDia()) {
      const actual = acc.get(m.metodoPago) ?? { monto: 0, cantidad: 0 };
      acc.set(m.metodoPago, { monto: actual.monto + m.monto, cantidad: actual.cantidad + 1 });
    }
    return Array.from(acc.entries()).map(([metodo, v]) => ({ metodo, monto: v.monto, cantidad: v.cantidad }));
  });

  // Tabla "Movimientos Digitales": por cada método NO efectivo, cuánto se pagó y cuánto se gastó.
  readonly metodosDigitales = ['YAPE', 'PLIN', 'TRANSFERENCIA', 'POS'];
  movimientosDigitales = computed(() => {
    const acc = new Map<string, { pago: number; gasto: number }>();
    for (const m of this.movimientos()) {
      if (m.metodoPago === 'EFECTIVO') continue;
      const cur = acc.get(m.metodoPago) ?? { pago: 0, gasto: 0 };
      if (m.tipo === 'INGRESO') cur.pago += m.monto; else cur.gasto += m.monto;
      acc.set(m.metodoPago, cur);
    }
    return Array.from(acc.entries()).map(([metodo, v]) => ({ metodo, pago: v.pago, gasto: v.gasto }));
  });

  // Detalle pago por pago de los INGRESOS digitales (Yape/Plin/Transferencia/POS/Tarjeta),
  // ordenado por hora — el operador puede reconciliar cada pago contra su app.
  ingresosDigitalesDetalle = computed(() =>
    this.ingresosDelDia()
      .filter(m => m.metodoPago !== 'EFECTIVO')
      .sort((a, b) => a.fecha.localeCompare(b.fecha))
  );
  ingresosDigitalesTotal = computed(() =>
    this.ingresosDigitalesDetalle().reduce((acc, m) => acc + m.monto, 0)
  );

  ingresosBilleteras = computed(() =>
    this.ingresosDelDia().filter(m => ['YAPE', 'PLIN'].includes(m.metodoPago)).reduce((a, m) => a + m.monto, 0)
  );

  ingresosTransferencia = computed(() =>
    this.ingresosDelDia().filter(m => m.metodoPago === 'TRANSFERENCIA').reduce((a, m) => a + m.monto, 0)
  );

  // Ingresos digitales agrupados para el reporte: transferencia móvil (Yape/Plin/Transferencia) vs tarjeta (POS).
  ingresosTransferenciaMovil = computed(() =>
    this.ingresosDelDia().filter(m => ['YAPE', 'PLIN', 'TRANSFERENCIA'].includes(m.metodoPago)).reduce((a, m) => a + m.monto, 0)
  );
  ingresosTarjeta = computed(() =>
    this.ingresosDelDia().filter(m => ['POS', 'TARJETA'].includes(m.metodoPago)).reduce((a, m) => a + m.monto, 0)
  );

  pagosDigitalesTotal = computed(() =>
    this.ingresosPorMetodo().filter(i => i.metodo !== 'EFECTIVO').reduce((acc, i) => acc + i.cantidad, 0)
  );

  // Detalle itemizado de pagos, agrupado por método (para las tablas al pie del cuadre).
  detalleIngresos = computed(() => {
    const orden = ['EFECTIVO', 'YAPE', 'PLIN', 'TRANSFERENCIA', 'POS', 'TARJETA'];
    const grupos = new Map<string, MovimientoCaja[]>();
    for (const m of this.ingresosDelDia()) {
      const lista = grupos.get(m.metodoPago) ?? [];
      lista.push(m);
      grupos.set(m.metodoPago, lista);
    }
    return Array.from(grupos.entries())
      .map(([metodo, items]) => ({ metodo, items, total: items.reduce((a, x) => a + x.monto, 0) }))
      .sort((a, b) => orden.indexOf(a.metodo) - orden.indexOf(b.metodo));
  });

  pedidosPagadosEfectivo = computed(() =>
    this.ingresosDelDia().filter(m => m.metodoPago === 'EFECTIVO').reduce((acc, m) => acc + m.monto, 0)
  );

  gananciaNeta = computed(() => this.ingresosTotal() - this.gastosTotal());

  readonly etiquetasMetodo: Record<string, string> = {
    EFECTIVO: 'Efectivo', YAPE: 'Yape', PLIN: 'Plin', TRANSFERENCIA: 'Transferencia', POS: 'POS/Tarjeta', TARJETA: 'Tarjeta'
  };

  abrirModalGasto() {
    this.gastoMonto = 0;
    this.gastoTipoId = '';
    this.gastoMetodo = 'EFECTIVO';
    this.gastoDescripcion = '';
    this.modalGasto.set(true);
  }

  confirmarGasto() {
    if (this.guardandoGasto()) return;
    if (!Number.isFinite(this.gastoMonto) || this.gastoMonto <= 0 || this.gastoMonto > 100_000) {
      this.toast.advertencia('Ingresa un gasto mayor a S/ 0.00 y menor o igual a S/ 100,000.00.');
      return;
    }
    this.guardandoGasto.set(true);
    this.cajaSvc.registrarGasto(
      this.gastoMonto,
      this.gastoMetodo,
      this.gastoTipoId ? (this.gastoTipoId as number) : null,
      this.gastoDescripcion.trim() || undefined
    ).subscribe({
      next: () => {
        this.guardandoGasto.set(false);
        this.modalGasto.set(false);
        this.toast.exito('Gasto registrado');
        this.cargarMovimientos();
      },
      error: err => {
        this.guardandoGasto.set(false);
        this.toast.desdeHttp(err, 'No se pudo registrar el gasto.');
      }
    });
  }

  actualizarCantidad(valor: number, cantidad: number) {
    const cantidadValida = Number.isFinite(cantidad) ? Math.max(0, Math.floor(cantidad)) : 0;
    this.denominaciones.update(list =>
      list.map(d => d.valor === valor ? { ...d, cantidad: cantidadValida } : d)
    );
  }

  readonly totalDenominaciones = computed(() =>
    this.denominaciones().reduce((acc, d) => acc + d.valor * d.cantidad, 0)
  );

  // El total contado sale del modo activo: en 'detallado' del contador de billetes,
  // en 'rapido' del número que el usuario escribe directo.
  totalContado = computed(() =>
    this.modoConteo() === 'detallado' ? this.totalDenominaciones() : (this.totalContadoManual() || 0)
  );

  usarContadorDetallado() { this.modoConteo.set('detallado'); }
  usarTotalRapido() {
    // Al colapsar el contador, conserva lo ya contado como total rápido para no perderlo.
    if (this.totalDenominaciones() > 0) this.totalContadoManual.set(this.totalDenominaciones());
    this.modoConteo.set('rapido');
  }

  enCajaDeberiaHaber = computed(() =>
    this.cajaInicial() + this.pedidosPagadosEfectivo() - this.gastosEfectivo()
  );

  diferencia = computed(() => this.totalContado() - this.enCajaDeberiaHaber());

  // Remanente físico que queda tras entregar el corte (getter: depende del campo plano `corte`).
  get cajaFinalRemanente(): number { return this.totalContado() - this.corte; }

  estadoCaja = computed<'SOBRA' | 'CUADRA' | 'FALTA'>(() => {
    const d = this.diferencia();
    if (Math.abs(d) < 0.01) return 'CUADRA';
    return d > 0 ? 'SOBRA' : 'FALTA';
  });

  guardando = signal(false);
  confirmarRegrabar = signal(false);

  guardarCuadre() {
    if (this.guardando()) return;
    if (!this.usuarioSeleccionadoId()) {
      this.toast.advertencia('Selecciona el colaborador cuyo turno deseas cuadrar.');
      return;
    }
    // En "Toda la caja del día" con varios colaboradores, los totales en pantalla son
    // del día completo (no de un turno): cerrar aquí guardaría un conteo que no
    // corresponde a una sola persona. Se exige elegir el turno.
    if (this.verTodos() && this.usuariosDelDia().length > 1) {
      this.toast.advertencia('Estás viendo “Toda la caja del día”. Para cerrar caja, elige el turno de un colaborador (botón “Solo …”).');
      return;
    }
    if (!Number.isFinite(this.cajaInicial()) || this.cajaInicial() < 0) {
      this.toast.advertencia('La caja inicial no puede ser negativa.');
      return;
    }
    if (!Number.isFinite(this.corte) || this.corte < 0) {
      this.toast.advertencia('El corte no puede ser negativo.');
      return;
    }
    if (this.corte > this.totalContado() + 0.001) {
      this.toast.advertencia('El corte no puede superar el efectivo contado en caja.');
      return;
    }
    // Ya existe un cuadre guardado para esta fecha/usuario: confirmar antes de sobrescribirlo
    // en vez de reemplazarlo en silencio (el conteo anterior se pierde sin aviso).
    if (this.guardado) {
      this.confirmarRegrabar.set(true);
      return;
    }
    this.procederGuardado();
  }

  confirmarRegrabarCuadre() {
    this.confirmarRegrabar.set(false);
    this.procederGuardado();
  }

  private procederGuardado() {
    this.guardando.set(true);

    // Si se contó billete por billete, guarda el desglose para poder mostrarlo al reabrir.
    const detalleConteo = (this.modoConteo() === 'detallado' && this.totalDenominaciones() > 0)
      ? JSON.stringify(this.denominaciones().reduce((o, d) => {
          if (d.cantidad > 0) o[String(d.valor)] = d.cantidad;
          return o;
        }, {} as Record<string, number>))
      : undefined;

    this.cajaSvc.guardarCuadre({
      fecha: this.fecha,
      usuarioId: this.usuarioSeleccionadoId() ?? undefined,
      cajaInicial: this.cajaInicial(),
      pedidosPagadosEfect: this.pedidosPagadosEfectivo(),
      gastos: this.gastosEfectivo(),
      totalContado: this.totalContado(),
      diferencia: this.diferencia(),
      cajaFinal: this.cajaFinalRemanente,
      corte: this.corte,
      ingresosDigital: this.ingresosTransferenciaMovil(),
      ingresosTarjeta: this.ingresosTarjeta(),
      nota: this.nota.trim() || undefined,
      observaciones: undefined,
      detalleConteo,
    }).subscribe({
      next: guardado => {
        this.guardando.set(false);
        this.guardado = true;
        this.toast.exito('Cuadre guardado. Abriendo PDF…');
        // Abre la vista imprimible en pestaña nueva (auto-lanza print)
        window.open(`/cuadre-caja/imprimir/${guardado.id}`, '_blank');
      },
      error: err => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar el cuadre.');
      }
    });
  }

  private reiniciarConteo() {
    this.denominaciones.update(list => list.map(d => ({ ...d, cantidad: 0 })));
    this.totalContadoManual.set(0);
    this.modoConteo.set('rapido');
    this.corte = 0;
    this.nota = '';
    this.guardado = false;
    this.totalCerradoGuardado.set(null);
    this.desgloseReconstruido.set(false);
    this.confirmarRegrabar.set(false);
  }
}
