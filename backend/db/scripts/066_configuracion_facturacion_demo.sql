-- Configuración exclusivamente local para reservar correlativos de documentos simulados.
-- No contiene credenciales SOL, certificado ni conexión con SUNAT.
USE LunaLav;
GO
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @NegocioId INT = (SELECT Id FROM dbo.Negocio WHERE Slug = 'demo');
IF @NegocioId IS NULL
    THROW 51002, 'No se encontro el tenant demo para configurar facturacion simulada.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionFacturacion WHERE NegocioId = @NegocioId)
BEGIN
    INSERT dbo.ConfiguracionFacturacion
      (NegocioId,RazonSocial,Ambiente,SerieBoleta,SerieFactura,CorrelativoBoleta,CorrelativoFactura,Activo,Proveedor,DireccionFiscal,Ubigeo,CodigoEstablecimiento,EmailEmisor,SoloBoletas)
    VALUES
      (@NegocioId,N'Lavandería Demo LunaLav','BETA','B001','F001',1,1,1,'SUNAT_DIRECTO',N'Dirección de demostración',N'150101','0000',NULL,0);
END
GO
