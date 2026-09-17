using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public interface IFacturacionRepository
{
    Task<ConfiguracionFacturacion?> ObtenerConfigAsync(int negocioId, CancellationToken ct = default);
    Task GuardarConfigAsync(ConfiguracionFacturacion c, CancellationToken ct = default);
    Task<int> SiguienteCorrelativoAsync(int negocioId, string tipo, CancellationToken ct = default);
    Task<int> CrearComprobanteAsync(ComprobanteElectronico c, CancellationToken ct = default);
    Task<(ComprobanteElectronico Comprobante, bool Creado)> CrearPendienteAtomicoAsync(
        ComprobanteElectronico c, List<ComprobanteElectronicoDetalle> detalles, CancellationToken ct = default);
    /// <summary>Crea una Nota de Crédito PENDIENTE reservando su propio correlativo (serie FC01/BC01)
    /// de forma atómica. A diferencia de la emisión de boleta/factura, un mismo pedido sí puede tener
    /// varias notas, por lo que no se aplica el bloqueo "un comprobante por pedido".</summary>
    Task<ComprobanteElectronico> CrearNotaAtomicaAsync(
        ComprobanteElectronico c, List<ComprobanteElectronicoDetalle> detalles, CancellationToken ct = default);
    Task<ComprobanteElectronico> CrearGuiaAtomicaAsync(
        ComprobanteElectronico c, List<ComprobanteElectronicoDetalle> detalles, GuiaRemisionDatos guia, CancellationToken ct = default);
    Task<GuiaRemisionDatos?> ObtenerGuiaDatosAsync(int comprobanteId, CancellationToken ct = default);
    /// <summary>Guarda el resultado de SUNAT. Acota por sede (defensa en profundidad: el Id ya
    /// viene de una lectura autorizada, pero el UPDATE no debe poder cruzar de sede nunca).</summary>
    Task ActualizarResultadoAsync(
        int id, int sedeId, string estado, string? codigoRespuesta, string? descripcionRespuesta,
        byte[]? xmlFirmado, byte[]? cdrZip, string? hashCpe, DateTime? fechaEnvio, CancellationToken ct = default);
    Task<ComprobanteElectronico?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default);
    /// <summary>Comprobante pendiente, aceptado o con error recuperable para este pedido.
    /// Un rechazado consume numeración y debe reemplazarse mediante una nueva emisión.</summary>
    Task<ComprobanteElectronico?> ObtenerVigentePorPedidoAsync(int pedidoId, CancellationToken ct = default);
    Task<(List<ComprobanteElectronico> Items, int Total)> ListarPaginadoAsync(int sedeId, int pagina, int tamanoPagina, CancellationToken ct = default);
    Task<(List<ComprobanteElectronico> Items, int Total)> ListarFiltradoAsync(int sedeId, string? tipo, string? estado,
        string? busqueda, DateTime? desde, DateTime? hasta, int pagina, int tamanoPagina, CancellationToken ct = default);
    Task<List<KpiComprobantesMesDto>> KpiMensualAsync(int sedeId, int meses, CancellationToken ct = default);
    Task<List<ComprobanteElectronicoDetalle>> ListarDetallesAsync(int comprobanteId, CancellationToken ct = default);
    Task<List<ComprobanteElectronicoIntento>> ListarIntentosAsync(int comprobanteId, CancellationToken ct = default);
    Task RegistrarIntentoAsync(int comprobanteId, string accion, string estado, string? codigo, string? descripcion, int? usuarioId, CancellationToken ct = default);
    Task ActualizarResultadoCompletoAsync(int id, int sedeId, string estado, string? codigo, string? descripcion,
        byte[]? xml, byte[]? cdr, string? hash, string? externalId, DateTime? fechaEnvio, DateTime? fechaRespuesta, CancellationToken ct = default);
    Task ActualizarAnulacionAsync(int id, int sedeId, string estadoAnulacion, string? estadoComprobante,
        string motivo, string? codigo, string? descripcion, DateTime? fechaAnulacion, CancellationToken ct = default);
    Task<bool> TieneComprobanteVigenteAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task<List<ComprobanteElectronico>> ListarPendientesGlobalAsync(int limite, CancellationToken ct = default);
    /// <summary>Ids (con su sede) de todos los comprobantes ACEPTADOS con XML de un negocio, para el respaldo local.</summary>
    Task<List<(int Id, int SedeId)>> ListarIdsAceptadosAsync(int negocioId, CancellationToken ct = default);
}

public class FacturacionRepository : IFacturacionRepository
{
    private readonly ISqlConnectionFactory _factory;
    public FacturacionRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string ConfigSelect = @"
        SELECT Id, NegocioId, RazonSocial, RucEmisor, Ambiente, SolUsuario, SolClaveCifrada,
               CertificadoPfx, CertificadoPasswordCifrada, SerieBoleta, SerieFactura,
               CorrelativoBoleta, CorrelativoFactura, Activo, SoloBoletas, Proveedor, ApiSunatPersonaId,
               ApiSunatTokenCifrado, DireccionFiscal, Ubigeo, CodigoEstablecimiento, EmailEmisor,
               SerieNotaCreditoFactura, SerieNotaCreditoBoleta,
               CorrelativoNotaCreditoFactura, CorrelativoNotaCreditoBoleta,
               SerieNotaDebitoFactura, SerieNotaDebitoBoleta,
               CorrelativoNotaDebitoFactura, CorrelativoNotaDebitoBoleta,
               SerieGuiaRemision, CorrelativoGuiaRemision
        FROM dbo.ConfiguracionFacturacion";

    private static ConfiguracionFacturacion MapConfig(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        RazonSocial = r.GetNullableString("RazonSocial"),
        RucEmisor = r.GetNullableString("RucEmisor"),
        Ambiente = r.GetString(r.GetOrdinal("Ambiente")),
        SolUsuario = r.GetNullableString("SolUsuario"),
        SolClaveCifrada = r.GetNullableString("SolClaveCifrada"),
        CertificadoPfx = r.IsDBNull(r.GetOrdinal("CertificadoPfx")) ? null : (byte[])r["CertificadoPfx"],
        CertificadoPasswordCifrada = r.GetNullableString("CertificadoPasswordCifrada"),
        SerieBoleta = r.GetString(r.GetOrdinal("SerieBoleta")),
        SerieFactura = r.GetString(r.GetOrdinal("SerieFactura")),
        CorrelativoBoleta = r.GetInt32(r.GetOrdinal("CorrelativoBoleta")),
        CorrelativoFactura = r.GetInt32(r.GetOrdinal("CorrelativoFactura")),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        SoloBoletas = r.GetBoolean(r.GetOrdinal("SoloBoletas")),
        Proveedor = r.GetString(r.GetOrdinal("Proveedor")),
        ApiSunatPersonaId = r.GetNullableString("ApiSunatPersonaId"),
        ApiSunatTokenCifrado = r.GetNullableString("ApiSunatTokenCifrado"),
        DireccionFiscal = r.GetNullableString("DireccionFiscal"),
        Ubigeo = r.GetNullableString("Ubigeo"),
        CodigoEstablecimiento = r.GetString(r.GetOrdinal("CodigoEstablecimiento")),
        EmailEmisor = r.GetNullableString("EmailEmisor"),
        SerieNotaCreditoFactura = r.GetString(r.GetOrdinal("SerieNotaCreditoFactura")),
        SerieNotaCreditoBoleta = r.GetString(r.GetOrdinal("SerieNotaCreditoBoleta")),
        CorrelativoNotaCreditoFactura = r.GetInt32(r.GetOrdinal("CorrelativoNotaCreditoFactura")),
        CorrelativoNotaCreditoBoleta = r.GetInt32(r.GetOrdinal("CorrelativoNotaCreditoBoleta")),
        SerieNotaDebitoFactura = r.GetString(r.GetOrdinal("SerieNotaDebitoFactura")),
        SerieNotaDebitoBoleta = r.GetString(r.GetOrdinal("SerieNotaDebitoBoleta")),
        CorrelativoNotaDebitoFactura = r.GetInt32(r.GetOrdinal("CorrelativoNotaDebitoFactura")),
        CorrelativoNotaDebitoBoleta = r.GetInt32(r.GetOrdinal("CorrelativoNotaDebitoBoleta")),
        SerieGuiaRemision = r.GetString(r.GetOrdinal("SerieGuiaRemision")),
        CorrelativoGuiaRemision = r.GetInt32(r.GetOrdinal("CorrelativoGuiaRemision"))
    };

