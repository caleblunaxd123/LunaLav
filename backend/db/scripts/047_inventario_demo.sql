-- 047: Inventario mínimo y ficticio para la demostración pública.
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @SedeId INT = (
    SELECT TOP 1 s.Id
      FROM dbo.Sede s
      JOIN dbo.Negocio n ON n.Id = s.NegocioId
     WHERE n.Slug = 'demo'
     ORDER BY s.Id
);

IF @SedeId IS NULL
    PRINT '047: no se encontró el tenant demo; se omite el inventario.';
ELSE
BEGIN
    INSERT INTO dbo.Insumo
        (SedeId, Nombre, UnidadMedida, Clase, ContenidoValor, ContenidoUnidad, StockActual, StockMinimo, Activo)
    SELECT @SedeId, v.Nombre, v.Unidad, v.Clase, NULL, NULL, v.Stock, v.Minimo, 1
      FROM (VALUES
        (N'Detergente líquido', N'L',      N'INSUMO',   CAST(24 AS DECIMAL(12,3)), CAST(8 AS DECIMAL(12,3))),
        (N'Suavizante textil',  N'L',      N'INSUMO',   CAST(18 AS DECIMAL(12,3)), CAST(6 AS DECIMAL(12,3))),
        (N'Quitamanchas',       N'L',      N'INSUMO',   CAST(8  AS DECIMAL(12,3)), CAST(3 AS DECIMAL(12,3))),
        (N'Bolsas de entrega',  N'unidad', N'INSUMO',   CAST(80 AS DECIMAL(12,3)), CAST(20 AS DECIMAL(12,3))),
        (N'Canastas de ropa',   N'unidad', N'MATERIAL', CAST(12 AS DECIMAL(12,3)), CAST(4 AS DECIMAL(12,3))),
        (N'Balanza digital',    N'unidad', N'EQUIPO',   CAST(2  AS DECIMAL(12,3)), CAST(1 AS DECIMAL(12,3)))
      ) AS v(Nombre, Unidad, Clase, Stock, Minimo)
     WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Insumo i WHERE i.SedeId = @SedeId AND i.Nombre = v.Nombre
     );

    PRINT 'OK 047: inventario ficticio cargado para la demo.';
END
GO
