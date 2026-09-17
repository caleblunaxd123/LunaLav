using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Policy = "Modulo:CLIENTES")]
public class ClientesController : TenantAwareControllerBase
{
    private readonly IClienteRepository _repo;

    public ClientesController(IClienteRepository repo) => _repo = repo;

    [HttpGet("analitica")]
    public async Task<ActionResult<List<ClienteAnaliticaDto>>> Analitica(CancellationToken ct)
        => Ok(await _repo.ListarAnaliticaAsync(NegocioId, ct));

    [HttpGet("cumpleanos-proximos")]
    public async Task<ActionResult<List<ClienteCumpleanosDto>>> CumpleanosProximos([FromQuery] int dias = 30, CancellationToken ct = default)
        => Ok(await _repo.ListarCumpleanosProximosAsync(NegocioId, Math.Clamp(dias, 1, 90), ct));

    /// <summary>Barras de tendencia de la ventana de Clientes: clientes nuevos por mes.</summary>
    [HttpGet("tendencia")]
    public async Task<ActionResult<List<TendenciaPuntoDto>>> Tendencia([FromQuery] int meses = 6, CancellationToken ct = default)
    {
        meses = Math.Clamp(meses, 3, 12);
        var desde = Lavanderia.Api.Infrastructure.TendenciaBuilder.DesdeMeses(meses);
        var datos = await _repo.ContarNuevosPorMesAsync(desde, NegocioId, ct);
        return Ok(Lavanderia.Api.Infrastructure.TendenciaBuilder.SerieMensual(datos, meses));
    }

    [HttpGet]
    public async Task<ActionResult<List<ClienteDto>>> Buscar(
        [FromQuery] string? texto,
        [FromQuery] string? campo,
        [FromQuery] int limite = 50,
        CancellationToken ct = default)
    {
        var list = await _repo.BuscarAsync(texto, campo, Math.Clamp(limite, 1, 500), NegocioId, ct);
        return Ok(list.Select(Map).ToList());
    }

