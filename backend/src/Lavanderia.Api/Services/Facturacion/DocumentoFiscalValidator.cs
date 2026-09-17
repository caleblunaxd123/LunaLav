namespace Lavanderia.Api.Services.Facturacion;

public static class DocumentoFiscalValidator
{
    private static readonly int[] PesosRuc = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    public static bool EsRucValido(string? ruc)
    {
        if (ruc is null || ruc.Length != 11 || ruc.Any(c => !char.IsDigit(c))) return false;
        if (ruc[..2] is not ("10" or "15" or "17" or "20")) return false;

        var suma = 0;
        for (var i = 0; i < PesosRuc.Length; i++) suma += (ruc[i] - '0') * PesosRuc[i];
        var digito = 11 - suma % 11;
        if (digito == 10) digito = 0;
        else if (digito == 11) digito = 1;
        return digito == ruc[10] - '0';
    }
}
