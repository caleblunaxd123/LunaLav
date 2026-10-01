-- ============================================================
-- 079: Sub-permisos finos (Domain/PermisosFinos.cs) - compatibilidad.
--
-- Los permisos finos se guardan en dbo.RolPermiso (Modulo = la clave del permiso) y, para un
-- rol de trabajador, "ausente = oculto". Antes de este cambio esos botones eran visibles para
-- cualquiera con el modulo, asi que los roles YA existentes conservan lo que hoy pueden hacer:
-- cada rol que tiene el modulo activo recibe sus sub-permisos activados. El administrador del
-- negocio puede quitarlos despues desde Ajustes > Usuarios y permisos.
-- Los roles de sistema (ADMIN, PROPIETARIO) no se tocan. Idempotente.
-- ============================================================
USE LunaLav;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

DECLARE @Finos TABLE (Clave NVARCHAR(40) NOT NULL PRIMARY KEY, Modulo NVARCHAR(40) NOT NULL);
INSERT INTO @Finos (Clave, Modulo) VALUES
    ('CAJA_VER_CIERRES_ANTERIORES', 'CAJA'),
    ('CAJA_VER_MONTOS_DIGITALES',   'CAJA'),
    ('CAJA_REPORTE_CUADRES',        'CAJA'),
    ('CAJA_REGISTRAR_GASTO',        'CAJA'),
    ('CAJA_VER_OTROS_TURNOS',       'CAJA'),
    ('INICIO_VER_MONTOS',           'INICIO'),
    ('PEDIDOS_ANULAR',              'PEDIDOS'),
    ('REGISTRAR_APLICAR_DESCUENTO', 'REGISTRAR'),
    ('CLIENTES_FUSIONAR',           'CLIENTES'),
    ('INVENTARIO_VER_COSTOS',       'INVENTARIO'),
    ('REPORTES_VER_GERENCIAL',      'REPORTES'),
    ('REPORTES_VER_CONSOLIDADO',    'REPORTES');

INSERT INTO dbo.RolPermiso (NegocioId, RolId, Modulo, PuedeAcceder)
SELECT p.NegocioId, p.RolId, f.Clave, 1
FROM dbo.RolPermiso p
JOIN dbo.Rol r ON r.Id = p.RolId AND r.EsSistema = 0
JOIN @Finos f ON f.Modulo = p.Modulo
WHERE p.PuedeAcceder = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.RolPermiso x
      WHERE x.NegocioId = p.NegocioId AND x.RolId = p.RolId AND x.Modulo = f.Clave);

PRINT CONCAT('079: ', @@ROWCOUNT, ' sub-permisos concedidos a roles existentes.');
GO
