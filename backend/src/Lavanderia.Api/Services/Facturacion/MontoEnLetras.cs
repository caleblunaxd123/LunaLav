namespace Lavanderia.Api.Services.Facturacion;

/// <summary>Convierte un monto en soles a su representación en letras, tal como exige SUNAT en el cbc:Note del comprobante.</summary>
public static class MontoEnLetras
{
    private static readonly string[] Unidades =
        ["", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE"];
    private static readonly string[] Decenas =
        ["DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISEIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE"];
    private static readonly string[] Decenas2 =
        ["", "", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"];
    private static readonly string[] Veintitantos =
        ["VEINTE", "VEINTIUNO", "VEINTIDOS", "VEINTITRES", "VEINTICUATRO", "VEINTICINCO", "VEINTISEIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"];
    private static readonly string[] Centenas =
        ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"];

    public static string Convertir(decimal monto, string moneda = "SOLES")
    {
        var entero = (long)decimal.Truncate(monto);
        var centimos = (int)Math.Round((monto - entero) * 100, MidpointRounding.AwayFromZero);
        var letras = entero == 0 ? "CERO" : ConvertirEntero(entero);
        return $"{letras} CON {centimos:00}/100 {moneda}";
    }

    private static string ConvertirEntero(long n)
    {
        if (n == 0) return "";
        if (n == 100) return "CIEN";
        if (n < 10) return Unidades[n];
        if (n < 20) return Decenas[n - 10];
        if (n < 100)
        {
            var d = n / 10; var u = n % 10;
            if (d == 2) return Veintitantos[u];
            return u == 0 ? Decenas2[d] : $"{Decenas2[d]} Y {Unidades[u]}";
        }
        if (n < 1000)
        {
            var c = n / 100; var resto = n % 100;
            return resto == 0 ? Centenas[c] : $"{Centenas[c]} {ConvertirEntero(resto)}";
        }
        if (n < 1_000_000)
        {
            var miles = n / 1000; var resto = n % 1000;
            var prefijo = miles == 1 ? "MIL" : $"{ConvertirEntero(miles)} MIL";
            return resto == 0 ? prefijo : $"{prefijo} {ConvertirEntero(resto)}";
        }
        var millones = n / 1_000_000; var restoMillon = n % 1_000_000;
        var prefijoM = millones == 1 ? "UN MILLON" : $"{ConvertirEntero(millones)} MILLONES";
        return restoMillon == 0 ? prefijoM : $"{prefijoM} {ConvertirEntero(restoMillon)}";
    }
}