    [HttpPost("fusionar")]
    public async Task<IActionResult> Fusionar([FromBody] FusionarClientesRequest req, CancellationToken ct)
    {
        var origen = await _repo.ObtenerPorIdAsync(req.OrigenId, NegocioId, ct);
        var destino = await _repo.ObtenerPorIdAsync(req.DestinoId, NegocioId, ct);
        if (origen is null || destino is null) return NotFound();

        try
        {
            await _repo.FusionarAsync(req.OrigenId, req.DestinoId, NegocioId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }

        return Ok(new { mensaje = $"Se fusionó \"{origen.Nombre}\" dentro de \"{destino.Nombre}\"." });
    }

    [HttpGet("frecuentes")]
    public async Task<ActionResult<List<ClienteFrecuenteDto>>> Frecuentes(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] int limite = 25,
        CancellationToken ct = default)
    {
        var h = (hasta ?? DateTime.Today).Date.AddDays(1);
        var d = (desde ?? h.AddDays(-31)).Date;
        return Ok(await _repo.ListarFrecuentesAsync(d, h, Math.Clamp(limite, 1, 200), NegocioId, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClienteDto>> Obtener(int id, CancellationToken ct)
    {
        var c = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (c is null) return NotFound();
        return Ok(Map(c));
    }

    [HttpPost]
    public async Task<ActionResult<ClienteDto>> Crear([FromBody] ClienteDto dto, CancellationToken ct)
    {
        var nombre = dto.Nombre?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre))
            return BadRequest(new { mensaje = "El nombre del cliente es obligatorio." });

        var celular = LimpiarTexto(dto.Celular);
        var dni = LimpiarTexto(dto.Dni);
        var documentoFiscal = LimpiarTexto(dto.DocumentoFiscal);
        var direccion = LimpiarTexto(dto.Direccion);
        if (documentoFiscal is not null && !DocumentoFiscalValidator.EsRucValido(documentoFiscal))
            return BadRequest(new { mensaje = "El RUC ingresado no es valido ante SUNAT." });

        var duplicado = await _repo.BuscarDuplicadoAsync(nombre, celular, dni, documentoFiscal, NegocioId, null, ct);
        if (duplicado is not null)
            return Conflict(new { mensaje = MensajeDuplicado(duplicado, celular, dni, documentoFiscal), clienteId = duplicado.Id });

        var id = await _repo.CrearAsync(new Cliente
        {
            NegocioId = NegocioId,
            Nombre = nombre,
            Celular = celular,
            Dni = dni,
            DocumentoFiscal = documentoFiscal,
            Direccion = direccion,
            Puntos = dto.Puntos,
            FechaNacimiento = dto.FechaNacimiento
        }, ct);

        var creado = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Obtener), new { id }, Map(creado!));
    }

    /// <summary>
    /// Carga masiva de clientes. Valida fila por fila: omite duplicados (por celular/DNI, dentro del
    /// archivo y contra la base) y filas inválidas sin abortar el resto. Devuelve un resumen.
    /// </summary>
    [HttpPost("importar")]
    public async Task<ActionResult<ImportarClientesResultado>> Importar([FromBody] ImportarClientesRequest req, CancellationToken ct)
    {
        if (req.Filas is null || req.Filas.Count == 0)
            return BadRequest(new { mensaje = "No se recibió ninguna fila para importar." });
        if (req.Filas.Count > 1000)
            return BadRequest(new { mensaje = "Máximo 1000 clientes por importación. Divide el archivo en partes." });

        var resultado = new ImportarClientesResultado();
        var dnisLote = new HashSet<string>();
        // Clave "nombre|celular" normalizada: un mismo celular con nombres distintos (familias)
        // NO es duplicado, así que se deduplica por la combinación, no por el celular solo.
        var nombreCelularLote = new HashSet<string>();

        var fila = 0;
        foreach (var f in req.Filas)
        {
            fila++;
            var nombre = (f.Nombre ?? "").Trim();
            var celular = LimpiarTexto(f.Celular);
            var dni = LimpiarTexto(f.Dni);
            var direccion = LimpiarTexto(f.Direccion);

            if (nombre.Length == 0 && celular is null && dni is null) continue; // fila vacía

            if (nombre.Length < 2 || nombre.Length > 120)
            { resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Nombre inválido (2 a 120 caracteres)." }); continue; }
            if (celular is not null && !System.Text.RegularExpressions.Regex.IsMatch(celular, @"^\+?\d{4,20}$"))
            { resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Celular inválido (solo números; para el extranjero, + y código de país)." }); continue; }
            if (dni is not null && !System.Text.RegularExpressions.Regex.IsMatch(dni, @"^\d{8}$"))
            { resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "DNI inválido (8 dígitos)." }); continue; }

            if (dni is not null && !dnisLote.Add(dni))
            { resultado.Omitidos++; resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "DNI repetido dentro del archivo." }); continue; }
            if (celular is not null && !nombreCelularLote.Add($"{nombre.ToUpperInvariant()}|{celular}"))
            { resultado.Omitidos++; resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Cliente repetido dentro del archivo (mismo nombre y celular)." }); continue; }

            if ((celular is not null || dni is not null) &&
                await _repo.BuscarDuplicadoAsync(nombre, celular, dni, null, NegocioId, null, ct) is not null)
            { resultado.Omitidos++; resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Ya existe este cliente (mismo DNI, o mismo nombre y celular)." }); continue; }

            await _repo.CrearAsync(new Cliente
            {
                NegocioId = NegocioId,
                Nombre = nombre,
                Celular = celular,
                Dni = dni,
                Direccion = direccion
            }, ct);
            resultado.Creados++;
        }

        return Ok(resultado);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ClienteDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        var nombre = dto.Nombre?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre))
            return BadRequest(new { mensaje = "El nombre del cliente es obligatorio." });

        var celular = LimpiarTexto(dto.Celular);
        var dni = LimpiarTexto(dto.Dni);
        var documentoFiscal = LimpiarTexto(dto.DocumentoFiscal);
        if (documentoFiscal is not null && !DocumentoFiscalValidator.EsRucValido(documentoFiscal))
            return BadRequest(new { mensaje = "El RUC ingresado no es valido ante SUNAT." });
        var duplicado = await _repo.BuscarDuplicadoAsync(nombre, celular, dni, documentoFiscal, NegocioId, id, ct);
        if (duplicado is not null)
            return Conflict(new { mensaje = MensajeDuplicado(duplicado, celular, dni, documentoFiscal), clienteId = duplicado.Id });

        existente.Nombre = nombre;
        existente.Celular = celular;
        existente.Dni = dni;
        existente.DocumentoFiscal = documentoFiscal;
        existente.Direccion = LimpiarTexto(dto.Direccion);
        existente.Puntos = dto.Puntos;
        existente.FechaNacimiento = dto.FechaNacimiento;

        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        var pedidos = await _repo.ContarPedidosAsync(id, NegocioId, ct);
        await _repo.DesactivarAsync(id, NegocioId, ct);
        return Ok(new
        {
            mensaje = pedidos > 0
                ? $"Cliente desactivado. Se conservan sus {pedidos} pedidos históricos."
                : "Cliente eliminado.",
            desactivado = true
        });
    }

    [HttpGet("{id:int}/puntos")]
    public async Task<ActionResult<List<MovimientoPuntosDto>>> ListarPuntos(int id, CancellationToken ct)
    {
        var list = await _repo.ListarMovimientosPuntosAsync(id, NegocioId, ct);
        return Ok(list.Select(m => new MovimientoPuntosDto
        {
            Id = m.Id,
            ClienteId = m.ClienteId,
            Fecha = m.Fecha,
            Motivo = m.Motivo,
            Puntos = m.Puntos,
            Tipo = m.Tipo,
            UsuarioNombre = m.UsuarioNombre
        }).ToList());
    }

    [HttpPost("{id:int}/puntos")]
    public async Task<IActionResult> AgregarPuntos(int id, [FromBody] CrearMovimientoPuntosRequest req, CancellationToken ct)
    {
        var cliente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (cliente is null) return NotFound();

        var tipo = req.Tipo.ToUpperInvariant();
        if (tipo != "SUMA" && tipo != "RESTA")
            return BadRequest(new { mensaje = "Tipo inválido. Debe ser SUMA o RESTA." });

        await _repo.AgregarMovimientoPuntosAsync(new MovimientoPuntos
        {
            NegocioId = NegocioId,
            ClienteId = id,
            Motivo = req.Motivo.Trim(),
            Puntos = req.Puntos,
            Tipo = tipo,
            UsuarioId = UsuarioId
        }, NegocioId, ct);

        return NoContent();
    }

    private static ClienteDto Map(Cliente c) => new()
    {
        Id = c.Id,
        Nombre = c.Nombre,
        Celular = c.Celular,
        Dni = c.Dni,
        DocumentoFiscal = c.DocumentoFiscal,
        Direccion = c.Direccion,
        Puntos = c.Puntos,
        FechaCreacion = c.FechaCreacion,
        FechaNacimiento = c.FechaNacimiento
    };

    private static string? LimpiarTexto(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string MensajeDuplicado(Cliente duplicado, string? celular, string? dni, string? documentoFiscal)
    {
        if (!string.IsNullOrWhiteSpace(dni) && string.Equals(duplicado.Dni, dni, StringComparison.Ordinal))
            return "Ya existe un cliente con ese DNI.";
        if (!string.IsNullOrWhiteSpace(documentoFiscal) && string.Equals(duplicado.DocumentoFiscal, documentoFiscal, StringComparison.Ordinal))
            return "Ya existe un cliente con ese documento fiscal.";
        if (!string.IsNullOrWhiteSpace(celular) && string.Equals(duplicado.Celular, celular, StringComparison.Ordinal))
            return "Ya existe un cliente con ese nombre y celular. Si es otra persona, usa un dato distinto.";
        return "Ya existe un cliente con esos datos.";
    }
}
