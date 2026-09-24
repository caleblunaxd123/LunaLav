using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Auth;

public static class NegocioAccessRules
{
    private static readonly HashSet<string> EstadosBloqueados = new(StringComparer.OrdinalIgnoreCase)
    {
        "VENCIDA",
        "SUSPENDIDA"
    };

    public static bool PuedeOperar(Negocio? negocio)
        => negocio is not null
           && negocio.Activo
           && !EstadosBloqueados.Contains(negocio.EstadoSuscripcion)
           && !(negocio.EstadoSuscripcion.Equals("PRUEBA", StringComparison.OrdinalIgnoreCase)
                && negocio.ProximoPago is DateOnly finPrueba
                && finPrueba < DateOnly.FromDateTime(DateTime.UtcNow));

    public static string MensajeBloqueo(Negocio? negocio, string? celularContacto = null)
    {
        var contacto = string.IsNullOrWhiteSpace(celularContacto)
            ? string.Empty
            : $" Comunicate al {celularContacto}.";

        if (negocio is null || !negocio.Activo)
            return $"La empresa se encuentra suspendida.{contacto}";

        return negocio.EstadoSuscripcion.ToUpperInvariant() switch
        {
            "PRUEBA" when negocio.ProximoPago is DateOnly finPrueba
                && finPrueba < DateOnly.FromDateTime(DateTime.UtcNow)
                => $"La prueba gratuita de la empresa ha finalizado.{contacto}",
            "VENCIDA" => $"La suscripcion de la empresa esta vencida.{contacto}",
            "SUSPENDIDA" => $"La suscripcion de la empresa se encuentra suspendida.{contacto}",
            _ => $"La empresa no esta habilitada para operar.{contacto}"
        };
    }
}