    public async Task<ConfiguracionFacturacion?> ObtenerConfigAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ConfigSelect + " WHERE NegocioId = @NegocioId";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(MapConfig, ct);
    }

    public async Task GuardarConfigAsync(ConfiguracionFacturacion c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Upsert: el negocio puede no tener fila propia todavia (primera vez que configura).
        cmd.CommandText = @"
            MERGE dbo.ConfiguracionFacturacion AS target
            USING (SELECT @NegocioId AS NegocioId) AS src
                ON target.NegocioId = src.NegocioId
            WHEN MATCHED THEN UPDATE SET
                RazonSocial = @RazonSocial,
                RucEmisor = @RucEmisor,
                Ambiente = @Ambiente,
                SolUsuario = @SolUsuario,
                SolClaveCifrada = COALESCE(@SolClaveCifrada, target.SolClaveCifrada),
                CertificadoPfx = COALESCE(@CertificadoPfx, target.CertificadoPfx),
                CertificadoPasswordCifrada = COALESCE(@CertificadoPasswordCifrada, target.CertificadoPasswordCifrada),
                SerieBoleta = @SerieBoleta,
                SerieFactura = @SerieFactura,
                Proveedor = @Proveedor,
                ApiSunatPersonaId = @ApiSunatPersonaId,
                ApiSunatTokenCifrado = COALESCE(@ApiSunatTokenCifrado, target.ApiSunatTokenCifrado),
                DireccionFiscal = @DireccionFiscal, Ubigeo = @Ubigeo,
                CodigoEstablecimiento = @CodigoEstablecimiento, EmailEmisor = @EmailEmisor,
                Activo = @Activo, SoloBoletas = @SoloBoletas,
                FechaActualizacion = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (NegocioId, RazonSocial, RucEmisor, Ambiente, SolUsuario, SolClaveCifrada,
                 CertificadoPfx, CertificadoPasswordCifrada, SerieBoleta, SerieFactura, Activo, SoloBoletas,
                 Proveedor, ApiSunatPersonaId, ApiSunatTokenCifrado, DireccionFiscal, Ubigeo, CodigoEstablecimiento, EmailEmisor)
                VALUES
                (@NegocioId, @RazonSocial, @RucEmisor, @Ambiente, @SolUsuario, @SolClaveCifrada,
                 @CertificadoPfx, @CertificadoPasswordCifrada, @SerieBoleta, @SerieFactura, @Activo, @SoloBoletas,
                 @Proveedor, @ApiSunatPersonaId, @ApiSunatTokenCifrado, @DireccionFiscal, @Ubigeo, @CodigoEstablecimiento, @EmailEmisor);";
        cmd.AddParam("@NegocioId", c.NegocioId);
        cmd.AddParam("@RazonSocial", c.RazonSocial);
        cmd.AddParam("@RucEmisor", c.RucEmisor);
        cmd.AddParam("@Ambiente", c.Ambiente);
        cmd.AddParam("@SolUsuario", c.SolUsuario);
        cmd.AddParam("@SolClaveCifrada", c.SolClaveCifrada);
        cmd.AddBinaryParam("@CertificadoPfx", c.CertificadoPfx);
        cmd.AddParam("@CertificadoPasswordCifrada", c.CertificadoPasswordCifrada);
        cmd.AddParam("@SerieBoleta", c.SerieBoleta);
        cmd.AddParam("@SerieFactura", c.SerieFactura);
        cmd.AddParam("@Activo", c.Activo);
        cmd.AddParam("@SoloBoletas", c.SoloBoletas);
        cmd.AddParam("@Proveedor", c.Proveedor);
        cmd.AddParam("@ApiSunatPersonaId", c.ApiSunatPersonaId);
        cmd.AddParam("@ApiSunatTokenCifrado", c.ApiSunatTokenCifrado);
        cmd.AddParam("@DireccionFiscal", c.DireccionFiscal);
        cmd.AddParam("@Ubigeo", c.Ubigeo);
        cmd.AddParam("@CodigoEstablecimiento", c.CodigoEstablecimiento);
        cmd.AddParam("@EmailEmisor", c.EmailEmisor);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> SiguienteCorrelativoAsync(int negocioId, string tipo, CancellationToken ct = default)
    {
        var columna = tipo == "FACTURA" ? "CorrelativoFactura" : "CorrelativoBoleta";
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // UPDATE ... OUTPUT es atomico: seguro ante emisiones concurrentes del mismo negocio.
        cmd.CommandText = $@"
            UPDATE dbo.ConfiguracionFacturacion
               SET {columna} = {columna} + 1
            OUTPUT INSERTED.{columna}
             WHERE NegocioId = @NegocioId";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<int> CrearComprobanteAsync(ComprobanteElectronico c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.ComprobanteElectronico
                (NegocioId, SedeId, PedidoId, Tipo, Serie, Correlativo, ClienteNombre, ClienteTipoDoc,
                 ClienteNumDoc, OpGravada, Igv, Total, Estado, UsuarioId)
            OUTPUT INSERTED.Id
            VALUES
                (@NegocioId, @SedeId, @PedidoId, @Tipo, @Serie, @Correlativo, @ClienteNombre, @ClienteTipoDoc,
                 @ClienteNumDoc, @OpGravada, @Igv, @Total, @Estado, @UsuarioId);";
        cmd.AddParam("@NegocioId", c.NegocioId);
        cmd.AddParam("@SedeId", c.SedeId);
        cmd.AddParam("@PedidoId", c.PedidoId);
        cmd.AddParam("@Tipo", c.Tipo);
        cmd.AddParam("@Serie", c.Serie);
        cmd.AddParam("@Correlativo", c.Correlativo);
        cmd.AddParam("@ClienteNombre", c.ClienteNombre);
        cmd.AddParam("@ClienteTipoDoc", c.ClienteTipoDoc);
        cmd.AddParam("@ClienteNumDoc", c.ClienteNumDoc);
        cmd.AddParam("@OpGravada", c.OpGravada);
        cmd.AddParam("@Igv", c.Igv);
        cmd.AddParam("@Total", c.Total);
        cmd.AddParam("@Estado", c.Estado);
        cmd.AddParam("@UsuarioId", c.UsuarioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<(ComprobanteElectronico Comprobante, bool Creado)> CrearPendienteAtomicoAsync(
        ComprobanteElectronico c, List<ComprobanteElectronicoDetalle> detalles, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            await using (var lockCmd = conn.CreateCommand())
            {
                lockCmd.Transaction = tx;
                lockCmd.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource, 'Exclusive', 'Transaction', 10000; SELECT @r;";
                lockCmd.AddParam("@Resource", $"facturacion:pedido:{c.PedidoId}");
                var lockResult = await lockCmd.ReadScalarAsync<int>(ct);
                if (lockResult < 0) throw new InvalidOperationException("No se pudo reservar el pedido para emitir. Intenta nuevamente.");
            }

            await using (var vigenteCmd = conn.CreateCommand())
            {
                vigenteCmd.Transaction = tx;
                vigenteCmd.CommandText = ComprobanteSelect + @"
                    WHERE c.PedidoId = @PedidoId AND c.SedeId = @SedeId
                      AND c.Estado IN ('PENDIENTE','ACEPTADO','ERROR')
                    ORDER BY c.FechaEmision DESC";
                vigenteCmd.AddParam("@PedidoId", c.PedidoId).AddParam("@SedeId", c.SedeId);
                var vigente = await vigenteCmd.ReadFirstOrDefaultAsync(MapComprobante, ct);
                if (vigente is not null)
                {
                    await tx.CommitAsync(ct);
                    return (vigente, false);
                }
            }

            var columna = c.Tipo == "FACTURA" ? "CorrelativoFactura" : "CorrelativoBoleta";
            await using (var corrCmd = conn.CreateCommand())
            {
                corrCmd.Transaction = tx;
                corrCmd.CommandText = $@"UPDATE dbo.ConfiguracionFacturacion WITH (UPDLOCK, ROWLOCK)
                    SET {columna} = {columna} + 1 OUTPUT INSERTED.{columna} WHERE NegocioId = @NegocioId";
                corrCmd.AddParam("@NegocioId", c.NegocioId);
                c.Correlativo = await corrCmd.ReadScalarAsync<int>(ct);
                if (c.Correlativo <= 0) throw new InvalidOperationException("No se pudo reservar el correlativo.");
            }

            await using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = @"
                    INSERT dbo.ComprobanteElectronico
                      (NegocioId,SedeId,PedidoId,Tipo,Serie,Correlativo,ClienteNombre,ClienteTipoDoc,ClienteNumDoc,
                       OpGravada,Igv,Total,Estado,UsuarioId,Proveedor,Ambiente,RucEmisor,RazonSocialEmisor,
                       DireccionFiscalEmisor,UbigeoEmisor,CodigoEstablecimientoEmisor,Moneda,IgvPorcentaje,
                       Subtotal,Descuento,Recargo,Redondeo,EsSimulado,FechaEmision,FechaActualizacion)
                    OUTPUT INSERTED.Id
                    VALUES
                      (@NegocioId,@SedeId,@PedidoId,@Tipo,@Serie,@Correlativo,@ClienteNombre,@ClienteTipoDoc,@ClienteNumDoc,
                       @OpGravada,@Igv,@Total,'PENDIENTE',@UsuarioId,@Proveedor,@Ambiente,@RucEmisor,@RazonSocialEmisor,
                       @Direccion,@Ubigeo,@Establecimiento,'PEN',@IgvPct,@Subtotal,@Descuento,@Recargo,@Redondeo,0,@Fecha,SYSDATETIME())";
                insert.AddParam("@NegocioId", c.NegocioId).AddParam("@SedeId", c.SedeId).AddParam("@PedidoId", c.PedidoId)
                    .AddParam("@Tipo", c.Tipo).AddParam("@Serie", c.Serie).AddParam("@Correlativo", c.Correlativo)
                    .AddParam("@ClienteNombre", c.ClienteNombre).AddParam("@ClienteTipoDoc", c.ClienteTipoDoc)
                    .AddParam("@ClienteNumDoc", c.ClienteNumDoc).AddParam("@OpGravada", c.OpGravada).AddParam("@Igv", c.Igv)
                    .AddParam("@Total", c.Total).AddParam("@UsuarioId", c.UsuarioId).AddParam("@Proveedor", c.Proveedor)
                    .AddParam("@Ambiente", c.Ambiente).AddParam("@RucEmisor", c.RucEmisor)
                    .AddParam("@RazonSocialEmisor", c.RazonSocialEmisor).AddParam("@Direccion", c.DireccionFiscalEmisor)
                    .AddParam("@Ubigeo", c.UbigeoEmisor).AddParam("@Establecimiento", c.CodigoEstablecimientoEmisor)
                    .AddParam("@IgvPct", c.IgvPorcentaje).AddParam("@Subtotal", c.Subtotal)
                    .AddParam("@Descuento", c.Descuento).AddParam("@Recargo", c.Recargo)
                    .AddParam("@Redondeo", c.Redondeo).AddParam("@Fecha", c.FechaEmision);
                c.Id = await insert.ReadScalarAsync<int>(ct);
            }

            var numero = 0;
            foreach (var detalle in detalles)
            {
                detalle.ComprobanteId = c.Id;
                detalle.NumeroLinea = ++numero;
                await using var line = conn.CreateCommand();
                line.Transaction = tx;
                line.CommandText = @"INSERT dbo.ComprobanteElectronicoDetalle
                    (ComprobanteId,NumeroLinea,ServicioId,Descripcion,UnidadMedida,Cantidad,PrecioUnitarioIgv,ValorVenta,Igv,Total)
                    VALUES (@ComprobanteId,@Numero,@ServicioId,@Descripcion,@Unidad,@Cantidad,@Precio,@Valor,@Igv,@Total)";
                line.AddParam("@ComprobanteId", c.Id).AddParam("@Numero", numero).AddParam("@ServicioId", detalle.ServicioId)
                    .AddParam("@Descripcion", detalle.Descripcion).AddParam("@Unidad", detalle.UnidadMedida)
                    .AddParam("@Cantidad", detalle.Cantidad).AddParam("@Precio", detalle.PrecioUnitarioIgv)
                    .AddParam("@Valor", detalle.ValorVenta).AddParam("@Igv", detalle.Igv).AddParam("@Total", detalle.Total);
                await line.ExecuteNonQueryAsync(ct);
            }
            c.Detalles = detalles;
            await tx.CommitAsync(ct);
            return (c, true);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ComprobanteElectronico> CrearNotaAtomicaAsync(
        ComprobanteElectronico c, List<ComprobanteElectronicoDetalle> detalles, CancellationToken ct = default)
    {
        var esDebito = c.Tipo == "NOTA_DEBITO";
        var raiz = esDebito ? "CorrelativoNotaDebito" : "CorrelativoNotaCredito";
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            // Correlativo de la nota según su tipo (NC/ND) y el tipo del documento referenciado (01 factura / 03 boleta).
            var columna = c.DocRefTipo == "01" ? $"{raiz}Factura" : $"{raiz}Boleta";
            await using (var corrCmd = conn.CreateCommand())
            {
                corrCmd.Transaction = tx;
                corrCmd.CommandText = $@"UPDATE dbo.ConfiguracionFacturacion WITH (UPDLOCK, ROWLOCK)
                    SET {columna} = {columna} + 1 OUTPUT INSERTED.{columna} WHERE NegocioId = @NegocioId";
                corrCmd.AddParam("@NegocioId", c.NegocioId);
                c.Correlativo = await corrCmd.ReadScalarAsync<int>(ct);
                if (c.Correlativo <= 0) throw new InvalidOperationException("No se pudo reservar el correlativo de la nota.");
            }

            await using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = @"
                    INSERT dbo.ComprobanteElectronico
                      (NegocioId,SedeId,PedidoId,Tipo,Serie,Correlativo,ClienteNombre,ClienteTipoDoc,ClienteNumDoc,
                       OpGravada,Igv,Total,Estado,UsuarioId,Proveedor,Ambiente,RucEmisor,RazonSocialEmisor,
                       DireccionFiscalEmisor,UbigeoEmisor,CodigoEstablecimientoEmisor,Moneda,IgvPorcentaje,
                       Subtotal,Descuento,Recargo,Redondeo,EsSimulado,FechaEmision,FechaActualizacion,
                       ComprobanteRefId,DocRefTipo,DocRefSerieNumero,MotivoNotaCodigo,MotivoNotaDescripcion)
                    OUTPUT INSERTED.Id
                    VALUES
                      (@NegocioId,@SedeId,@PedidoId,@Tipo,@Serie,@Correlativo,@ClienteNombre,@ClienteTipoDoc,@ClienteNumDoc,
                       @OpGravada,@Igv,@Total,'PENDIENTE',@UsuarioId,@Proveedor,@Ambiente,@RucEmisor,@RazonSocialEmisor,
                       @Direccion,@Ubigeo,@Establecimiento,'PEN',@IgvPct,@Subtotal,@Descuento,@Recargo,@Redondeo,@EsSimulado,@Fecha,SYSDATETIME(),
                       @RefId,@DocRefTipo,@DocRefSerieNumero,@MotivoCod,@MotivoDesc)";
                insert.AddParam("@NegocioId", c.NegocioId).AddParam("@SedeId", c.SedeId).AddParam("@PedidoId", c.PedidoId)
                    .AddParam("@Tipo", c.Tipo).AddParam("@Serie", c.Serie).AddParam("@Correlativo", c.Correlativo)
                    .AddParam("@ClienteNombre", c.ClienteNombre).AddParam("@ClienteTipoDoc", c.ClienteTipoDoc)
                    .AddParam("@ClienteNumDoc", c.ClienteNumDoc).AddParam("@OpGravada", c.OpGravada).AddParam("@Igv", c.Igv)
                    .AddParam("@Total", c.Total).AddParam("@UsuarioId", c.UsuarioId).AddParam("@Proveedor", c.Proveedor)
                    .AddParam("@Ambiente", c.Ambiente).AddParam("@RucEmisor", c.RucEmisor)
                    .AddParam("@RazonSocialEmisor", c.RazonSocialEmisor).AddParam("@Direccion", c.DireccionFiscalEmisor)
                    .AddParam("@Ubigeo", c.UbigeoEmisor).AddParam("@Establecimiento", c.CodigoEstablecimientoEmisor)
                    .AddParam("@IgvPct", c.IgvPorcentaje).AddParam("@Subtotal", c.Subtotal)
                    .AddParam("@Descuento", c.Descuento).AddParam("@Recargo", c.Recargo)
                    .AddParam("@Redondeo", c.Redondeo).AddParam("@EsSimulado", c.EsSimulado).AddParam("@Fecha", c.FechaEmision)
                    .AddParam("@RefId", c.ComprobanteRefId).AddParam("@DocRefTipo", c.DocRefTipo)
                    .AddParam("@DocRefSerieNumero", c.DocRefSerieNumero).AddParam("@MotivoCod", c.MotivoNotaCodigo)
                    .AddParam("@MotivoDesc", c.MotivoNotaDescripcion);
                c.Id = await insert.ReadScalarAsync<int>(ct);
            }

            var numero = 0;
            foreach (var detalle in detalles)
            {
                detalle.ComprobanteId = c.Id;
                detalle.NumeroLinea = ++numero;
                await using var line = conn.CreateCommand();
                line.Transaction = tx;
                line.CommandText = @"INSERT dbo.ComprobanteElectronicoDetalle
                    (ComprobanteId,NumeroLinea,ServicioId,Descripcion,UnidadMedida,Cantidad,PrecioUnitarioIgv,ValorVenta,Igv,Total)
                    VALUES (@ComprobanteId,@Numero,@ServicioId,@Descripcion,@Unidad,@Cantidad,@Precio,@Valor,@Igv,@Total)";
                line.AddParam("@ComprobanteId", c.Id).AddParam("@Numero", numero).AddParam("@ServicioId", detalle.ServicioId)
                    .AddParam("@Descripcion", detalle.Descripcion).AddParam("@Unidad", detalle.UnidadMedida)
                    .AddParam("@Cantidad", detalle.Cantidad).AddParam("@Precio", detalle.PrecioUnitarioIgv)
                    .AddParam("@Valor", detalle.ValorVenta).AddParam("@Igv", detalle.Igv).AddParam("@Total", detalle.Total);
                await line.ExecuteNonQueryAsync(ct);
            }
            c.Detalles = detalles;
            await tx.CommitAsync(ct);
            return c;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ComprobanteElectronico> CrearGuiaAtomicaAsync(
        ComprobanteElectronico c, List<ComprobanteElectronicoDetalle> detalles, GuiaRemisionDatos guia, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            await using (var corrCmd = conn.CreateCommand())
            {
                corrCmd.Transaction = tx;
                corrCmd.CommandText = @"UPDATE dbo.ConfiguracionFacturacion WITH (UPDLOCK, ROWLOCK)
                    SET CorrelativoGuiaRemision = CorrelativoGuiaRemision + 1
                    OUTPUT INSERTED.CorrelativoGuiaRemision WHERE NegocioId = @NegocioId";
                corrCmd.AddParam("@NegocioId", c.NegocioId);
                c.Correlativo = await corrCmd.ReadScalarAsync<int>(ct);
                if (c.Correlativo <= 0) throw new InvalidOperationException("No se pudo reservar el correlativo de la guía de remisión.");
            }

            await using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = @"
                    INSERT dbo.ComprobanteElectronico
                      (NegocioId,SedeId,PedidoId,Tipo,Serie,Correlativo,ClienteNombre,ClienteTipoDoc,ClienteNumDoc,
                       OpGravada,Igv,Total,Estado,UsuarioId,Proveedor,Ambiente,RucEmisor,RazonSocialEmisor,
                       DireccionFiscalEmisor,UbigeoEmisor,CodigoEstablecimientoEmisor,Moneda,IgvPorcentaje,
                       Subtotal,Descuento,Recargo,Redondeo,EsSimulado,FechaEmision,FechaActualizacion)
                    OUTPUT INSERTED.Id
                    VALUES
                      (@NegocioId,@SedeId,@PedidoId,'GUIA_REMISION',@Serie,@Correlativo,@ClienteNombre,@ClienteTipoDoc,@ClienteNumDoc,
                       0,0,0,'PENDIENTE',@UsuarioId,@Proveedor,@Ambiente,@RucEmisor,@RazonSocialEmisor,
                       @Direccion,@Ubigeo,@Establecimiento,'PEN',@IgvPct,0,0,0,0,@EsSimulado,@Fecha,SYSDATETIME())";
                insert.AddParam("@NegocioId", c.NegocioId).AddParam("@SedeId", c.SedeId).AddParam("@PedidoId", c.PedidoId)
                    .AddParam("@Serie", c.Serie).AddParam("@Correlativo", c.Correlativo)
                    .AddParam("@ClienteNombre", c.ClienteNombre).AddParam("@ClienteTipoDoc", c.ClienteTipoDoc)
                    .AddParam("@ClienteNumDoc", c.ClienteNumDoc).AddParam("@UsuarioId", c.UsuarioId).AddParam("@Proveedor", c.Proveedor)
                    .AddParam("@Ambiente", c.Ambiente).AddParam("@RucEmisor", c.RucEmisor)
                    .AddParam("@RazonSocialEmisor", c.RazonSocialEmisor).AddParam("@Direccion", c.DireccionFiscalEmisor)
                    .AddParam("@Ubigeo", c.UbigeoEmisor).AddParam("@Establecimiento", c.CodigoEstablecimientoEmisor)
                    .AddParam("@IgvPct", c.IgvPorcentaje).AddParam("@EsSimulado", c.EsSimulado).AddParam("@Fecha", c.FechaEmision);
                c.Id = await insert.ReadScalarAsync<int>(ct);
            }

            await using (var g = conn.CreateCommand())
            {
                g.Transaction = tx;
                g.CommandText = @"INSERT dbo.GuiaRemisionDatos
                    (ComprobanteId,MotivoTrasladoCodigo,MotivoTrasladoDescripcion,PesoBrutoTotal,UnidadPeso,NumeroBultos,
                     FechaInicioTraslado,ModalidadTransporte,PartidaUbigeo,PartidaDireccion,LlegadaUbigeo,LlegadaDireccion,
                     TransportistaNumDoc,TransportistaRazonSocial,VehiculoPlaca,ConductorTipoDoc,ConductorNumDoc,ConductorNombres,ConductorLicencia)
                    VALUES
                    (@Id,@MotCod,@MotDesc,@Peso,@Unidad,@Bultos,@Fecha,@Modalidad,@PartUbi,@PartDir,@LlegUbi,@LlegDir,
                     @TransNum,@TransRazon,@Placa,@CondTipo,@CondNum,@CondNom,@CondLic)";
                g.AddParam("@Id", c.Id).AddParam("@MotCod", guia.MotivoTrasladoCodigo).AddParam("@MotDesc", guia.MotivoTrasladoDescripcion)
                    .AddParam("@Peso", guia.PesoBrutoTotal).AddParam("@Unidad", guia.UnidadPeso).AddParam("@Bultos", (object?)guia.NumeroBultos ?? DBNull.Value)
                    .AddParam("@Fecha", guia.FechaInicioTraslado).AddParam("@Modalidad", guia.ModalidadTransporte)
                    .AddParam("@PartUbi", guia.PartidaUbigeo).AddParam("@PartDir", guia.PartidaDireccion)
                    .AddParam("@LlegUbi", guia.LlegadaUbigeo).AddParam("@LlegDir", guia.LlegadaDireccion)
                    .AddParam("@TransNum", (object?)guia.TransportistaNumDoc ?? DBNull.Value).AddParam("@TransRazon", (object?)guia.TransportistaRazonSocial ?? DBNull.Value)
                    .AddParam("@Placa", (object?)guia.VehiculoPlaca ?? DBNull.Value).AddParam("@CondTipo", (object?)guia.ConductorTipoDoc ?? DBNull.Value)
                    .AddParam("@CondNum", (object?)guia.ConductorNumDoc ?? DBNull.Value).AddParam("@CondNom", (object?)guia.ConductorNombres ?? DBNull.Value)
                    .AddParam("@CondLic", (object?)guia.ConductorLicencia ?? DBNull.Value);
                await g.ExecuteNonQueryAsync(ct);
            }

            var numero = 0;
            foreach (var detalle in detalles)
            {
                detalle.ComprobanteId = c.Id;
                detalle.NumeroLinea = ++numero;
                await using var line = conn.CreateCommand();
                line.Transaction = tx;
                line.CommandText = @"INSERT dbo.ComprobanteElectronicoDetalle
                    (ComprobanteId,NumeroLinea,ServicioId,Descripcion,UnidadMedida,Cantidad,PrecioUnitarioIgv,ValorVenta,Igv,Total)
                    VALUES (@ComprobanteId,@Numero,@ServicioId,@Descripcion,@Unidad,@Cantidad,0,0,0,0)";
                line.AddParam("@ComprobanteId", c.Id).AddParam("@Numero", numero).AddParam("@ServicioId", detalle.ServicioId)
                    .AddParam("@Descripcion", detalle.Descripcion).AddParam("@Unidad", detalle.UnidadMedida)
                    .AddParam("@Cantidad", detalle.Cantidad);
                await line.ExecuteNonQueryAsync(ct);
            }
            c.Detalles = detalles;
            c.Guia = guia;
            guia.ComprobanteId = c.Id;
            await tx.CommitAsync(ct);
            return c;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<GuiaRemisionDatos?> ObtenerGuiaDatosAsync(int comprobanteId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM dbo.GuiaRemisionDatos WHERE ComprobanteId = @Id";
        cmd.AddParam("@Id", comprobanteId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new GuiaRemisionDatos
        {
            ComprobanteId = comprobanteId,
            MotivoTrasladoCodigo = r.GetString(r.GetOrdinal("MotivoTrasladoCodigo")),
            MotivoTrasladoDescripcion = r.GetNullableString("MotivoTrasladoDescripcion"),
            PesoBrutoTotal = r.GetDecimal(r.GetOrdinal("PesoBrutoTotal")),
            UnidadPeso = r.GetString(r.GetOrdinal("UnidadPeso")),
            NumeroBultos = r.IsDBNull(r.GetOrdinal("NumeroBultos")) ? null : r.GetInt32(r.GetOrdinal("NumeroBultos")),
            FechaInicioTraslado = r.GetDateTime(r.GetOrdinal("FechaInicioTraslado")),
            ModalidadTransporte = r.GetString(r.GetOrdinal("ModalidadTransporte")),
            PartidaUbigeo = r.GetString(r.GetOrdinal("PartidaUbigeo")),
            PartidaDireccion = r.GetString(r.GetOrdinal("PartidaDireccion")),
            LlegadaUbigeo = r.GetString(r.GetOrdinal("LlegadaUbigeo")),
            LlegadaDireccion = r.GetString(r.GetOrdinal("LlegadaDireccion")),
            TransportistaNumDoc = r.GetNullableString("TransportistaNumDoc"),
            TransportistaRazonSocial = r.GetNullableString("TransportistaRazonSocial"),
            VehiculoPlaca = r.GetNullableString("VehiculoPlaca"),
            ConductorTipoDoc = r.GetNullableString("ConductorTipoDoc"),
            ConductorNumDoc = r.GetNullableString("ConductorNumDoc"),
            ConductorNombres = r.GetNullableString("ConductorNombres"),
            ConductorLicencia = r.GetNullableString("ConductorLicencia")
        };
    }

    public async Task ActualizarResultadoAsync(
        int id, int sedeId, string estado, string? codigoRespuesta, string? descripcionRespuesta,
        byte[]? xmlFirmado, byte[]? cdrZip, string? hashCpe, DateTime? fechaEnvio, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.ComprobanteElectronico
               SET Estado = @Estado, CodigoRespuestaSunat = @Codigo, DescripcionRespuestaSunat = @Descripcion,
                   XmlFirmado = @Xml, CdrZip = @Cdr, HashCpe = @Hash, FechaEnvio = @FechaEnvio
             WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Estado", estado);
        cmd.AddParam("@Codigo", codigoRespuesta);
        cmd.AddParam("@Descripcion", descripcionRespuesta);
        // AddWithValue no puede inferir el tipo SQL de un byte[] nulo (lo trata como nvarchar,
        // lo que choca contra la columna VARBINARY(MAX)); se tipa explicito para ese caso.
        cmd.Parameters.Add("@Xml", System.Data.SqlDbType.VarBinary).Value = (object?)xmlFirmado ?? DBNull.Value;
        cmd.Parameters.Add("@Cdr", System.Data.SqlDbType.VarBinary).Value = (object?)cdrZip ?? DBNull.Value;
        cmd.AddParam("@Hash", hashCpe);
        cmd.AddParam("@FechaEnvio", fechaEnvio);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private const string ComprobanteSelect = @"
        SELECT c.Id, c.NegocioId, c.SedeId, c.PedidoId, p.Numero AS PedidoNumero, c.Tipo, c.Serie, c.Correlativo,
               c.ClienteNombre, c.ClienteTipoDoc, c.ClienteNumDoc, c.OpGravada, c.Igv, c.Total, c.Estado,
               c.CodigoRespuestaSunat, c.DescripcionRespuestaSunat, c.XmlFirmado, c.CdrZip, c.HashCpe,
               c.FechaEmision, c.FechaEnvio, c.UsuarioId, c.Proveedor, c.Ambiente, c.ExternalId,
               c.RucEmisor, c.RazonSocialEmisor, c.DireccionFiscalEmisor, c.UbigeoEmisor,
               c.CodigoEstablecimientoEmisor, c.Moneda, c.IgvPorcentaje, c.Subtotal, c.Descuento,
               c.Recargo, c.Redondeo, c.EsSimulado, c.FechaActualizacion, c.FechaRespuesta,
               c.EstadoAnulacion, c.MotivoAnulacion, c.FechaSolicitudAnulacion, c.FechaAnulacion,
               c.ComprobanteRefId, c.DocRefTipo, c.DocRefSerieNumero, c.MotivoNotaCodigo, c.MotivoNotaDescripcion
        FROM dbo.ComprobanteElectronico c
        INNER JOIN dbo.Pedido p ON p.Id = c.PedidoId";

    private static ComprobanteElectronico MapComprobante(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        SedeId = r.GetInt32(r.GetOrdinal("SedeId")),
        PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
        PedidoNumero = r.GetNullableInt("PedidoNumero"),
        Tipo = r.GetString(r.GetOrdinal("Tipo")),
        Serie = r.GetString(r.GetOrdinal("Serie")),
        Correlativo = r.GetInt32(r.GetOrdinal("Correlativo")),
        ClienteNombre = r.GetString(r.GetOrdinal("ClienteNombre")),
        ClienteTipoDoc = r.GetString(r.GetOrdinal("ClienteTipoDoc")),
        ClienteNumDoc = r.GetNullableString("ClienteNumDoc"),
        OpGravada = r.GetDecimal(r.GetOrdinal("OpGravada")),
        Igv = r.GetDecimal(r.GetOrdinal("Igv")),
        Total = r.GetDecimal(r.GetOrdinal("Total")),
        Estado = r.GetString(r.GetOrdinal("Estado")),
        CodigoRespuestaSunat = r.GetNullableString("CodigoRespuestaSunat"),
        DescripcionRespuestaSunat = r.GetNullableString("DescripcionRespuestaSunat"),
        XmlFirmado = r.IsDBNull(r.GetOrdinal("XmlFirmado")) ? null : (byte[])r["XmlFirmado"],
        CdrZip = r.IsDBNull(r.GetOrdinal("CdrZip")) ? null : (byte[])r["CdrZip"],
        HashCpe = r.GetNullableString("HashCpe"),
        FechaEmision = r.GetDateTime(r.GetOrdinal("FechaEmision")),
        FechaEnvio = r.IsDBNull(r.GetOrdinal("FechaEnvio")) ? null : r.GetDateTime(r.GetOrdinal("FechaEnvio")),
        UsuarioId = r.GetInt32(r.GetOrdinal("UsuarioId")),
        Proveedor = r.GetString(r.GetOrdinal("Proveedor")),
        Ambiente = r.GetString(r.GetOrdinal("Ambiente")),
        ExternalId = r.GetNullableString("ExternalId"),
        RucEmisor = r.GetNullableString("RucEmisor"),
        RazonSocialEmisor = r.GetNullableString("RazonSocialEmisor"),
        DireccionFiscalEmisor = r.GetNullableString("DireccionFiscalEmisor"),
        UbigeoEmisor = r.GetNullableString("UbigeoEmisor"),
        CodigoEstablecimientoEmisor = r.GetNullableString("CodigoEstablecimientoEmisor"),
        Moneda = r.GetString(r.GetOrdinal("Moneda")),
        IgvPorcentaje = r.GetDecimal(r.GetOrdinal("IgvPorcentaje")),
        Subtotal = r.GetDecimal(r.GetOrdinal("Subtotal")),
        Descuento = r.GetDecimal(r.GetOrdinal("Descuento")),
        Recargo = r.GetDecimal(r.GetOrdinal("Recargo")),
        Redondeo = r.GetDecimal(r.GetOrdinal("Redondeo")),
        EsSimulado = r.GetBoolean(r.GetOrdinal("EsSimulado")),
        FechaActualizacion = r.GetDateTime(r.GetOrdinal("FechaActualizacion")),
        FechaRespuesta = r.GetNullableDateTime("FechaRespuesta"),
        EstadoAnulacion = r.GetNullableString("EstadoAnulacion"),
        MotivoAnulacion = r.GetNullableString("MotivoAnulacion"),
        FechaSolicitudAnulacion = r.GetNullableDateTime("FechaSolicitudAnulacion"),
        FechaAnulacion = r.GetNullableDateTime("FechaAnulacion"),
        ComprobanteRefId = r.GetNullableInt("ComprobanteRefId"),
        DocRefTipo = r.GetNullableString("DocRefTipo"),
        DocRefSerieNumero = r.GetNullableString("DocRefSerieNumero"),
        MotivoNotaCodigo = r.GetNullableString("MotivoNotaCodigo"),
        MotivoNotaDescripcion = r.GetNullableString("MotivoNotaDescripcion")
    };

    public async Task<ComprobanteElectronico?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ComprobanteSelect + " WHERE c.Id = @Id AND c.SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        var comprobante = await cmd.ReadFirstOrDefaultAsync(MapComprobante, ct);
        if (comprobante is not null)
            comprobante.Detalles = await ListarDetallesAsync(comprobante.Id, ct);
        return comprobante;
    }

    public async Task<ComprobanteElectronico?> ObtenerVigentePorPedidoAsync(int pedidoId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ComprobanteSelect + @"
            WHERE c.PedidoId = @PedidoId AND c.Estado IN ('PENDIENTE', 'ACEPTADO', 'ERROR')
            ORDER BY c.FechaEmision DESC";
        cmd.AddParam("@PedidoId", pedidoId);
        return await cmd.ReadFirstOrDefaultAsync(MapComprobante, ct);
    }

    public async Task<(List<ComprobanteElectronico> Items, int Total)> ListarPaginadoAsync(
        int sedeId, int pagina, int tamanoPagina, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ComprobanteSelect + @"
            WHERE c.SedeId = @SedeId
            ORDER BY c.FechaEmision DESC
            OFFSET @Salto ROWS FETCH NEXT @Tamano ROWS ONLY";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Salto", (pagina - 1) * tamanoPagina);
        cmd.AddParam("@Tamano", tamanoPagina);
        var items = await cmd.ReadListAsync(MapComprobante, ct);

        await using var cmdCount = conn.CreateCommand();
        cmdCount.CommandText = "SELECT COUNT(1) FROM dbo.ComprobanteElectronico WHERE SedeId = @SedeId";
        cmdCount.AddParam("@SedeId", sedeId);
        var total = await cmdCount.ReadScalarAsync<int>(ct);

        return (items, total);
    }

    public async Task<(List<ComprobanteElectronico> Items, int Total)> ListarFiltradoAsync(int sedeId,
        string? tipo, string? estado, string? busqueda, DateTime? desde, DateTime? hasta,
        int pagina, int tamanoPagina, CancellationToken ct = default)
    {
        var condiciones = new List<string> { "c.SedeId=@SedeId" };
        if (!string.IsNullOrWhiteSpace(tipo)) condiciones.Add("c.Tipo=@Tipo");
        if (!string.IsNullOrWhiteSpace(estado)) condiciones.Add("c.Estado=@Estado");
        if (desde.HasValue) condiciones.Add("c.FechaEmision>=@Desde");
        if (hasta.HasValue) condiciones.Add("c.FechaEmision<DATEADD(day,1,@Hasta)");
        if (!string.IsNullOrWhiteSpace(busqueda))
            condiciones.Add("(c.ClienteNombre LIKE @Busqueda OR c.ClienteNumDoc LIKE @Busqueda OR c.Serie + '-' + CONVERT(varchar(12),c.Correlativo) LIKE @Busqueda OR CONVERT(varchar(12),p.Numero) LIKE @Busqueda)");
        var where = " WHERE " + string.Join(" AND ", condiciones);
        await using var conn = _factory.Create(); await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ComprobanteSelect + where + " ORDER BY c.FechaEmision DESC OFFSET @Salto ROWS FETCH NEXT @Tamano ROWS ONLY";
        AgregarFiltros(cmd, sedeId, tipo, estado, busqueda, desde, hasta);
        cmd.AddParam("@Salto", (pagina - 1) * tamanoPagina).AddParam("@Tamano", tamanoPagina);
        var items = await cmd.ReadListAsync(MapComprobante, ct);
        await using var count = conn.CreateCommand();
        count.CommandText = "SELECT COUNT(1) FROM dbo.ComprobanteElectronico c INNER JOIN dbo.Pedido p ON p.Id=c.PedidoId" + where;
        AgregarFiltros(count, sedeId, tipo, estado, busqueda, desde, hasta);
        return (items, await count.ReadScalarAsync<int>(ct));
    }

    private static void AgregarFiltros(SqlCommand cmd, int sedeId, string? tipo, string? estado,
        string? busqueda, DateTime? desde, DateTime? hasta)
    {
        cmd.AddParam("@SedeId", sedeId);
        if (!string.IsNullOrWhiteSpace(tipo)) cmd.AddParam("@Tipo", tipo);
        if (!string.IsNullOrWhiteSpace(estado)) cmd.AddParam("@Estado", estado);
        if (desde.HasValue) cmd.AddParam("@Desde", desde.Value.Date);
        if (hasta.HasValue) cmd.AddParam("@Hasta", hasta.Value.Date);
        if (!string.IsNullOrWhiteSpace(busqueda)) cmd.AddParam("@Busqueda", $"%{busqueda.Trim()}%");
    }

    public async Task<List<KpiComprobantesMesDto>> KpiMensualAsync(int sedeId, int meses, CancellationToken ct = default)
    {
        meses = Math.Clamp(meses, 1, 24);
        var hoy = DateTime.Today;
        var desde = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-(meses - 1));

        // Todos los meses del rango, para que salgan con 0 si no hubo emisiones.
        var buckets = new Dictionary<(int Anio, int Mes), KpiComprobantesMesDto>();
        for (var i = 0; i < meses; i++)
        {
            var d = desde.AddMonths(i);
            buckets[(d.Year, d.Month)] = new KpiComprobantesMesDto { Anio = d.Year, Mes = d.Month };
        }

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT YEAR(FechaEmision) AS Anio, MONTH(FechaEmision) AS Mes, Tipo,
                   COUNT(*) AS Cantidad, SUM(Total) AS Monto
            FROM dbo.ComprobanteElectronico
            WHERE SedeId = @SedeId AND Estado = 'ACEPTADO' AND EsSimulado = 0 AND FechaEmision >= @Desde
                  AND Tipo IN ('BOLETA', 'FACTURA')
            GROUP BY YEAR(FechaEmision), MONTH(FechaEmision), Tipo";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Desde", desde);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var anio = r.GetInt32(0);
            var mes = r.GetInt32(1);
            var tipo = r.GetString(2);
            var cantidad = r.GetInt32(3);
            var monto = r.IsDBNull(4) ? 0m : r.GetDecimal(4);
            if (!buckets.TryGetValue((anio, mes), out var b)) continue;
            if (tipo == "FACTURA") { b.FacturasCantidad = cantidad; b.FacturasMonto = monto; }
            else { b.BoletasCantidad = cantidad; b.BoletasMonto = monto; }
        }

        return buckets.Values.OrderBy(b => b.Anio).ThenBy(b => b.Mes).ToList();
    }

    public async Task<List<ComprobanteElectronicoDetalle>> ListarDetallesAsync(int comprobanteId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id,ComprobanteId,NumeroLinea,ServicioId,Descripcion,UnidadMedida,Cantidad,
                                   PrecioUnitarioIgv,ValorVenta,Igv,Total
                              FROM dbo.ComprobanteElectronicoDetalle WHERE ComprobanteId=@Id ORDER BY NumeroLinea";
        cmd.AddParam("@Id", comprobanteId);
        return await cmd.ReadListAsync(r => new ComprobanteElectronicoDetalle
        {
            Id = r.GetInt32(0), ComprobanteId = r.GetInt32(1), NumeroLinea = r.GetInt32(2),
            ServicioId = r.IsDBNull(3) ? null : r.GetInt32(3), Descripcion = r.GetString(4),
            UnidadMedida = r.GetString(5), Cantidad = r.GetDecimal(6), PrecioUnitarioIgv = r.GetDecimal(7),
            ValorVenta = r.GetDecimal(8), Igv = r.GetDecimal(9), Total = r.GetDecimal(10)
        }, ct);
    }

    public async Task<List<ComprobanteElectronicoIntento>> ListarIntentosAsync(int comprobanteId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id,ComprobanteId,Accion,Estado,Codigo,Descripcion,Fecha,UsuarioId
                              FROM dbo.ComprobanteElectronicoIntento WHERE ComprobanteId=@Id ORDER BY Fecha DESC";
        cmd.AddParam("@Id", comprobanteId);
        return await cmd.ReadListAsync(r => new ComprobanteElectronicoIntento
        {
            Id = r.GetInt64(0), ComprobanteId = r.GetInt32(1), Accion = r.GetString(2), Estado = r.GetString(3),
            Codigo = r.IsDBNull(4) ? null : r.GetString(4), Descripcion = r.IsDBNull(5) ? null : r.GetString(5),
            Fecha = r.GetDateTime(6), UsuarioId = r.IsDBNull(7) ? null : r.GetInt32(7)
        }, ct);
    }

    public async Task RegistrarIntentoAsync(int comprobanteId, string accion, string estado, string? codigo,
        string? descripcion, int? usuarioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT dbo.ComprobanteElectronicoIntento
            (ComprobanteId,Accion,Estado,Codigo,Descripcion,UsuarioId) VALUES (@Id,@Accion,@Estado,@Codigo,@Descripcion,@UsuarioId)";
        cmd.AddParam("@Id", comprobanteId).AddParam("@Accion", accion).AddParam("@Estado", estado)
            .AddParam("@Codigo", codigo).AddParam("@Descripcion", descripcion is { Length: > 1000 } ? descripcion[..1000] : descripcion)
            .AddParam("@UsuarioId", usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ActualizarResultadoCompletoAsync(int id, int sedeId, string estado, string? codigo,
        string? descripcion, byte[]? xml, byte[]? cdr, string? hash, string? externalId,
        DateTime? fechaEnvio, DateTime? fechaRespuesta, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE dbo.ComprobanteElectronico SET Estado=@Estado,CodigoRespuestaSunat=@Codigo,
            DescripcionRespuestaSunat=@Descripcion,XmlFirmado=COALESCE(@Xml,XmlFirmado),CdrZip=COALESCE(@Cdr,CdrZip),
            HashCpe=COALESCE(@Hash,HashCpe),ExternalId=COALESCE(@ExternalId,ExternalId),
            FechaEnvio=COALESCE(@FechaEnvio,FechaEnvio),FechaRespuesta=COALESCE(@FechaRespuesta,FechaRespuesta),
            FechaActualizacion=SYSDATETIME() WHERE Id=@Id AND SedeId=@SedeId";
        cmd.AddParam("@Id", id).AddParam("@SedeId", sedeId).AddParam("@Estado", estado).AddParam("@Codigo", codigo)
            .AddParam("@Descripcion", descripcion is { Length: > 500 } ? descripcion[..500] : descripcion)
            .AddParam("@Hash", hash).AddParam("@ExternalId", externalId).AddParam("@FechaEnvio", fechaEnvio)
            .AddParam("@FechaRespuesta", fechaRespuesta);
        cmd.Parameters.Add("@Xml", System.Data.SqlDbType.VarBinary).Value = (object?)xml ?? DBNull.Value;
        cmd.Parameters.Add("@Cdr", System.Data.SqlDbType.VarBinary).Value = (object?)cdr ?? DBNull.Value;
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("El comprobante ya no esta disponible.");
    }

    public async Task ActualizarAnulacionAsync(int id, int sedeId, string estadoAnulacion, string? estadoComprobante,
        string motivo, string? codigo, string? descripcion, DateTime? fechaAnulacion, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE dbo.ComprobanteElectronico SET EstadoAnulacion=@EstadoAnulacion,
            Estado=COALESCE(@Estado,Estado),MotivoAnulacion=@Motivo,FechaSolicitudAnulacion=COALESCE(FechaSolicitudAnulacion,SYSDATETIME()),
            FechaAnulacion=@FechaAnulacion,CodigoRespuestaSunat=@Codigo,DescripcionRespuestaSunat=@Descripcion,
            FechaActualizacion=SYSDATETIME() WHERE Id=@Id AND SedeId=@SedeId";
        cmd.AddParam("@Id", id).AddParam("@SedeId", sedeId).AddParam("@EstadoAnulacion", estadoAnulacion)
            .AddParam("@Estado", estadoComprobante).AddParam("@Motivo", motivo).AddParam("@FechaAnulacion", fechaAnulacion)
            .AddParam("@Codigo", codigo).AddParam("@Descripcion", descripcion);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("El comprobante ya no esta disponible.");
    }

    public async Task<bool> TieneComprobanteVigenteAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM dbo.ComprobanteElectronico WHERE PedidoId=@PedidoId AND SedeId=@SedeId AND Estado IN ('PENDIENTE','ACEPTADO','ERROR')";
        cmd.AddParam("@PedidoId", pedidoId).AddParam("@SedeId", sedeId);
        return await cmd.ReadScalarAsync<int>(ct) > 0;
    }

    public async Task<List<ComprobanteElectronico>> ListarPendientesGlobalAsync(int limite, CancellationToken ct = default)
    {
        await using var conn = _factory.Create(); await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ComprobanteSelect + @" WHERE (c.Estado='PENDIENTE' OR c.EstadoAnulacion='PENDIENTE') AND c.EsSimulado=0
            AND c.ExternalId IS NOT NULL AND c.FechaActualizacion < DATEADD(second,-10,SYSDATETIME())
            ORDER BY c.FechaActualizacion OFFSET 0 ROWS FETCH NEXT @Limite ROWS ONLY";
        cmd.AddParam("@Limite", Math.Clamp(limite, 1, 100));
        return await cmd.ReadListAsync(MapComprobante, ct);
    }

    public async Task<List<(int Id, int SedeId)>> ListarIdsAceptadosAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create(); await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, SedeId FROM dbo.ComprobanteElectronico
            WHERE NegocioId = @NegocioId AND Estado = 'ACEPTADO' AND XmlFirmado IS NOT NULL
            ORDER BY Id";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => (r.GetInt32(0), r.GetInt32(1)), ct);
    }
}
