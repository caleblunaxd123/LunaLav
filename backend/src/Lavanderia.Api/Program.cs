using Lavanderia.Api.Auth;
using Lavanderia.Api.Infrastructure;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;
using Lavanderia.Api.Services.Facturacion;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Secretos locales fuera de git (ej. credenciales de APISUNAT). Opcional; se carga en cualquier
// entorno y pisa a appsettings.json. NUNCA se commitea (ver .gitignore: appsettings.Local.json).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
    options.AddServerHeader = false;
});

// ------------------------------------------------------------
// Configuracion
// ------------------------------------------------------------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configuracion Jwt faltante.");

// Falla rapido en Produccion si alguien olvido cambiar los valores de ejemplo de
// appsettings.json: un JWT firmado con esta clave conocida, o una cuenta admin/propietario
// con la clave por defecto, le darian control total del sistema (o de todo el SaaS) a
// cualquiera que haya leido este repo publico.
if (builder.Environment.IsProduction())
{
    const string ClavePlaceholder = "CAMBIAR_ESTA_CLAVE_EN_PROD_DEBE_TENER_AL_MENOS_32_CHARS";
    if (jwt.SecretKey == ClavePlaceholder || jwt.SecretKey.Length < 32)
        throw new InvalidOperationException(
            "Jwt:SecretKey sigue en su valor de ejemplo (o es muy corta). Configura una clave real de al menos 32 caracteres via variable de entorno antes de arrancar en Produccion.");

    var passwordAdmin = builder.Configuration.GetValue<string>("SeedAdmin:Password");
    if (passwordAdmin == "admin123")
        throw new InvalidOperationException(
            "SeedAdmin:Password sigue en su valor de ejemplo ('admin123'). Configura una contrasena real antes de arrancar en Produccion.");

    var passwordPropietario = builder.Configuration.GetValue<string>("SeedPropietario:Password");
    if (passwordPropietario == "propietario123")
        throw new InvalidOperationException(
            "SeedPropietario:Password sigue en su valor de ejemplo ('propietario123'). Este usuario administra TODOS los negocios del SaaS: configura una contrasena real antes de arrancar en Produccion.");
}

// ------------------------------------------------------------
// Servicios
// ------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errores = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(
                x => string.IsNullOrWhiteSpace(x.Key) ? "solicitud" : x.Key,
                x => x.Value!.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                    ? "Valor inválido."
                    : e.ErrorMessage).ToArray());
        return new BadRequestObjectResult(new
        {
            mensaje = "Revisa los datos ingresados y vuelve a intentarlo.",
            errores
        });
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();

// Infra
builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

// Repositorios (transient — sin estado)
builder.Services.AddTransient<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddTransient<IRolRepository, RolRepository>();
builder.Services.AddTransient<IClienteRepository, ClienteRepository>();
builder.Services.AddTransient<IServicioRepository, ServicioRepository>();
builder.Services.AddTransient<IAreaLavadoRepository, AreaLavadoRepository>();
builder.Services.AddTransient<IPedidoRepository, PedidoRepository>();
builder.Services.AddTransient<IConfiguracionNegocioRepository, ConfiguracionNegocioRepository>();
builder.Services.AddTransient<ICajaRepository, CajaRepository>();
builder.Services.AddTransient<IPromocionRepository, PromocionRepository>();
builder.Services.AddTransient<IRolPermisoRepository, RolPermisoRepository>();
builder.Services.AddTransient<ICategoriaRepository, CategoriaRepository>();
builder.Services.AddTransient<IReporteRepository, ReporteRepository>();
builder.Services.AddTransient<IEmpleadoRepository, EmpleadoRepository>();
builder.Services.AddTransient<IPlantillaWhatsappRepository, PlantillaWhatsappRepository>();
builder.Services.AddTransient<IRolPersonalRepository, RolPersonalRepository>();
builder.Services.AddTransient<IInsumoRepository, InsumoRepository>();
builder.Services.AddTransient<INegocioRepository, NegocioRepository>();
builder.Services.AddTransient<ISedeRepository, SedeRepository>();
builder.Services.AddTransient<IPagoSuscripcionRepository, PagoSuscripcionRepository>();
builder.Services.AddTransient<IConfiguracionPlataformaRepository, ConfiguracionPlataformaRepository>();
builder.Services.AddTransient<IFacturacionRepository, FacturacionRepository>();
builder.Services.AddTransient<IPagosRepository, PagosRepository>();
builder.Services.AddTransient<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddTransient<IGerencialRepository, GerencialRepository>();
builder.Services.AddTransient<IMotorizadoRepository, MotorizadoRepository>();
builder.Services.AddTransient<IRutaRepartoRepository, RutaRepartoRepository>();
builder.Services.AddTransient<IPedidoFotoRepository, PedidoFotoRepository>();
builder.Services.AddSingleton<Lavanderia.Api.Services.IAlmacenamientoFotos, Lavanderia.Api.Services.AlmacenamientoFotosLocal>();

