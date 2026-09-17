-- 046: Identidad de marca LunaLav como estilo por defecto de TODA la app.
-- El área de trabajo de cada negocio se pinta con SUS colores guardados (ColorPrimario/
-- Secundario/Acento). Hasta ahora todos tenían el default genérico viejo (#0b57d0 azul
-- Google / #29b6f6 / #f5a623 naranja), por eso el workspace no seguía la marca LunaLav.
-- La paleta oficial usa azul noche, turquesa y violeta.
-- Solo actualiza los que aún tienen el default viejo, para NO pisar a un negocio que a
-- futuro personalice su propia marca desde Ajustes → Negocio.
SET QUOTED_IDENTIFIER ON;
GO

UPDATE dbo.ConfiguracionNegocio
   SET ColorPrimario   = '#10233f',
       ColorSecundario = '#2dd4bf',
       ColorAcento     = '#8b5cf6'
 WHERE ColorPrimario = '#0b57d0'
   AND ColorSecundario = '#29b6f6';
GO

PRINT 'OK 046: negocios con default viejo actualizados a la paleta LunaLav.';
GO
