using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Infrastructure;

/// <summary>
/// Borrado "inteligente" para catálogos: intenta eliminar de verdad la fila y, si la base la
/// rechaza porque está referenciada por otra tabla (pedidos, movimientos, etc.), avisa para que
/// el controller la desactive en su lugar. Así no hay que enumerar a mano cada relación: la
/// integridad referencial de SQL Server es la fuente de verdad. Las tablas/columnas son
/// constantes internas del backend (no entran datos del usuario), por eso es seguro interpolarlas.
/// </summary>
public static class CatalogoEliminacion
{
    private const int ErrorViolacionFk = 547; // FK/reference constraint

    /// <returns>true si se eliminó; false si está referenciada (el controller debe desactivar).</returns>
    public static async Task<bool> EliminarCatalogoAsync(
        this ISqlConnectionFactory factory, string tabla, string columnaScope, int id, int scope, CancellationToken ct = default)
    {
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM dbo.{tabla} WHERE Id = @Id AND {columnaScope} = @Scope";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Scope", scope);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == ErrorViolacionFk)
        {
            return false;
        }
    }
}
