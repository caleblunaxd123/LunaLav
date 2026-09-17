using Microsoft.AspNetCore.Authorization;

namespace Lavanderia.Api.Auth;

public static class ModuloPolicies
{
    public const string Prefix = "Modulo:";

    public static string For(string modulo) => Prefix + modulo;
}

public sealed class ModuloRequirement : IAuthorizationRequirement
{
    // Acepta uno o varios módulos: el acceso se concede si el usuario tiene CUALQUIERA de ellos.
    public ModuloRequirement(params string[] modulos) => Modulos = modulos;

    public IReadOnlyList<string> Modulos { get; }
}

public sealed class ModuloAuthorizationHandler : AuthorizationHandler<ModuloRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ModuloRequirement requirement)
    {
        if (context.User.IsInRole("ADMIN"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var modulos = context.User.FindAll("mod").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requirement.Modulos.Any(m => modulos.Contains(m)))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
