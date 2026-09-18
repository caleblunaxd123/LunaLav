-- Restablece EXCLUSIVAMENTE el tenant cuyo slug es "demo".
-- No toca negocios reales, como LavandaLuna, ni los interesados comerciales.
-- Se ejecuta desde scripts/restablecer-demo-lunalav.ps1 antes de recargar
-- los datos ficticios que ve el visitante.
USE LunaLav;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;

DECLARE @NegocioId INT = (SELECT Id FROM dbo.Negocio WHERE Slug = 'demo');
IF @NegocioId IS NULL
    THROW 51010, 'No existe el tenant demo; se cancela el restablecimiento.', 1;

-- Documentos y dependencias de pedidos.
DELETE gd FROM dbo.GuiaRemisionDatos gd
JOIN dbo.ComprobanteElectronico ce ON ce.Id = gd.ComprobanteId
WHERE ce.NegocioId = @NegocioId;
DELETE ci FROM dbo.ComprobanteElectronicoIntento ci
JOIN dbo.ComprobanteElectronico ce ON ce.Id = ci.ComprobanteId
WHERE ce.NegocioId = @NegocioId;
DELETE cd FROM dbo.ComprobanteElectronicoDetalle cd
JOIN dbo.ComprobanteElectronico ce ON ce.Id = cd.ComprobanteId
WHERE ce.NegocioId = @NegocioId;
DELETE FROM dbo.ComprobanteElectronico WHERE NegocioId = @NegocioId;

DELETE ed FROM dbo.PedidoEntregaDetalle ed
JOIN dbo.PedidoEntrega e ON e.Id = ed.EntregaId
JOIN dbo.Sede s ON s.Id = e.SedeId
WHERE s.NegocioId = @NegocioId;
DELETE e FROM dbo.PedidoEntrega e
JOIN dbo.Sede s ON s.Id = e.SedeId
WHERE s.NegocioId = @NegocioId;
DELETE FROM dbo.PedidoFoto WHERE NegocioId = @NegocioId;
DELETE h FROM dbo.PedidoHistorial h
JOIN dbo.Pedido p ON p.Id = h.PedidoId
JOIN dbo.Sede s ON s.Id = p.SedeId
WHERE s.NegocioId = @NegocioId;
DELETE i FROM dbo.PedidoItem i
JOIN dbo.Pedido p ON p.Id = i.PedidoId
JOIN dbo.Sede s ON s.Id = p.SedeId
WHERE s.NegocioId = @NegocioId;
DELETE sp FROM dbo.SolicitudPago sp WHERE sp.NegocioId = @NegocioId;

-- Caja e inventario creados durante las pruebas de visitantes.
DELETE mi FROM dbo.MovimientoInsumo mi
JOIN dbo.Sede s ON s.Id = mi.SedeId
WHERE s.NegocioId = @NegocioId;
DELETE mc FROM dbo.MovimientoCaja mc
JOIN dbo.Sede s ON s.Id = mc.SedeId
WHERE s.NegocioId = @NegocioId;
DELETE cc FROM dbo.CuadreCaja cc
JOIN dbo.Sede s ON s.Id = cc.SedeId
WHERE s.NegocioId = @NegocioId;

DELETE p FROM dbo.Pedido p
JOIN dbo.Sede s ON s.Id = p.SedeId
WHERE s.NegocioId = @NegocioId;
DELETE i FROM dbo.Insumo i
JOIN dbo.Sede s ON s.Id = i.SedeId
WHERE s.NegocioId = @NegocioId;

-- Catálogos transaccionales y clientes propios de la demo.
DELETE FROM dbo.Promocion WHERE NegocioId = @NegocioId;
DELETE mp FROM dbo.MovimientoPuntos mp
JOIN dbo.Cliente c ON c.Id = mp.ClienteId
WHERE c.NegocioId = @NegocioId;
DELETE FROM dbo.Cliente WHERE NegocioId = @NegocioId;
DELETE FROM dbo.ConfiguracionFacturacion WHERE NegocioId = @NegocioId;

COMMIT TRANSACTION;
GO
