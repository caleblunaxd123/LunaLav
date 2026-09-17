using Lavanderia.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Lavanderia.Api.Auth;

public interface ITokenService
{
    (string token, DateTime expira) GenerarAccessToken(Usuario usuario, IEnumerable<string>? modulos = null);
}

public class TokenService : ITokenService
{
    private readonly JwtOptions _opts;

    public TokenService(IOptions<JwtOptions> opts) => _opts = opts.Value;

    public (string token, DateTime expira) GenerarAccessToken(Usuario u, IEnumerable<string>? modulos = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, u.Id.ToString()),
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.UsuarioLogin),
            new("nombre", u.NombreCompleto),
            new(ClaimTypes.Role, u.RolCodigo),
            new("negocioId", u.NegocioId.ToString()),
            new("sedeId", u.SedeId?.ToString() ?? "")
        };

        foreach (var modulo in (modulos ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim("mod", modulo));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expira = DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);

        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            expires: expira,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
