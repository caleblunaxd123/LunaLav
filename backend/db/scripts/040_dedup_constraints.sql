-- 040: Impedir duplicados a nivel de base de datos (clientes, servicios, categorías).
--
-- Regla de "cliente duplicado" (misma persona ya registrada en el mismo negocio):
--   * mismo DNI  -> identificador único de una persona, o
--   * mismo Nombre + mismo Celular -> la misma persona reingresada.
-- IMPORTANTE: un celular COMPARTIDO por personas con nombres distintos (familias que usan
-- un solo número) NO es duplicado, por eso el índice de celular incluye el Nombre.
--
-- Servicios y categorías: nombre único por negocio.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- =====================================================================================
-- (1) Consolidar duplicados de CLIENTE existentes antes de crear los índices únicos.
--     Se conserva el registro más antiguo (MIN Id) de cada grupo; al conservado se le
--     reasignan pedidos, movimientos de puntos y el saldo de puntos de los duplicados,
--     que quedan inactivos. Es idempotente: si no hay duplicados, no hace nada.
-- =====================================================================================

-- 1a. Duplicados por Nombre + Celular
IF OBJECT_ID('tempdb..#dupNombreCel') IS NOT NULL DROP TABLE #dupNombreCel;
SELECT c.Id,
       MIN(c.Id) OVER (PARTITION BY c.NegocioId, c.Celular, UPPER(LTRIM(RTRIM(c.Nombre)))) AS KeeperId
INTO #dupNombreCel
FROM dbo.Cliente c
WHERE c.Activo = 1 AND c.Celular IS NOT NULL;

UPDATE k SET k.Puntos = k.Puntos + x.Extra
  FROM dbo.Cliente k
  JOIN (SELECT d.KeeperId, SUM(c.Puntos) AS Extra
          FROM #dupNombreCel d JOIN dbo.Cliente c ON c.Id = d.Id
         WHERE d.Id <> d.KeeperId
         GROUP BY d.KeeperId) x ON x.KeeperId = k.Id;
UPDATE p SET p.ClienteId = d.KeeperId
  FROM dbo.Pedido p JOIN #dupNombreCel d ON p.ClienteId = d.Id WHERE d.Id <> d.KeeperId;
UPDATE mp SET mp.ClienteId = d.KeeperId
  FROM dbo.MovimientoPuntos mp JOIN #dupNombreCel d ON mp.ClienteId = d.Id WHERE d.Id <> d.KeeperId;
UPDATE c SET c.Activo = 0
  FROM dbo.Cliente c JOIN #dupNombreCel d ON c.Id = d.Id WHERE d.Id <> d.KeeperId;
GO

-- 1b. Duplicados por DNI (sobre los que siguen activos)
IF OBJECT_ID('tempdb..#dupDni') IS NOT NULL DROP TABLE #dupDni;
SELECT c.Id,
       MIN(c.Id) OVER (PARTITION BY c.NegocioId, c.Dni) AS KeeperId
INTO #dupDni
FROM dbo.Cliente c
WHERE c.Activo = 1 AND c.Dni IS NOT NULL;

UPDATE k SET k.Puntos = k.Puntos + x.Extra
  FROM dbo.Cliente k
  JOIN (SELECT d.KeeperId, SUM(c.Puntos) AS Extra
          FROM #dupDni d JOIN dbo.Cliente c ON c.Id = d.Id
         WHERE d.Id <> d.KeeperId
         GROUP BY d.KeeperId) x ON x.KeeperId = k.Id;
UPDATE p SET p.ClienteId = d.KeeperId
  FROM dbo.Pedido p JOIN #dupDni d ON p.ClienteId = d.Id WHERE d.Id <> d.KeeperId;
UPDATE mp SET mp.ClienteId = d.KeeperId
  FROM dbo.MovimientoPuntos mp JOIN #dupDni d ON mp.ClienteId = d.Id WHERE d.Id <> d.KeeperId;
UPDATE c SET c.Activo = 0
  FROM dbo.Cliente c JOIN #dupDni d ON c.Id = d.Id WHERE d.Id <> d.KeeperId;
GO

-- =====================================================================================
-- (2) Índices únicos (idempotentes). Solo aplican a registros activos, así un registro
--     desactivado no impide reusar el mismo dato en uno nuevo.
-- =====================================================================================

-- Cliente: DNI único por negocio
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Cliente_Negocio_Dni')
    CREATE UNIQUE INDEX UX_Cliente_Negocio_Dni
        ON dbo.Cliente (NegocioId, Dni)
        WHERE Dni IS NOT NULL AND Activo = 1;
GO

-- Cliente: Nombre + Celular único por negocio (misma persona reingresada)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Cliente_Negocio_Nombre_Celular')
    CREATE UNIQUE INDEX UX_Cliente_Negocio_Nombre_Celular
        ON dbo.Cliente (NegocioId, Nombre, Celular)
        WHERE Celular IS NOT NULL AND Activo = 1;
GO

-- Servicio: nombre único por negocio (excluye el cargo de delivery de sistema)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Servicio_Negocio_Nombre')
    CREATE UNIQUE INDEX UX_Servicio_Negocio_Nombre
        ON dbo.Servicio (NegocioId, Nombre)
        WHERE EsCargoDelivery = 0;
GO

-- Categoria: nombre único por negocio (entre las activas)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Categoria_Negocio_Nombre')
    CREATE UNIQUE INDEX UX_Categoria_Negocio_Nombre
        ON dbo.Categoria (NegocioId, Nombre)
        WHERE Activa = 1;
GO

PRINT 'OK 040: duplicados consolidados e indices unicos creados (cliente/servicio/categoria).';
GO
