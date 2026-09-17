using Lavanderia.Api.Repositories;

namespace Lavanderia.Api.Services;

/// <summary>Cálculos compartidos del seguimiento en vivo del reparto: distancia del repartidor
/// al destino, estado de la ruta (para el mensaje y el mapa del cliente) y ETA aproximado.</summary>
public static class SeguimientoRutaCalculo
{
    // Umbrales en metros. Con GPS de celular el error típico ronda 10-30 m, así que "llegó" se
    // marca generoso para que el aviso salga aunque el pin no caiga exacto sobre la puerta.
    public const double MetrosLlego = 90;
    public const double MetrosCerca = 500;

    // Velocidad urbana promedio de una moto en reparto (~18 km/h = 300 m/min).
    private const double MetrosPorMinuto = 300;

    // Se considera GPS "vigente" si se reportó hace poco; si el repartidor cerró la pantalla,
    // dejamos de afirmar que va en movimiento.
    private static readonly TimeSpan VigenciaGps = TimeSpan.FromMinutes(3);

    /// <summary>Distancia en metros entre dos coordenadas (fórmula de Haversine).</summary>
    public static double DistanciaMetros(double lat1, double lon1, double lat2, double lon2)
    {
        const double radioTierra = 6_371_000; // metros
        double dLat = GradosARad(lat2 - lat1);
        double dLon = GradosARad(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(GradosARad(lat1)) * Math.Cos(GradosARad(lat2))
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radioTierra * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double GradosARad(double g) => g * Math.PI / 180.0;

    /// <summary>Distancia del repartidor al destino, en metros, si hay GPS vigente y destino con
    /// coordenadas. Devuelve null cuando no se puede calcular.</summary>
    public static int? DistanciaAlDestino(RutaReparto r)
    {
        if (r.MotorizadoLat is null || r.MotorizadoLng is null) return null;
        if (r.LatitudEntrega is null || r.LongitudEntrega is null) return null;
        if (r.MotorizadoUbicadoEn is null || DateTime.Now - r.MotorizadoUbicadoEn.Value > VigenciaGps) return null;

        var metros = DistanciaMetros(
            (double)r.MotorizadoLat.Value, (double)r.MotorizadoLng.Value,
            (double)r.LatitudEntrega.Value, (double)r.LongitudEntrega.Value);
        return (int)Math.Round(metros);
    }

    /// <summary>SIN_RUTA · EN_RUTA · CERCA · LLEGO · ENTREGADO.</summary>
    public static string DeterminarEstado(RutaReparto r, int? distanciaMetros)
    {
        if (string.Equals(r.EstadoProceso, "ENTREGADO", StringComparison.OrdinalIgnoreCase))
            return "ENTREGADO";
        if (r.RutaIniciadaEn is null) return "SIN_RUTA";
        if (distanciaMetros is null) return "EN_RUTA";
        if (distanciaMetros <= MetrosLlego) return "LLEGO";
        if (distanciaMetros <= MetrosCerca) return "CERCA";
        return "EN_RUTA";
    }

    public static int? EtaMinutos(int? distanciaMetros)
    {
        if (distanciaMetros is null) return null;
        return Math.Max(1, (int)Math.Ceiling(distanciaMetros.Value / MetrosPorMinuto));
    }
}
