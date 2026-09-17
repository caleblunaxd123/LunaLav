using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IPlantillaWhatsappRepository
{
    Task<List<PlantillaWhatsapp>> ListarTodasAsync(int negocioId, CancellationToken ct = default);
    Task<List<PlantillaWhatsapp>> ListarActivasAsync(int negocioId, CancellationToken ct = default);
    Task<PlantillaWhatsapp?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task ActualizarAsync(PlantillaWhatsapp p, int negocioId, CancellationToken ct = default);
    Task<int> CrearAsync(PlantillaWhatsapp p, CancellationToken ct = default);
}

public class PlantillaWhatsappRepository : IPlantillaWhatsappRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PlantillaWhatsappRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static PlantillaWhatsapp Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Evento = r.GetString(r.GetOrdinal("Evento")),
        Mensaje = r.GetString(r.GetOrdinal("Mensaje")),
        Activa = r.GetBoolean(r.GetOrdinal("Activa"))
    };

    private const string Select = "SELECT Id, Evento, Mensaje, Activa FROM dbo.PlantillaWhatsapp";

    private static readonly (string Evento, string Mensaje)[] Defaults =
    [
        ("INGRESO", "¡Hola *{cliente}*!\nLe saluda la lavandería *{negocio}*. Su orden es la *{numero}* con los siguientes ítems:\n\n{items}\n\nMonto total a pagar *S/{total}*, del cual falta pagar *S/{saldo}*.\nFecha de entrega: *{entrega}*.\n\nNuestro horario de atención es:\n{horario}\n\n{seguimiento}\n\n*CONDICIONES DEL SERVICIO - {negocio}*\n{condiciones}"),
        ("CAMBIO_AREA", "Hola {cliente}, tu pedido #{numero} ya esta en la etapa: {area}. Tiempo estimado restante: {tiempoRestante}."),
        ("LISTO", "Hola {cliente}! Tu pedido #{numero} esta LISTO para recoger en {negocio}. Te esperamos!"),
        ("EN_RUTA", "Hola {cliente}! Tu pedido #{numero} de {negocio} ya va en camino a tu direccion. Sigue al repartidor en tiempo real aqui:\n{seguimiento}"),
        ("DEMORA", "Hola {cliente}, tu pedido #{numero} tendra una demora. Nueva hora estimada: {entrega}. Disculpa las molestias."),
        ("ENTREGADO", "Gracias por tu preferencia, {cliente}! Pedido #{numero} entregado. Total: S/ {total}.")
    ];

    /// <summary>Siembra los mensajes por defecto que le falten a este negocio. Es idempotente por
    /// evento: un negocio nuevo recibe todos, y uno existente recibe solo los eventos nuevos
    /// (p.ej. EN_RUTA) sin tocar los que el dueño ya haya personalizado.</summary>
    private async Task SembrarDefaultsSiFaltanAsync(Microsoft.Data.SqlClient.SqlConnection conn, int negocioId, CancellationToken ct)
    {
        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT Evento FROM dbo.PlantillaWhatsapp WHERE NegocioId = @NegocioId";
        check.AddParam("@NegocioId", negocioId);
        var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await check.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) existentes.Add(r.GetString(0));

        foreach (var (evento, mensaje) in Defaults)
        {
            if (existentes.Contains(evento)) continue;
            await using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO dbo.PlantillaWhatsapp (NegocioId, Evento, Mensaje, Activa) VALUES (@NegocioId, @Evento, @Mensaje, 1)";
            insert.AddParam("@NegocioId", negocioId);
            insert.AddParam("@Evento", evento);
            insert.AddParam("@Mensaje", mensaje);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<List<PlantillaWhatsapp>> ListarTodasAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await SembrarDefaultsSiFaltanAsync(conn, negocioId, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE NegocioId = @NegocioId ORDER BY Id";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<List<PlantillaWhatsapp>> ListarActivasAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await SembrarDefaultsSiFaltanAsync(conn, negocioId, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE Activa = 1 AND NegocioId = @NegocioId";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<PlantillaWhatsapp?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task ActualizarAsync(PlantillaWhatsapp p, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.PlantillaWhatsapp SET Mensaje = @Mensaje, Activa = @Activa WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", p.Id);
        cmd.AddParam("@Mensaje", p.Mensaje);
        cmd.AddParam("@Activa", p.Activa);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CrearAsync(PlantillaWhatsapp p, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.PlantillaWhatsapp (NegocioId, Evento, Mensaje, Activa)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Evento, @Mensaje, @Activa)";
        cmd.AddParam("@NegocioId", p.NegocioId);
        cmd.AddParam("@Evento", p.Evento);
        cmd.AddParam("@Mensaje", p.Mensaje);
        cmd.AddParam("@Activa", p.Activa);
        return await cmd.ReadScalarAsync<int>(ct);
    }
}
