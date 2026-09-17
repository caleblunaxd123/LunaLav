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
        => negocio is not null && negocio.Activo && !EstadosBloqueados.Contains(negocio.EstadoSuscripcion);

    public static string MensajeBloqueo(Negocio? negocio, string? celularContacto = null)
    {
        var contacto = string.IsNullOrWhiteSpace(celularContacto)
            ? string.Empty
            : $" Comunicate al {celularContacto}.";

        if (negocio is null || !negocio.Activo)
            return $"La empresa se encuentra suspendida.{contacto}";

        return negocio.EstadoSuscripcion.ToUpperInvariant() switch
        {
            "VENCIDA" => $"La suscripcion de la empresa esta vencida.{contacto}",
            "SUSPENDIDA" => $"La suscripcion de la empresa se encuentra suspendida.{contacto}",
            _ => $"La empresa no esta habilitada para operar.{contacto}"
        };
    }
}
