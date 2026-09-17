namespace Lavanderia.Api.Infrastructure;

/// <summary>
/// Lee el NegocioId/SedeId del usuario autenticado desde los claims del JWT actual.
/// NegocioId siempre esta presente; SedeId es null cuando el usuario (tipicamente un
/// ADMIN) tiene acceso a todas las sedes de su negocio.
/// </summary>
public interface ITenantContext
{
    int NegocioId { get; }
    int? SedeId { get; }
}

public class TenantContext : ITenantContext
{
    public int NegocioId { get; }
    public int? SedeId { get; }

    public TenantContext(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        NegocioId = int.TryParse(user?.FindFirst("negocioId")?.Value, out var n) ? n : 0;
        SedeId = int.TryParse(user?.FindFirst("sedeId")?.Value, out var s) ? s : null;
    }
}
