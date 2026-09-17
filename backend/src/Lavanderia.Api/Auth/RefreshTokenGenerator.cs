using System.Security.Cryptography;

namespace Lavanderia.Api.Auth;

/// <summary>
/// Genera el valor real del refresh token (que solo ve el cliente) y su hash (lo unico que se
/// guarda en la BD). Asi, si alguien copia la base de datos, no puede autenticarse con esos
/// hashes — necesitaria el token original, que nunca se persiste.
/// </summary>
public static class RefreshTokenGenerator
{
    public static string GenerarToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
