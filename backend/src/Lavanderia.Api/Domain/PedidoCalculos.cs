using System.Text.RegularExpressions;

namespace Lavanderia.Api.Domain;

/// <summary>Próximo paso del flujo de un pedido (área destino + estado + nota sugerida).</summary>
public sealed record SiguientePaso(int? AreaId, string Estado, string Nota);

/// <summary>
/// Lógica pura de negocio del pedido: redondeo del dinero, estado de pago, puntos ganados,
/// máquina de estados del flujo y validaciones de contacto/entrega. Sin dependencias de BD
/// para poder probarla en aislamiento (ver Lavanderia.Api.Tests). Es la única fuente de verdad
/// de estas reglas: tanto PedidoService como los tests las consumen desde aquí.
/// </summary>
public static class PedidoCalculos
{
    /// <summary>Redondeo comercial a 10 céntimos (mismo criterio al crear y al agregar ítems).</summary>
    public static decimal RedondearA10Centimos(decimal monto)
        => Math.Round(monto * 10m, MidpointRounding.AwayFromZero) / 10m;

    /// <summary>Estado de pago derivado del monto pagado contra el total.</summary>
    public static string DeterminarEstadoPago(decimal montoPagado, decimal total)
        => montoPagado <= 0 ? "PENDIENTE" : montoPagado >= total ? "PAGADO" : "PARCIAL";

    /// <summary>Puntos de fidelización que otorga una compra (0 si el negocio no lo usa).</summary>
    public static int PuntosGanados(decimal total, decimal solesPorPunto)
        => solesPorPunto > 0 ? (int)Math.Floor(total / solesPorPunto) : 0;

    private static bool EsEntregaDomicilio(string? modalidad)
        => string.Equals(modalidad, "Delivery", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Calcula el siguiente paso válido del flujo. Lanza <see cref="InvalidOperationException"/>
    /// si el pedido está en un estado desde el que no se puede avanzar o su área es inconsistente.
    /// </summary>
    public static SiguientePaso CalcularSiguientePaso(Pedido pedido, IReadOnlyList<AreaLavado> areasActivasOrdenadas)
    {
        if (pedido.EstadoProceso is "LISTO" or "ENTREGA_PARCIAL")
            return new SiguientePaso(pedido.AreaActualId, "ENTREGADO", "Entregado al cliente");

        if (pedido.EstadoProceso is not ("PENDIENTE" or "EN_PROCESO"))
            throw new InvalidOperationException($"El estado '{pedido.EstadoProceso}' no permite avanzar el pedido.");

        if (areasActivasOrdenadas.Count == 0)
            throw new InvalidOperationException("No hay áreas de lavado configuradas para esta sede.");

        if (pedido.EstadoProceso == "EN_PROCESO" && pedido.AreaActualId is null)
            throw new InvalidOperationException(
                "El pedido está EN PROCESO pero no tiene un área actual. Corrige la incidencia antes de continuar; no se reinició el flujo.");

        if (pedido.AreaActualId is null)
            return new SiguientePaso(areasActivasOrdenadas[0].Id, "EN_PROCESO", $"Ingresa a: {areasActivasOrdenadas[0].Nombre}");

        var indiceActual = -1;
        for (var i = 0; i < areasActivasOrdenadas.Count; i++)
            if (areasActivasOrdenadas[i].Id == pedido.AreaActualId.Value) { indiceActual = i; break; }

        if (indiceActual < 0)
            throw new InvalidOperationException(
                "El área actual del pedido no está activa o no pertenece a esta sede. Corrige la configuración antes de avanzar.");

        if (indiceActual == areasActivasOrdenadas.Count - 1)
        {
            var nota = EsEntregaDomicilio(pedido.Modalidad) ? "Listo para salir a ruta" : "Listo para recojo";
            return new SiguientePaso(pedido.AreaActualId, "LISTO", nota);
        }

        var proximaArea = areasActivasOrdenadas[indiceActual + 1];
        return new SiguientePaso(proximaArea.Id, "EN_PROCESO", $"Avanza a: {proximaArea.Nombre}");
    }

    /// <summary>
    /// Valida los datos de contacto del cliente para crear un pedido. El celular es opcional
    /// (solo se valida su formato si viene); la dirección solo si es Recojo a domicilio.
    /// </summary>
    public static void ValidarContacto(string? celular, string? direccion, string modalidad)
    {
        // El celular es opcional: algunos clientes no quieren darlo. Solo se valida el
        // formato cuando viene informado.
        if (!string.IsNullOrWhiteSpace(celular) && !Regex.IsMatch(celular.Trim(), @"^\+?\d{4,20}$"))
            throw new InvalidOperationException("El celular no es válido. Ingresa solo números (para el extranjero, empieza con + y el código de país).");
        if (modalidad == "Recojo" && string.IsNullOrWhiteSpace(direccion))
            throw new InvalidOperationException("Para pedidos a domicilio debes registrar la dirección del cliente.");
    }

    /// <summary>Valida el destino de un Delivery (dirección + distrito + coordenadas válidas).</summary>
    public static void ValidarDestinoDelivery(
        string modalidad, string? direccion, string? distrito, decimal? latitud, decimal? longitud)
    {
        if (modalidad != "Delivery") return;
        if (string.IsNullOrWhiteSpace(direccion))
            throw new InvalidOperationException("Indica la dirección exacta de entrega para el Delivery.");
        if (string.IsNullOrWhiteSpace(distrito))
            throw new InvalidOperationException("Selecciona el distrito de entrega para el Delivery.");
        if (!latitud.HasValue || !longitud.HasValue)
            throw new InvalidOperationException("Confirma el punto exacto de entrega en el mapa.");
        if (latitud is < -90 or > 90 || longitud is < -180 or > 180)
            throw new InvalidOperationException("Las coordenadas del punto de entrega no son válidas.");
    }
}
