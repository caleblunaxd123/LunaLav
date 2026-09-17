-- ============================================================
-- 021: Rol PROPIETARIO + negocio reservado de plataforma + backfill de
-- RolPermiso para negocios existentes que quedaron sin ninguna fila.
--
-- Contexto del backfill: al dar de alta negocios de prueba (Speedy Wash,
-- El Rapido) por SQL directo se olvido sembrar RolPermiso. Cualquier usuario
-- no-ADMIN de esos negocios recibia modulosPermitidos=[] al iniciar sesion y
-- el guard del frontend lo desconectaba en silencio. Este script deja
-- cualquier negocio existente sin permisos con los mismos defaults que ya
-- usa el producto por defecto, y el alta de negocios nuevos (NegociosController)
-- siembra estos mismos defaults desde el principio.
--
-- Es re-ejecutable: cada paso valida si ya se aplico antes de tocar nada.
-- ============================================================
USE LunaLav;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Rol WHERE Codigo = 'PROPIETARIO')
    INSERT INTO dbo.Rol (Codigo, Nombre) VALUES ('PROPIETARIO', 'Propietario de la plataforma');
GO

-- Negocio "paraguas" reservado donde vive el usuario propietario. Nunca es un
-- tenant real: Activo=0 para que no aparezca en ningun listado de clientes.
IF NOT EXISTS (SELECT 1 FROM dbo.Negocio WHERE Slug = 'plataforma-interna')
    INSERT INTO dbo.Negocio (Nombre, Slug, Activo) VALUES (N'Plataforma (interno)', 'plataforma-interna', 0);
GO

-- Backfill: negocios existentes sin ninguna fila de RolPermiso reciben los
-- defaults del producto. Se calcula el snapshot ANTES de insertar para
-- que un negocio no se "auto-excluya" tras el primer INSERT (COORDINADOR).
DECLARE @NegociosSinPermisos TABLE (Id INT);
INSERT INTO @NegociosSinPermisos (Id)
SELECT n.Id FROM dbo.Negocio n
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolPermiso rp WHERE rp.NegocioId = n.Id);

DECLARE @rolCoord INT = (SELECT Id FROM dbo.Rol WHERE Codigo = 'COORDINADOR');
DECLARE @rolTrab  INT = (SELECT Id FROM dbo.Rol WHERE Codigo = 'TRABAJADOR');

INSERT INTO dbo.RolPermiso (NegocioId, RolId, Modulo, PuedeAcceder)
SELECT ns.Id, @rolCoord, m.Modulo, m.PuedeAcceder
FROM @NegociosSinPermisos ns
CROSS JOIN (VALUES
    ('INICIO', 1), ('PEDIDOS', 1), ('REGISTRAR', 1), ('CAJA', 1), ('CLIENTES', 1),
    ('PROMOCIONES', 0), ('REPORTES', 1), ('INVENTARIO', 1), ('AJUSTES', 0)
) AS m(Modulo, PuedeAcceder)
WHERE @rolCoord IS NOT NULL;

INSERT INTO dbo.RolPermiso (NegocioId, RolId, Modulo, PuedeAcceder)
SELECT ns.Id, @rolTrab, m.Modulo, m.PuedeAcceder
FROM @NegociosSinPermisos ns
CROSS JOIN (VALUES
    ('INICIO', 1), ('PEDIDOS', 1), ('REGISTRAR', 1), ('CAJA', 0), ('CLIENTES', 0),
    ('PROMOCIONES', 0), ('REPORTES', 0), ('INVENTARIO', 0), ('AJUSTES', 0)
) AS m(Modulo, PuedeAcceder)
WHERE @rolTrab IS NOT NULL;
GO

