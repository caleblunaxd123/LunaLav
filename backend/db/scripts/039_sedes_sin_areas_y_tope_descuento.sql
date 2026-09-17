-- 039: Reparación de datos detectada en auditoría QA (Fase 1)
-- (a) Sedes existentes creadas sin áreas de lavado: sus pedidos no podían avanzar
--     ("No hay áreas de lavado configuradas para esta sede"). Se siembra el flujo estándar.
-- (b) Negocios con MaxDescuentoPct = 0 (sin tope): se fija el tope por defecto de 30%
--     para cerrar el vector de descuentos ilimitados. El dueño puede cambiarlo en Ajustes.
SET QUOTED_IDENTIFIER ON;
GO

-- (a) Sembrar áreas estándar en toda sede activa que no tenga ninguna
INSERT INTO dbo.AreaLavado (SedeId, Nombre, Orden, TiempoEstMinutos, Activa)
SELECT s.Id, a.Nombre, a.Orden, a.TiempoEstMinutos, 1
FROM dbo.Sede s
CROSS JOIN (VALUES
    (N'Recepcion', 1, 15),
    (N'Lavado', 2, 60),
    (N'Secado', 3, 45),
    (N'Doblado', 4, 20),
    (N'Control de calidad', 5, 10),
    (N'Empacado', 6, 5)
) AS a(Nombre, Orden, TiempoEstMinutos)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AreaLavado al WHERE al.SedeId = s.Id);
GO

-- (b) Tope de descuento por defecto en negocios que quedaron sin tope
UPDATE dbo.ConfiguracionNegocio
   SET MaxDescuentoPct = 30
 WHERE MaxDescuentoPct = 0;
GO

-- (c) Sedes que tienen áreas pero TODAS desactivadas (mismo síntoma: pedidos no avanzan).
--     Se reactivan sus áreas existentes. A futuro el API ya impide desactivar la última activa.
UPDATE al
   SET al.Activa = 1
  FROM dbo.AreaLavado al
 WHERE al.Activa = 0
   AND NOT EXISTS (SELECT 1 FROM dbo.AreaLavado x WHERE x.SedeId = al.SedeId AND x.Activa = 1);
GO

PRINT 'OK 039: areas sembradas en sedes vacias y tope de descuento aplicado.';
GO
