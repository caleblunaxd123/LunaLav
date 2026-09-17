using Lavanderia.Api.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace Lavanderia.Api.Auth;

public interface INegocioAccessValidator
{
    Task<bool> PuedeOperarAsync(int negocioId, CancellationToken ct = default);
    void Invalidar(int negocioId);
}

public sealed class NegocioAccessValidator : INegocioAccessValidator
{
    private readonly INegocioRepository _negocios;
    private readonly IMemoryCache _cache;

    public NegocioAccessValidator(INegocioRepository negocios, IMemoryCache cache)
    {
        _negocios = negocios;
        _cache = cache;
    }

    public async Task<bool> PuedeOperarAsync(int negocioId, CancellationToken ct = default)
    {
        var cacheKey = $"negocio-acceso:{negocioId}";
        if (_cache.TryGetValue(cacheKey, out bool permitido)) return permitido;

        permitido = NegocioAccessRules.PuedeOperar(await _negocios.ObtenerPorIdAsync(negocioId, ct));
        _cache.Set(cacheKey, permitido, TimeSpan.FromSeconds(30));
        return permitido;
    }

    public void Invalidar(int negocioId) => _cache.Remove($"negocio-acceso:{negocioId}");
}