// Facturacion electronica (SUNAT directo) + Pagos online: SecretProtector cifra credenciales
// reales (clave SOL, password del certificado .pfx y futuras claves de Izipay) con las llaves de
// Data Protection. Sin persistirlas a disco, un reinicio/redeploy sin volumen fijo genera un
// llavero nuevo y todo lo ya cifrado queda indescifrable (Desproteger lanza excepcion) hasta
// que el negocio reconfigure sus credenciales a mano.
var keysPath = builder.Configuration.GetValue<string>("DataProtection:KeysPath")
    ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Lavanderia")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
builder.Services.AddTransient<SecretProtector>();
  builder.Services.AddHttpClient<SunatSoapClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddHttpClient<GeocodificacionService>();
// Proveedor de facturación electrónica seleccionable por configuración:
//   SUNAT_DIRECTO (default) → firma local + SOAP de SUNAT.
//   APISUNAT               → PSE en la nube (listo para activar cuando lleguen
//                             ApiSunat:PersonaId y ApiSunat:PersonaToken de Mekias).
builder.Services.Configure<Lavanderia.Api.Services.Facturacion.ApiSunatOptions>(builder.Configuration.GetSection("ApiSunat"));
  builder.Services.AddHttpClient<Lavanderia.Api.Services.Facturacion.ApiSunatProvider>(client =>
      client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddTransient<SunatDirectoProvider>();
builder.Services.AddTransient<IFacturacionElectronicaProvider>(sp => sp.GetRequiredService<SunatDirectoProvider>());
builder.Services.AddTransient<IFacturacionElectronicaProvider>(sp => sp.GetRequiredService<Lavanderia.Api.Services.Facturacion.ApiSunatProvider>());
builder.Services.AddTransient<ComprobantePdfGenerator>();
builder.Services.AddScoped<IRespaldoComprobantes, RespaldoComprobantesLocal>();
builder.Services.AddHostedService<FacturacionPendientesWorker>();

// Limites defensivos para los puntos anonimos que pueden disparar trabajo costoso o
// solicitudes hacia terceros. En produccion, el proxy debe preservar la IP remota real.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            mensaje = "Demasiados intentos. Espera un minuto y vuelve a intentarlo."
        }, ct);
    };

    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("public-read", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    options.AddPolicy("public-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    // El celular del repartidor reporta su GPS cada ~12s; damos holgura para varios repartidores
    // detrás de la misma IP (red del local) sin permitir un abuso desmedido.
    options.AddPolicy("repartidor-gps", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

// Contexto de tenant (negocio/sede) leido de los claims del JWT actual
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<INegocioAccessValidator, NegocioAccessValidator>();

// Servicios de dominio
builder.Services.AddTransient<IPedidoService, PedidoService>();

// Auth
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        opts.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (context.Principal?.IsInRole("PROPIETARIO") == true) return;

                var negocioClaim = context.Principal?.FindFirst("negocioId")?.Value;
                if (!int.TryParse(negocioClaim, out var negocioId))
                {
                    context.Fail("El token no contiene un negocio valido.");
                    return;
                }

                var validator = context.HttpContext.RequestServices.GetRequiredService<INegocioAccessValidator>();
                if (!await validator.PuedeOperarAsync(negocioId, context.HttpContext.RequestAborted))
                    context.Fail("La empresa o su suscripcion no estan habilitadas.");
            }
        };
    });
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ModuloAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var modulo in Lavanderia.Api.Domain.Modulos.Todos)
    {
        options.AddPolicy(ModuloPolicies.For(modulo), policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.Requirements.Add(new ModuloRequirement(modulo));
        });
    }
    // El reporte de cuadres es parte de la operación de caja: lo pueden ver quienes manejan
    // la caja (CAJA) o quienes ven reportes (REPORTES). ADMIN siempre pasa.
    options.AddPolicy("Modulo:CAJA_O_REPORTES", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new ModuloRequirement("CAJA", "REPORTES"));
    });
});

// CORS
// En Development se permite cualquier puerto de localhost/127.0.0.1 (el dev server
// de Angular cambia de puerto seguido si 4300 ya esta ocupado). En produccion se
// restringe estrictamente a los origenes configurados en appsettings.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
    {
        if (builder.Environment.IsDevelopment())
        {
            p.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Host is "localhost" or "127.0.0.1"));
        }
        else
        {
            p.WithOrigins(corsOrigins);
        }
        p.AllowAnyHeader().AllowAnyMethod();
    });
});

// Seeder
builder.Services.AddTransient<DbInitializer>();

