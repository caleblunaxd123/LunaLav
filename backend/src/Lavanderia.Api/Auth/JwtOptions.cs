namespace Lavanderia.Api.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 480;
    public int RefreshTokenDays { get; set; } = 14;
}
