-- ============================================================
-- 064: Escenario inicial para la demostración pública LunaLav
--
-- Solo agrega información ficticia al tenant cuyo slug es "demo".
-- Las marcas DEMO-xxx hacen que el script sea idempotente y permiten
-- distinguir estos datos de cualquier prueba que haga un visitante.
-- ============================================================
USE LunaLav;
GO

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @NegocioId INT = (SELECT Id FROM dbo.Negocio WHERE Slug = 'demo');
DECLARE @SedeId INT = (SELECT TOP 1 Id FROM dbo.Sede WHERE NegocioId = @NegocioId AND Activo = 1 ORDER BY Id);
DECLARE @UsuarioId INT = (SELECT TOP 1 Id FROM dbo.Usuario WHERE NegocioId = @NegocioId AND Activo = 1 ORDER BY Id);

IF @NegocioId IS NULL OR @SedeId IS NULL OR @UsuarioId IS NULL
    THROW 51000, 'No se encontro el tenant demo de LunaLav para cargar los datos ficticios.', 1;

-- Clientes sin teléfonos ni documentos: la demo nunca debe presentar datos personales reales.
IF NOT EXISTS (SELECT 1 FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Ana Torres (Demo)')
    INSERT INTO dbo.Cliente (NegocioId, Nombre, Puntos, Activo, FechaCreacion) VALUES (@NegocioId, N'Ana Torres (Demo)', 120, 1, DATEADD(day, -40, SYSDATETIME()));
IF NOT EXISTS (SELECT 1 FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Carlos Rojas (Demo)')
    INSERT INTO dbo.Cliente (NegocioId, Nombre, Puntos, Activo, FechaCreacion) VALUES (@NegocioId, N'Carlos Rojas (Demo)', 35, 1, DATEADD(day, -28, SYSDATETIME()));
IF NOT EXISTS (SELECT 1 FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'María Flores (Demo)')
    INSERT INTO dbo.Cliente (NegocioId, Nombre, Puntos, Activo, FechaCreacion) VALUES (@NegocioId, N'María Flores (Demo)', 80, 1, DATEADD(day, -17, SYSDATETIME()));
IF NOT EXISTS (SELECT 1 FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Jorge Salazar (Demo)')
    INSERT INTO dbo.Cliente (NegocioId, Nombre, Puntos, Activo, FechaCreacion) VALUES (@NegocioId, N'Jorge Salazar (Demo)', 15, 1, DATEADD(day, -9, SYSDATETIME()));
IF NOT EXISTS (SELECT 1 FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Lucía Mendoza (Demo)')
    INSERT INTO dbo.Cliente (NegocioId, Nombre, Puntos, Activo, FechaCreacion) VALUES (@NegocioId, N'Lucía Mendoza (Demo)', 60, 1, DATEADD(day, -5, SYSDATETIME()));
IF NOT EXISTS (SELECT 1 FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Pedro Castillo (Demo)')
    INSERT INTO dbo.Cliente (NegocioId, Nombre, Puntos, Activo, FechaCreacion) VALUES (@NegocioId, N'Pedro Castillo (Demo)', 10, 1, DATEADD(day, -2, SYSDATETIME()));

DECLARE @Ana INT = (SELECT Id FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Ana Torres (Demo)');
DECLARE @Carlos INT = (SELECT Id FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Carlos Rojas (Demo)');
DECLARE @Maria INT = (SELECT Id FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'María Flores (Demo)');
DECLARE @Jorge INT = (SELECT Id FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Jorge Salazar (Demo)');
DECLARE @Lucia INT = (SELECT Id FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Lucía Mendoza (Demo)');
DECLARE @Pedro INT = (SELECT Id FROM dbo.Cliente WHERE NegocioId = @NegocioId AND Nombre = N'Pedro Castillo (Demo)');

DECLARE @Recepcion INT = (SELECT TOP 1 Id FROM dbo.AreaLavado WHERE SedeId = @SedeId AND Nombre = N'Recepcion');
DECLARE @Lavado INT = (SELECT TOP 1 Id FROM dbo.AreaLavado WHERE SedeId = @SedeId AND Nombre = N'Lavado');
DECLARE @Empacado INT = (SELECT TOP 1 Id FROM dbo.AreaLavado WHERE SedeId = @SedeId AND Nombre = N'Empacado');
DECLARE @LavadoKilo INT = (SELECT TOP 1 Id FROM dbo.Servicio WHERE NegocioId = @NegocioId AND Nombre = N'Lavado al agua por kilo');
DECLARE @Seco INT = (SELECT TOP 1 Id FROM dbo.Servicio WHERE NegocioId = @NegocioId AND Nombre = N'Lavado en seco');
DECLARE @Sabanas INT = (SELECT TOP 1 Id FROM dbo.Servicio WHERE NegocioId = @NegocioId AND Nombre = N'Sabanas 2 plazas');
DECLARE @Toallas INT = (SELECT TOP 1 Id FROM dbo.Servicio WHERE NegocioId = @NegocioId AND Nombre = N'Toallas');

DECLARE @NumeroBase INT = ISNULL((SELECT MAX(Numero) FROM dbo.Pedido WHERE SedeId = @SedeId), 0);

-- Cada estado permite que el visitante entienda el flujo completo sin tener que crear todo desde cero.
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-001')
    INSERT INTO dbo.Pedido (SedeId, Numero, ClienteId, UsuarioId, FechaIngreso, FechaEntregaEst, Modalidad, Subtotal, Descuento, EsUrgente, RecargoUrgente, Redondeo, Total, MontoPagado, EstadoPago, EstadoProceso, AreaActualId, Observaciones, CodigoAntiguo, Anulado, NotifRutaEnviada, NotifCercaEnviada, NotifLlegadaEnviada)
    VALUES (@SedeId, @NumeroBase + 1, @Ana, @UsuarioId, DATEADD(hour, -1, SYSDATETIME()), DATEADD(hour, 5, SYSDATETIME()), N'Tienda', 22.50, 0, 0, 0, 0, 22.50, 0, N'PENDIENTE', N'PENDIENTE', @Recepcion, N'Prendas de uso diario · datos ficticios de demo', 'DEMO-001', 0, 0, 0, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-002')
    INSERT INTO dbo.Pedido (SedeId, Numero, ClienteId, UsuarioId, FechaIngreso, FechaEntregaEst, Modalidad, Subtotal, Descuento, EsUrgente, RecargoUrgente, Redondeo, Total, MontoPagado, EstadoPago, EstadoProceso, AreaActualId, Observaciones, CodigoAntiguo, Anulado, NotifRutaEnviada, NotifCercaEnviada, NotifLlegadaEnviada)
    VALUES (@SedeId, @NumeroBase + 2, @Carlos, @UsuarioId, DATEADD(hour, -4, SYSDATETIME()), DATEADD(hour, 3, SYSDATETIME()), N'Tienda', 24.00, 0, 0, 0, 0, 24.00, 15.00, N'PARCIAL', N'EN_PROCESO', @Lavado, N'Camisas y pantalones · datos ficticios de demo', 'DEMO-002', 0, 0, 0, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-003')
    INSERT INTO dbo.Pedido (SedeId, Numero, ClienteId, UsuarioId, FechaIngreso, FechaEntregaEst, Modalidad, Subtotal, Descuento, EsUrgente, RecargoUrgente, Redondeo, Total, MontoPagado, EstadoPago, EstadoProceso, AreaActualId, Observaciones, CodigoAntiguo, Anulado, NotifRutaEnviada, NotifCercaEnviada, NotifLlegadaEnviada)
    VALUES (@SedeId, @NumeroBase + 3, @Maria, @UsuarioId, DATEADD(day, -1, SYSDATETIME()), DATEADD(hour, 2, SYSDATETIME()), N'Tienda', 17.50, 0, 0, 0, 0, 17.50, 17.50, N'PAGADO', N'LISTO', @Empacado, N'Pedido listo para recojo · datos ficticios de demo', 'DEMO-003', 0, 0, 0, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-004')
    INSERT INTO dbo.Pedido (SedeId, Numero, ClienteId, UsuarioId, FechaIngreso, FechaEntregaEst, FechaEntregaReal, Modalidad, Subtotal, Descuento, EsUrgente, RecargoUrgente, Redondeo, Total, MontoPagado, EstadoPago, EstadoProceso, AreaActualId, Observaciones, CodigoAntiguo, Anulado, NotifRutaEnviada, NotifCercaEnviada, NotifLlegadaEnviada)
    VALUES (@SedeId, @NumeroBase + 4, @Jorge, @UsuarioId, DATEADD(day, -1, SYSDATETIME()), DATEADD(hour, -1, DATEADD(day, -1, SYSDATETIME())), DATEADD(hour, -3, SYSDATETIME()), N'Tienda', 16.00, 0, 0, 0, 0, 16.00, 16.00, N'PAGADO', N'ENTREGADO', NULL, N'Entrega completada · datos ficticios de demo', 'DEMO-004', 0, 0, 0, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-005')
    INSERT INTO dbo.Pedido (SedeId, Numero, ClienteId, UsuarioId, FechaIngreso, FechaEntregaEst, Modalidad, Subtotal, Descuento, EsUrgente, RecargoUrgente, Redondeo, Total, MontoPagado, EstadoPago, EstadoProceso, AreaActualId, Observaciones, CodigoAntiguo, Anulado, NotifRutaEnviada, NotifCercaEnviada, NotifLlegadaEnviada)
    VALUES (@SedeId, @NumeroBase + 5, @Lucia, @UsuarioId, DATEADD(day, -2, SYSDATETIME()), DATEADD(hour, 7, SYSDATETIME()), N'Tienda', 18.00, 0, 1, 5.60, 0, 23.60, 0, N'PENDIENTE', N'EN_PROCESO', @Lavado, N'Servicio urgente · datos ficticios de demo', 'DEMO-005', 0, 0, 0, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-006')
    INSERT INTO dbo.Pedido (SedeId, Numero, ClienteId, UsuarioId, FechaIngreso, FechaEntregaEst, FechaEntregaReal, Modalidad, Subtotal, Descuento, EsUrgente, RecargoUrgente, Redondeo, Total, MontoPagado, EstadoPago, EstadoProceso, AreaActualId, Observaciones, CodigoAntiguo, Anulado, NotifRutaEnviada, NotifCercaEnviada, NotifLlegadaEnviada)
    VALUES (@SedeId, @NumeroBase + 6, @Pedro, @UsuarioId, DATEADD(day, -4, SYSDATETIME()), DATEADD(day, -3, SYSDATETIME()), DATEADD(hour, -1, DATEADD(day, -3, SYSDATETIME())), N'Tienda', 24.00, 0, 0, 0, 0, 24.00, 24.00, N'PAGADO', N'ENTREGADO', NULL, N'Pedido entregado · datos ficticios de demo', 'DEMO-006', 0, 0, 0, 0);

-- Ítems de los pedidos de muestra.
INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion, CantidadEntregada)
SELECT p.Id, @LavadoKilo, 5, 4.50, 22.50, N'Ropa diaria', 0 FROM dbo.Pedido p WHERE p.SedeId = @SedeId AND p.CodigoAntiguo = 'DEMO-001' AND NOT EXISTS (SELECT 1 FROM dbo.PedidoItem pi WHERE pi.PedidoId = p.Id);
INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion, CantidadEntregada)
SELECT p.Id, @Seco, 2, 12.00, 24.00, N'Prendas delicadas', 0 FROM dbo.Pedido p WHERE p.SedeId = @SedeId AND p.CodigoAntiguo = 'DEMO-002' AND NOT EXISTS (SELECT 1 FROM dbo.PedidoItem pi WHERE pi.PedidoId = p.Id);
INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion, CantidadEntregada)
SELECT p.Id, @Toallas, 5, 3.50, 17.50, N'Toallas', 0 FROM dbo.Pedido p WHERE p.SedeId = @SedeId AND p.CodigoAntiguo = 'DEMO-003' AND NOT EXISTS (SELECT 1 FROM dbo.PedidoItem pi WHERE pi.PedidoId = p.Id);
INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion, CantidadEntregada)
SELECT p.Id, @Sabanas, 2, 8.00, 16.00, N'Juego de cama', 2 FROM dbo.Pedido p WHERE p.SedeId = @SedeId AND p.CodigoAntiguo = 'DEMO-004' AND NOT EXISTS (SELECT 1 FROM dbo.PedidoItem pi WHERE pi.PedidoId = p.Id);
INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion, CantidadEntregada)
SELECT p.Id, @LavadoKilo, 4, 4.50, 18.00, N'Ropa diaria', 0 FROM dbo.Pedido p WHERE p.SedeId = @SedeId AND p.CodigoAntiguo = 'DEMO-005' AND NOT EXISTS (SELECT 1 FROM dbo.PedidoItem pi WHERE pi.PedidoId = p.Id);
INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion, CantidadEntregada)
SELECT p.Id, @Seco, 2, 12.00, 24.00, N'Sacos', 2 FROM dbo.Pedido p WHERE p.SedeId = @SedeId AND p.CodigoAntiguo = 'DEMO-006' AND NOT EXISTS (SELECT 1 FROM dbo.PedidoItem pi WHERE pi.PedidoId = p.Id);

-- Historial visible en el detalle para que se entienda el progreso del pedido.
INSERT INTO dbo.PedidoHistorial (PedidoId, AreaId, EstadoProceso, UsuarioId, Fecha, Nota, NotificadoWsp, ActorTipo, ActorDescripcion)
SELECT p.Id, p.AreaActualId, p.EstadoProceso, @UsuarioId, p.FechaIngreso, N'Pedido de demostración creado', 0, N'USUARIO', N'Datos ficticios LunaLav'
FROM dbo.Pedido p
WHERE p.SedeId = @SedeId AND p.CodigoAntiguo LIKE 'DEMO-%'
  AND NOT EXISTS (SELECT 1 FROM dbo.PedidoHistorial h WHERE h.PedidoId = p.Id AND h.Nota = N'Pedido de demostración creado');

-- Inventario ligero para que el módulo no se vea vacío.
IF NOT EXISTS (SELECT 1 FROM dbo.Insumo WHERE SedeId = @SedeId AND Nombre = N'Detergente líquido (Demo)')
    INSERT INTO dbo.Insumo (SedeId, Nombre, UnidadMedida, StockActual, StockMinimo, Activo, Clase, FechaIngreso) VALUES (@SedeId, N'Detergente líquido (Demo)', N'L', 12, 4, 1, N'INSUMO', CAST(SYSDATETIME() AS DATE));
IF NOT EXISTS (SELECT 1 FROM dbo.Insumo WHERE SedeId = @SedeId AND Nombre = N'Suavizante (Demo)')
    INSERT INTO dbo.Insumo (SedeId, Nombre, UnidadMedida, StockActual, StockMinimo, Activo, Clase, FechaIngreso) VALUES (@SedeId, N'Suavizante (Demo)', N'L', 3, 4, 1, N'INSUMO', CAST(SYSDATETIME() AS DATE));
IF NOT EXISTS (SELECT 1 FROM dbo.Insumo WHERE SedeId = @SedeId AND Nombre = N'Bolsas de empaque (Demo)')
    INSERT INTO dbo.Insumo (SedeId, Nombre, UnidadMedida, StockActual, StockMinimo, Activo, Clase, FechaIngreso) VALUES (@SedeId, N'Bolsas de empaque (Demo)', N'und', 45, 20, 1, N'INSUMO', CAST(SYSDATETIME() AS DATE));

COMMIT TRANSACTION;
GO
