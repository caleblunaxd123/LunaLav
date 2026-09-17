USE LunaLav;
GO

-- Roles
IF NOT EXISTS (SELECT 1 FROM dbo.Rol WHERE Codigo = 'ADMIN')
    INSERT INTO dbo.Rol (Codigo, Nombre) VALUES
        ('ADMIN', 'Administrador'),
        ('COORDINADOR', 'Coordinador'),
        ('TRABAJADOR', 'Trabajador');
GO

-- Usuario admin inicial (password: admin123 — cambiar en produccion)
-- Hash BCrypt generado en runtime al primer arranque (ver Program.cs seed) si no existe.
-- Se deja para que el seed en C# lo cree con hash valido.

-- Configuracion negocio por defecto
IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionNegocio)
    INSERT INTO dbo.ConfiguracionNegocio (NombreNegocio, ColorPrimario, ColorSecundario, ColorAcento)
    VALUES (N'Mi Lavanderia', '#0b57d0', '#29b6f6', '#f5a623');
GO

-- Areas de lavado
IF NOT EXISTS (SELECT 1 FROM dbo.AreaLavado)
    INSERT INTO dbo.AreaLavado (Nombre, Orden, TiempoEstMinutos) VALUES
        (N'Recepcion', 1, 15),
        (N'Lavado', 2, 60),
        (N'Secado', 3, 45),
        (N'Doblado', 4, 20),
        (N'Control de calidad', 5, 10),
        (N'Empacado', 6, 5);
GO

-- Categorias base
IF NOT EXISTS (SELECT 1 FROM dbo.Categoria)
    INSERT INTO dbo.Categoria (Nombre) VALUES
        (N'Ropa por kilo'),
        (N'Ropa especial'),
        (N'Hogar'),
        (N'Adicionales');
GO

-- Servicios base
IF NOT EXISTS (SELECT 1 FROM dbo.Servicio)
BEGIN
    DECLARE @CatKilo INT = (SELECT Id FROM dbo.Categoria WHERE Nombre = N'Ropa por kilo');
    DECLARE @CatEsp  INT = (SELECT Id FROM dbo.Categoria WHERE Nombre = N'Ropa especial');
    DECLARE @CatHog  INT = (SELECT Id FROM dbo.Categoria WHERE Nombre = N'Hogar');
    DECLARE @CatAd   INT = (SELECT Id FROM dbo.Categoria WHERE Nombre = N'Adicionales');

    INSERT INTO dbo.Servicio (Nombre, Precio, Unidad, CategoriaId) VALUES
        (N'Lavado al agua por kilo', 4.50, 'kg', @CatKilo),
        (N'Lavado en seco', 12.00, 'prenda', @CatEsp),
        (N'Sabanas 2 plazas', 8.00, 'pieza', @CatHog),
        (N'Toallas', 3.50, 'pieza', @CatHog),
        (N'Doblado', 3.00, 'prenda', @CatAd),
        (N'Desmanchado', 6.00, 'prenda', @CatAd);
END
GO

-- Tipos de gasto
IF NOT EXISTS (SELECT 1 FROM dbo.TipoGasto)
    INSERT INTO dbo.TipoGasto (Nombre) VALUES
        (N'Detergente / suministros'),
        (N'Servicios (agua, luz, gas)'),
        (N'Sueldos'),
        (N'Alquiler'),
        (N'Otros');
GO

-- Plantillas de WhatsApp
IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaWhatsapp)
    INSERT INTO dbo.PlantillaWhatsapp (Evento, Mensaje) VALUES
        ('INGRESO',
         N'Hola {cliente}! Recibimos tu pedido #{numero} en {negocio}. Total: S/ {total}. Entrega estimada: {entrega}. Gracias!'),
        ('CAMBIO_AREA',
         N'Hola {cliente}, tu pedido #{numero} ya esta en la etapa: {area}. Tiempo estimado restante: {tiempoRestante}.'),
        ('LISTO',
         N'Hola {cliente}! Tu pedido #{numero} esta LISTO para recoger en {negocio}. Te esperamos!'),
        ('DEMORA',
         N'Hola {cliente}, tu pedido #{numero} tendra una demora. Nueva hora estimada: {entrega}. Disculpa las molestias.'),
        ('ENTREGADO',
         N'Gracias por tu preferencia, {cliente}! Pedido #{numero} entregado. Total: S/ {total}.');
GO

