using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IConfiguracionNegocioRepository
{
    /// <summary>
    /// negocioId null: usado por el endpoint publico (pre-login) que pinta la marca. Devuelve
    /// el primer negocio de esta instancia (cada despliegue de Lavanderia sirve a un solo
    /// negocio por ahora, ver roadmap de resolucion por subdominio para SaaS compartido).
    /// </summary>
    Task<ConfiguracionNegocio?> ObtenerAsync(int? negocioId, CancellationToken ct = default);
    Task ActualizarAsync(ConfiguracionNegocio c, int negocioId, CancellationToken ct = default);
}

public class ConfiguracionNegocioRepository : IConfiguracionNegocioRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ConfiguracionNegocioRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<ConfiguracionNegocio?> ObtenerAsync(int? negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var whereNegocio = negocioId.HasValue ? " WHERE NegocioId = @NegocioId " : "";
        cmd.CommandText = @$"
            SELECT TOP 1 Id, NombreNegocio, LogoUrl, ColorPrimario, ColorSecundario, ColorAcento,
                         Direccion, Telefono, Ruc, HorarioAtencion, Igv, MetaMensual, SolesPorPunto,
                         AnchoTicketMm, MensajePieTicket, CondicionesServicio, NotasProduccion, CostoDelivery,
                         ValorPuntoCanje, MaxDescuentoPct, YapeNumero, YapeTitular, YapeQrUrl
            FROM dbo.ConfiguracionNegocio
            {whereNegocio}
            ORDER BY Id";
        if (negocioId.HasValue) cmd.AddParam("@NegocioId", negocioId.Value);
        return await cmd.ReadFirstOrDefaultAsync(r => new ConfiguracionNegocio
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            NombreNegocio = r.GetString(r.GetOrdinal("NombreNegocio")),
            LogoUrl = r.GetNullableString("LogoUrl"),
            ColorPrimario = r.GetString(r.GetOrdinal("ColorPrimario")),
            ColorSecundario = r.GetString(r.GetOrdinal("ColorSecundario")),
            ColorAcento = r.GetString(r.GetOrdinal("ColorAcento")),
            Direccion = r.GetNullableString("Direccion"),
            Telefono = r.GetNullableString("Telefono"),
            Ruc = r.GetNullableString("Ruc"),
            HorarioAtencion = r.GetNullableString("HorarioAtencion"),
            Igv = r.GetDecimal(r.GetOrdinal("Igv")),
            MetaMensual = r.GetDecimal(r.GetOrdinal("MetaMensual")),
            SolesPorPunto = r.GetDecimal(r.GetOrdinal("SolesPorPunto")),
            AnchoTicketMm = r.GetInt32(r.GetOrdinal("AnchoTicketMm")),
            MensajePieTicket = r.GetNullableString("MensajePieTicket"),
            CondicionesServicio = r.GetNullableString("CondicionesServicio"),
            NotasProduccion = r.GetNullableString("NotasProduccion"),
            CostoDelivery = r.GetDecimal(r.GetOrdinal("CostoDelivery")),
            ValorPuntoCanje = r.GetDecimal(r.GetOrdinal("ValorPuntoCanje")),
            MaxDescuentoPct = r.GetDecimal(r.GetOrdinal("MaxDescuentoPct")),
            YapeNumero = r.GetNullableString("YapeNumero"),
            YapeTitular = r.GetNullableString("YapeTitular"),
            YapeQrUrl = r.GetNullableString("YapeQrUrl")
        }, ct);
    }

    public async Task ActualizarAsync(ConfiguracionNegocio c, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Upsert: una sede/negocio nuevo aun no tiene fila propia en ConfiguracionNegocio.
        cmd.CommandText = @"
            MERGE dbo.ConfiguracionNegocio AS target
            USING (SELECT @NegocioId AS NegocioId) AS src
                ON target.NegocioId = src.NegocioId
            WHEN MATCHED THEN UPDATE SET
                NombreNegocio = @NombreNegocio,
                LogoUrl = @LogoUrl,
                ColorPrimario = @ColorPrimario,
                ColorSecundario = @ColorSecundario,
                ColorAcento = @ColorAcento,
                Direccion = @Direccion,
                Telefono = @Telefono,
                Ruc = @Ruc,
                HorarioAtencion = @HorarioAtencion,
                Igv = @Igv,
                MetaMensual = @MetaMensual,
                SolesPorPunto = @SolesPorPunto,
                AnchoTicketMm = @AnchoTicketMm,
                MensajePieTicket = @MensajePieTicket,
                CondicionesServicio = @CondicionesServicio,
                NotasProduccion = @NotasProduccion,
                CostoDelivery = @CostoDelivery,
                ValorPuntoCanje = @ValorPuntoCanje,
                MaxDescuentoPct = @MaxDescuentoPct,
                YapeNumero = @YapeNumero,
                YapeTitular = @YapeTitular,
                YapeQrUrl = @YapeQrUrl,
                FechaActualizacion = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (NegocioId, NombreNegocio, LogoUrl, ColorPrimario, ColorSecundario, ColorAcento,
                 Direccion, Telefono, Ruc, HorarioAtencion, Igv, MetaMensual, SolesPorPunto,
                 AnchoTicketMm, MensajePieTicket, CondicionesServicio, NotasProduccion, CostoDelivery,
                 ValorPuntoCanje, MaxDescuentoPct, YapeNumero, YapeTitular, YapeQrUrl)
                VALUES
                (@NegocioId, @NombreNegocio, @LogoUrl, @ColorPrimario, @ColorSecundario, @ColorAcento,
                 @Direccion, @Telefono, @Ruc, @HorarioAtencion, @Igv, @MetaMensual, @SolesPorPunto,
                 @AnchoTicketMm, @MensajePieTicket, @CondicionesServicio, @NotasProduccion, @CostoDelivery,
                 @ValorPuntoCanje, @MaxDescuentoPct, @YapeNumero, @YapeTitular, @YapeQrUrl);";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@NombreNegocio", c.NombreNegocio);
        cmd.AddParam("@LogoUrl", c.LogoUrl);
        cmd.AddParam("@ColorPrimario", c.ColorPrimario);
        cmd.AddParam("@ColorSecundario", c.ColorSecundario);
        cmd.AddParam("@ColorAcento", c.ColorAcento);
        cmd.AddParam("@Direccion", c.Direccion);
        cmd.AddParam("@Telefono", c.Telefono);
        cmd.AddParam("@Ruc", c.Ruc);
        cmd.AddParam("@HorarioAtencion", c.HorarioAtencion);
        cmd.AddParam("@Igv", c.Igv);
        cmd.AddParam("@MetaMensual", c.MetaMensual);
        cmd.AddParam("@SolesPorPunto", c.SolesPorPunto);
        cmd.AddParam("@AnchoTicketMm", c.AnchoTicketMm);
        cmd.AddParam("@MensajePieTicket", c.MensajePieTicket);
        cmd.AddParam("@CondicionesServicio", c.CondicionesServicio);
        cmd.AddParam("@NotasProduccion", c.NotasProduccion);
        cmd.AddParam("@CostoDelivery", c.CostoDelivery);
        cmd.AddParam("@ValorPuntoCanje", c.ValorPuntoCanje);
        cmd.AddParam("@MaxDescuentoPct", c.MaxDescuentoPct);
        cmd.AddParam("@YapeNumero", c.YapeNumero);
        cmd.AddParam("@YapeTitular", c.YapeTitular);
        cmd.AddParam("@YapeQrUrl", c.YapeQrUrl);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
