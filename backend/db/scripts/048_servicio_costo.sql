-- 048: Costo por servicio para calcular rentabilidad (margen) por producto/lavado.
-- El sistema ya guardaba el precio de venta; agregamos el costo estimado (quimicos, agua,
-- luz, mano de obra) para que el Reporte de Servicios muestre Margen (S/) y Margen %.
-- Arranca en 0 (el negocio lo completa desde Ajustes -> Servicios).
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.Servicio', 'Costo') IS NULL
    ALTER TABLE dbo.Servicio ADD Costo DECIMAL(10,2) NOT NULL CONSTRAINT DF_Servicio_Costo DEFAULT 0;
GO

PRINT 'OK 048: dbo.Servicio.Costo agregado.';
GO
