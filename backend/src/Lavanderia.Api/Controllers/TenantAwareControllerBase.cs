using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Base para controllers que necesitan el negocio/sede del usuario autenticado.
/// Los repositorios de datos operacionales (pedidos, caja, inventario, personal) y de
/// catalogo (clientes, servicios, etc.) usaran estos valores para filtrar sus consultas
/// a medida que se vayan migrando (ver plan de fases B y C).
/// </summary>
[ApiController]
[Authorize]
public abstract class TenantAwareControllerBase : ControllerBase
{
    protected int UsuarioId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    protected int NegocioId => int.Parse(User.FindFirstValue("negocioId") ?? "0");

    protected int? SedeId
    {
        get
        {
            var v = User.FindFirstValue("sedeId");
            return string.IsNullOrEmpty(v) ? null : int.Parse(v);
        }
    }

    protected int SedeRequeridaId => SedeId
        ?? throw new InvalidOperationException("Selecciona una sede antes de continuar.");
}