// ------------------------------------------------------------
// App
// ------------------------------------------------------------
var app = builder.Build();

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
// Cloudflare Tunnel reaches the app from localhost. Never trust forwarded headers from an
// arbitrary direct client: otherwise it could spoof its IP to evade the login rate limiter or
// make an HTTP request appear HTTPS. Other reverse proxies must be listed explicitly.
forwardedHeaders.KnownProxies.Add(IPAddress.Loopback);
forwardedHeaders.KnownProxies.Add(IPAddress.IPv6Loopback);
foreach (var configuredProxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
{
    if (!IPAddress.TryParse(configuredProxy, out var proxyAddress))
        throw new InvalidOperationException($"ReverseProxy:KnownProxies contiene una IP invalida: '{configuredProxy}'.");
    forwardedHeaders.KnownProxies.Add(proxyAddress);
}
app.UseForwardedHeaders(forwardedHeaders);

if (app.Environment.IsProduction()) app.UseHsts();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self), payment=()";
        headers["Content-Security-Policy"] = "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https:; font-src 'self' data:; connect-src 'self' https:; worker-src 'self' blob:";
        return Task.CompletedTask;
    });
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // El cliente cerró la conexión; no es una falla de la aplicación ni hay respuesta útil.
    }
    catch (Exception exception)
    {
        if (context.Response.HasStarted) throw;

        // SqlException 8152 = "String or binary data would be truncated" (columna mas chica que
        // el dato enviado) y 2627/2601 = violacion de constraint UNIQUE: son errores de datos del
        // usuario, no fallas del servidor, asi que no deben salir como 500 generico.
        var (status, mensaje) = exception switch
        {
            InvalidOperationException ex when ex.Message == "Nullable object must have a value." =>
                (StatusCodes.Status400BadRequest, "Selecciona una sede antes de continuar."),
            InvalidOperationException ex => (StatusCodes.Status400BadRequest, ex.Message),
            ArgumentException ex => (StatusCodes.Status400BadRequest, ex.Message),
            UnauthorizedAccessException ex => (StatusCodes.Status403Forbidden, ex.Message),
            SqlException { Number: 8152 } => (StatusCodes.Status400BadRequest, "Uno de los datos ingresados es demasiado largo para ese campo."),
            SqlException { Number: 2627 or 2601 } => (StatusCodes.Status409Conflict, "Ya existe un registro con ese mismo dato."),
            _ => (StatusCodes.Status500InternalServerError, "Ocurrio un error inesperado. Intenta nuevamente.")
        };

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Error no controlado en {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogWarning("Solicitud rechazada en {Method} {Path}: {Message}", context.Request.Method, context.Request.Path, exception?.Message);

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { mensaje });
    }
});

// Sirve el frontend (Angular) compilado desde wwwroot: la API y la web quedan en un
// solo origen, listo para publicar bajo un dominio con HTTPS sin líos de CORS.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", componente = "api" }))
    .AllowAnonymous();

static async Task<IResult> Readiness(ISqlConnectionFactory factory, CancellationToken ct)
{
    try
    {
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await cmd.ExecuteScalarAsync(ct);
        return Results.Ok(new { status = "ok", baseDatos = "disponible" });
    }
    catch
    {
        return Results.Json(new { status = "degraded", baseDatos = "no disponible" }, statusCode: 503);
    }
}

app.MapGet("/health/ready", Readiness).AllowAnonymous();
app.MapGet("/health", Readiness).AllowAnonymous();

// Marca de versión: cambia en cada despliegue (fecha del .dll principal). El frontend
// la consulta cada cierto tiempo; si cambió, muestra "hay una nueva versión, recargar".
var versionApp = "0";
try
{
    var dllPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
    if (!string.IsNullOrEmpty(dllPath) && File.Exists(dllPath))
        versionApp = new DateTimeOffset(File.GetLastWriteTimeUtc(dllPath)).ToUnixTimeSeconds().ToString();
}
catch { /* si falla, queda "0" */ }
app.MapGet("/api/version", () => Results.Json(new { version = versionApp })).AllowAnonymous();

app.MapControllers();

// Un endpoint de API que no existe debe responder 404 JSON, no el index.html: si cae al
// fallback de Angular, el cliente recibe 200 + HTML donde espera datos y el error aparece
// como un fallo de parseo incomprensible (además enmascara despliegues incompletos).
app.MapFallback("/api/{**resto}", (HttpContext ctx) =>
{
    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    return Results.Json(new { mensaje = "El recurso solicitado no existe." }, statusCode: StatusCodes.Status404NotFound);
});

// El resto (rutas del cliente: /{slug}/inicio, /seguimiento/:token, /repartidor/:token…)
// lo resuelve Angular.
app.MapFallbackToFile("index.html");

// Seed inicial (usuario admin) — no bloquea el arranque si SQL no está disponible aún.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DbInitializer>();
        await seeder.EjecutarAsync();
        await seeder.EjecutarPropietarioAsync();
        await seeder.EndurecerCredencialesPredeterminadasAsync(app.Environment.IsProduction());
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex,
            "No se pudo ejecutar el seed inicial. Verifica que SQL Server este corriendo y que se hayan ejecutado los scripts 001_schema.sql y 002_seed.sql.");
    }
}

app.Run();
