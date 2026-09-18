# LunaLav OS — arquitectura

## Estado actual verificado

LunaLav es un monolito ASP.NET Core 9 con frontend Angular standalone y SQL Server. El backend
usa ADO.NET, repositorios, DTOs, JWT Bearer con refresh tokens rotativos, BCrypt, rate limits,
migraciones SQL ordenadas y un `BackgroundService` para facturación. El frontend ya reutiliza
guards, interceptores, `signals`, componentes de carga/toast/paginación y rutas lazy. No usa EF,
Docker, scheduler persistente ni un broker de mensajería.

La solución debe conservar esta base. Agregar microservicios, Kafka, RabbitMQ o un framework de
jobs antes de existir carga real incrementaría el costo operativo sin resolver un problema actual.

## Decisión: modular monolith

La API seguirá siendo una aplicación desplegable, con módulos internos explícitos y schemas SQL:

| Módulo | Schema | Responsabilidad |
| --- | --- | --- |
| Core | `core` | identidad corporativa, roles, auditoría, notificaciones, archivos y eventos |
| Marketing | `marketing` | prospectos, scoring, campañas y cola diaria |
| Sales | `sales` | oportunidades, pipeline, demos y propuestas |
| Communication | `communication` | buzones, threads, mensajes, plantillas y consentimiento |
| Automation | `automation` | definiciones, triggers y ejecuciones de workflow |
| Agents | `agents` | definiciones, permisos, ejecuciones, presupuestos y aprobaciones |

Los tenants de lavanderías existentes permanecen en sus tablas actuales. Las identidades internas
de LunaLav no son usuarios de un tenant y nunca reciben acceso implícito a datos de un cliente.

## Seguridad y límites

- Roles internos y sesiones separadas por aplicación/claim JWT.
- Secretos, OAuth refresh tokens y claves de proveedor se cifran en servidor; nunca llegan a
  Angular ni se incluyen en logs.
- Email recibido, páginas externas y datos de prospectos son datos no confiables: no pueden
  alterar prompts, políticas, permisos ni herramientas.
- Toda acción de agente se ejecuta mediante una herramienta registrada y validada, no SQL libre.
- Comunicaciones automáticas respetan consentimiento, supresión, límites, cooldown e idempotencia.
- La aprobación humana es obligatoria para primer contacto, campañas, precios, finanzas,
  eliminación y cualquier acción marcada de riesgo alto.

## Eventos, jobs y automatización

Fase inicial: `core.DomainEvent` persiste eventos y `automation.Job` funciona como outbox/cola
durable. Un `BackgroundService` toma únicamente trabajos vencidos, con lock, intento, backoff,
idempotency key, estado fallido y reintento manual. Esto evita depender de memoria del proceso y
funciona con el despliegue actual. Si la carga/operación 24x7 justifica un scheduler con dashboard,
se evaluará Hangfire como única incorporación posterior.

## Comunicación

`communication` define `IEmailProvider` e `IInboundEmailProvider`. Gmail API/Microsoft Graph se
conectarán mediante OAuth, SMTP/Resend/Postmark/SES mediante secretos de servidor. En desarrollo,
un `DevelopmentEmailSink` almacena el resultado como borrador, nunca envía. La UI Inbox muestra
"No configurado" hasta que haya una cuenta conectada; no simula mensajes enviados.

## Agentes

Los agentes tienen definición, autonomía (0–4), permisos, budget y una bitácora de cada run.
Los niveles 1–2 solo analizan/preparan; nivel 3 ejecuta acciones seguras declaradas; nivel 4 queda
limitado por políticas y nunca es autonomía ilimitada. `AgentApproval` centraliza decisiones
humanas y `AgentAction` conserva antes/después, resultado y correlación.

## Infraestructura

Cloudflare Tunnel sirve los subdominios desde el PC actualmente; es útil para demostración pero no
es producción 24/7. Producción futura debe mover API, SQL/backups, worker y cloudflared a un VPS o
servidor permanente, con servicio auto-restart, health checks y logs. Los endpoints existentes
`/health`, `/health/live` y `/health/ready` son la base del status futuro.

## Reutilización

Se reutilizan configuración, DI, rate limiting, manejo global de excepciones, `SqlExtensions`,
auditoría de Marketing, JWT, build/publish y UI compartida. Los subdominios resuelven hacia rutas
Angular dedicadas al inicio; no duplican deploys hasta que haya una razón técnica real.
