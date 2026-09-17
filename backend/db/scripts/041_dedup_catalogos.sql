-- 041: Candado de base de datos contra duplicados en el resto de catálogos.
-- La aplicación ya valida el nombre/DNI/celular antes de insertar en cada uno de estos
-- (InsumosController, AreasLavadoAdminController, PersonalController, MotorizadosController,
-- TiposGastoAdminController, RolPersonal, SedesController), así que estos índices son la red
-- de seguridad final: garantizan que ni un error de código ni dos operaciones simultáneas
-- puedan colar un duplicado. Solo aplican a registros activos, para poder reusar el mismo
-- dato tras dar de baja uno.
--
-- (No incluye PlantillaWhatsapp: sus filas se siembran/gestionan por evento y admiten varias.)
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Insumo: nombre único por sede
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Insumo_Sede_Nombre')
    CREATE UNIQUE INDEX UX_Insumo_Sede_Nombre
        ON dbo.Insumo (SedeId, Nombre) WHERE Activo = 1;
GO

-- Área de lavado: nombre único por sede (el orden lo gestiona la app, puede reordenarse)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_AreaLavado_Sede_Nombre')
    CREATE UNIQUE INDEX UX_AreaLavado_Sede_Nombre
        ON dbo.AreaLavado (SedeId, Nombre) WHERE Activa = 1;
GO

-- Empleado: DNI único por sede
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Empleado_Sede_Dni')
    CREATE UNIQUE INDEX UX_Empleado_Sede_Dni
        ON dbo.Empleado (SedeId, Dni) WHERE Dni IS NOT NULL AND Activo = 1;
GO

-- Motorizado / repartidor: celular único por sede
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Motorizado_Sede_Celular')
    CREATE UNIQUE INDEX UX_Motorizado_Sede_Celular
        ON dbo.Motorizado (SedeId, Celular) WHERE Celular IS NOT NULL AND Activo = 1;
GO

-- Tipo de gasto: nombre único por negocio
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TipoGasto_Negocio_Nombre')
    CREATE UNIQUE INDEX UX_TipoGasto_Negocio_Nombre
        ON dbo.TipoGasto (NegocioId, Nombre) WHERE Activo = 1;
GO

-- Rol de personal: nombre único por negocio
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RolPersonal_Negocio_Nombre')
    CREATE UNIQUE INDEX UX_RolPersonal_Negocio_Nombre
        ON dbo.RolPersonal (NegocioId, Nombre) WHERE Activo = 1;
GO

-- Sede: nombre único por negocio
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Sede_Negocio_Nombre')
    CREATE UNIQUE INDEX UX_Sede_Negocio_Nombre
        ON dbo.Sede (NegocioId, Nombre) WHERE Activo = 1;
GO

PRINT 'OK 041: indices unicos de catalogos creados (insumo/area/empleado/motorizado/tipogasto/rolpersonal/sede).';
GO
