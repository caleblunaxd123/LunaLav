using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Lavanderia.Api.Auth;

/// <summary>Emite sesiones exclusivamente para la aplicación interna de Marketing.</summary>
public interface IMarketingTokenService
{
    (string token, DateTime expira) Generar(int userId, string login, string nombre, string rol);
}

public sealed class MarketingTokenService : IMarketingTokenService
{
    private readonly JwtOptions _options;
    public MarketingTokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options) => _options = options.Value;

    public (string token, DateTime expira) Generar(int userId, string login, string nombre, string rol)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, login), new Claim("nombre", nombre), new Claim(ClaimTypes.Role, rol),
            new Claim("app", "marketing"), new Claim("marketingUserId", userId.ToString())
        };
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, expires: expires, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
