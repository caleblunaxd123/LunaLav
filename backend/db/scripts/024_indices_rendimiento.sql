-- ============================================================
-- 024: Indices compuestos para los patrones de consulta reales
--
-- Los indices existentes en Pedido/MovimientoCaja son de una sola columna
-- (EstadoProceso, FechaIngreso, Fecha) pero las consultas del dia a dia siempre
-- filtran ademas por SedeId (multi-sede). Un indice de una sola columna no le
-- sirve a SQL Server para un seek acotado cuando el filtro real es compuesto;
-- termina escaneando mas de lo necesario a medida que crece el volumen.
--
-- Es re-ejecutable: cada CREATE INDEX valida si ya existe antes de crearlo.
-- ============================================================
USE LunaLav;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Pedido_Sede_Estado' AND object_id = OBJECT_ID('dbo.Pedido'))
    CREATE INDEX IX_Pedido_Sede_Estado ON dbo.Pedido(SedeId, EstadoProceso) INCLUDE (Anulado);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Pedido_Sede_FechaIngreso' AND object_id = OBJECT_ID('dbo.Pedido'))
    CREATE INDEX IX_Pedido_Sede_FechaIngreso ON dbo.Pedido(SedeId, FechaIngreso DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MovimientoCaja_Sede_Fecha' AND object_id = OBJECT_ID('dbo.MovimientoCaja'))
    CREATE INDEX IX_MovimientoCaja_Sede_Fecha ON dbo.MovimientoCaja(SedeId, Fecha);
GO

