import { PasoTour } from '../services/tour.service';

/**
 * Tours guiados por módulo. La clave coincide con el `tourId` que pasa cada pantalla
 * a <app-page-header>. Los pasos con `ancla` resaltan un elemento marcado con
 * [data-tour="..."]; los demás se muestran centrados.
 *
 * Escritos en lenguaje simple, pensados para cualquier persona sin experiencia.
 */
export const TOURS: Record<string, PasoTour[]> = {
  // ---------------- Inicio ----------------
  inicio: [
    { titulo: '¡Bienvenido!', texto: 'Esta es la pantalla de Inicio. Aquí ves de un vistazo cómo va el negocio hoy. Te muestro lo principal en unos pasos.' },
    { ancla: 'inicio-kpis', titulo: 'Los números del día', texto: 'Pedidos en proceso, listos para entregar, ventas de hoy y del mes. Se actualizan solos.' },
    { ancla: 'inicio-areas', titulo: 'Ropa en cada área', texto: 'Cuántos pedidos hay ahora mismo en cada etapa del lavado (recepción, lavado, secado, etc.).' },
    { ancla: 'inicio-atajos', titulo: 'Accesos rápidos', texto: 'Botones directos a lo que más usarás: registrar un pedido, ver la lista, clientes y la caja.' },
    { titulo: 'Listo', texto: 'Cuando quieras repasar cómo funciona una pantalla, busca el botón "?" arriba a la derecha. ¡A trabajar!' },
  ],

  // ---------------- Pedidos ----------------
  pedidos: [
    { titulo: 'Lista de pedidos', texto: 'Aquí ves y manejas todos los pedidos de la lavandería. Te muestro cómo moverte.' },
    { ancla: 'pedidos-buscador', titulo: 'Buscar un pedido', texto: 'Escribe el número de ticket y presiona Enter para abrirlo directo. También busca por cliente, celular o DNI.' },
    { ancla: 'pedidos-filtros', titulo: 'Filtros', texto: '"Pendientes" son los que están en proceso, "Entregados / Cerrados" los ya entregados o anulados, "Últimos" muestra todos, y "Fecha" para buscar por día.' },
    { ancla: 'pedidos-vista', titulo: 'Lista o tablero', texto: 'El tablero muestra los pedidos en columnas por etapa (como una pizarra). La lista los muestra en una tabla.' },
    { ancla: 'pedidos-avanzar', titulo: 'Avanzar de etapa', texto: 'Cada tarjeta tiene un botón que mueve el pedido a la siguiente etapa. El sistema te dice qué toca hacer.' },
    { titulo: 'Ver el detalle', texto: 'Haz clic en cualquier pedido para abrir su página completa: cobrar, entregar, agregar prendas, fotos e historial.' },
  ],

  // ---------------- Detalle del pedido ----------------
  'pedido-detalle': [
    { titulo: 'Detalle del pedido', texto: 'Esta es la ficha completa de un pedido. Todo lo que puedes hacer con él está aquí.' },
    { ancla: 'detalle-progreso', titulo: 'En qué etapa va', texto: 'La barra muestra por dónde va el pedido: cada punto verde es una etapa ya cumplida.' },
    { ancla: 'detalle-cliente', titulo: 'Datos del cliente', texto: 'Nombre y celular. El botón verde abre WhatsApp con un mensaje ya escrito según la etapa del pedido.' },
    { ancla: 'detalle-pago', titulo: 'El pago', texto: 'Cuánto pagó y cuánto debe. Si hay saldo, aquí lo cobras. En verde cuando ya está pagado del todo.' },
    { ancla: 'detalle-mas', titulo: 'Acciones del pedido', texto: 'Aquí, a la vista, están todas las acciones: el botón grande verde hace lo que toca ahora (cobrar y entregar o avanzar la etapa), y al lado tienes reimprimir ticket, cambiar fecha, convertir a delivery, emitir boleta/factura o anular.' },
  ],

  // ---------------- Registrar ----------------
  registrar: [
    { titulo: 'Registrar un pedido', texto: 'Aquí creas un pedido nuevo. Son 4 partes; te las muestro.' },
    { ancla: 'registrar-modalidad', titulo: '1. ¿Cómo se entrega?', texto: 'Tienda (el cliente recoge), Recojo (vas por la ropa) o Delivery (la llevas a su casa).' },
    { ancla: 'registrar-cliente', titulo: '2. El cliente', texto: 'Busca si ya existe o escribe sus datos. El celular es obligatorio: es como le avisamos que su pedido está listo.' },
    { ancla: 'registrar-servicios', titulo: '3. Los servicios', texto: 'Elige el servicio, pon la cantidad y agrégalo. Puedes sumar varios (lavado, planchado, etc.).' },
    { ancla: 'registrar-pago', titulo: '4. El pago', texto: 'Registra si el cliente adelanta algo. Puede pagar todo, una parte, o nada (paga al recoger).' },
    { ancla: 'registrar-confirmar', titulo: 'Confirmar', texto: 'Revisa el total y presiona registrar. Se imprime el ticket con el número del pedido.' },
  ],

  // ---------------- Clientes ----------------
  clientes: [
    { titulo: 'Clientes', texto: 'Aquí está tu base de clientes. Te muestro lo esencial.' },
    { ancla: 'clientes-buscar', titulo: 'Buscar', texto: 'Encuentra un cliente por nombre, celular o DNI para ver sus datos o su historial.' },
    { ancla: 'clientes-nuevo', titulo: 'Nuevo cliente', texto: 'También puedes registrar clientes desde aquí, aunque normalmente se crean solos al hacer un pedido.' },
    { titulo: 'Fidelización (CRM)', texto: 'En el menú Clientes → CRM ves quién compra más, quién tiene deuda y cumpleaños. Útil para fidelizar.' },
  ],

  // ---------------- Cuadre de caja ----------------
  'cuadre-caja': [
    { titulo: 'Cierre de caja', texto: 'Al final del día revisas que el dinero cuadre. Es más fácil de lo que parece: son 3 pasos.' },
    { ancla: 'cuadre-paso1', titulo: '1. El efectivo', texto: 'Aquí solo el EFECTIVO. El sistema calcula cuánto debería haber en la caja y tú escribes cuánto contaste. Te dice si cuadra, sobra o falta.' },
    { ancla: 'cuadre-paso2', titulo: '2. Ventas del día', texto: 'Todo lo vendido hoy, por cualquier método. Ojo: Yape, Plin y tarjeta NO están en la caja física, están en tus cuentas.' },
    { ancla: 'cuadre-paso3', titulo: '3. Cerrar el día', texto: 'Si retiras efectivo (al banco o caja fuerte) lo anotas, agregas una nota y guardas. Se genera un PDF del cierre.' },
    { titulo: 'Un consejo', texto: 'Si te confunde algún número, recuerda: el paso 1 es la plata en la caja; el paso 2 es cuánto vendiste en total. Son cosas distintas.' },
  ],

  // ---------------- Inventario ----------------
  inventario: [
    { titulo: 'Inventario', texto: 'Controla tus insumos: detergente, bolsas, suavizante, etc.' },
    { ancla: 'inventario-nuevo', titulo: 'Registrar insumo', texto: 'Agrega un insumo con su stock mínimo. El sistema te avisa cuando esté por acabarse.' },
    { titulo: 'Movimientos', texto: 'Registra compras (entra stock) y consumos (sale stock) para que el conteo esté siempre al día.' },
  ],
};
