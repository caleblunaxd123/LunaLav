# LunaLav Marketing Operating System — plan de implementación

## Decisión de arquitectura

Marketing es una aplicación interna separada de la operación de cada lavandería. Se alojará en
`marketing.lunalav.pe`, compartiendo la API .NET, el despliegue y la instancia SQL Server de
LunaLav, pero con aislamiento lógico mediante el esquema SQL `marketing` y una sesión propia.
No usa las cuentas, permisos ni datos de los tenants de `app.lunalav.pe`.

La primera entrega se servirá desde el mismo artefacto Angular/API para no añadir costo de
infraestructura. Cloudflare deberá enrutar `marketing.lunalav.pe` al puerto 5004. El frontend
detectará ese host y abrirá exclusivamente la aplicación comercial.

## Arquitectura encontrada

| Área | Implementación actual | Reutilización |
| --- | --- | --- |
| Frontend | Angular standalone, lazy routes, signals, SCSS y servicios HTTP | shared icon, page header, skeleton, toaster, paginación y estilos base |
| Backend | ASP.NET Core 9 Web API, controllers, DTOs, ADO.NET repositories | validación automática DTO, manejo global de errores, rate limits, logging y DI |
| Seguridad | JWT Bearer, BCrypt, refresh token rotativo y guards Angular | mismo emisor JWT, claims de aplicación `marketing` y roles propios |
| Datos | SQL Server; scripts numerados e idempotentes | migración `069_marketing_fase1.sql`, esquema `marketing`, índices server-side |
| Despliegue | build Angular + API publicada en los puertos 5004/5005 | sin nuevo servidor; ruta Cloudflare adicional pendiente |

## Límites y seguridad

- La aplicación es privada: no hay registro público ni envío automático de mensajes.
- Usuarios comerciales independientes: `ADMINISTRADOR`, `MARKETING`, `SUPERVISOR`.
- Contraseñas con BCrypt; los refresh tokens de Marketing se guardan hasheados y rotan.
- Las API comerciales validan rol y registran auditoría relevante.
- La procedencia del dato se conserva como manual, importación o proveedor autorizado.
- No se implementará scraping ni se afirmará que Google Places, WhatsApp, Gmail o IA funcionan
  hasta que existan credenciales, consentimiento y un proveedor configurado.

## Modelo inicial

La Fase 1 normaliza lo que es operativo hoy:

- `marketing.User`, `RefreshToken`, `Audit`: identidad, sesión y trazabilidad.
- `marketing.Prospect`: negocio, canales públicos, ubicación, estado, prioridad, score,
  responsable, fuente y datos comerciales verificables.
- `marketing.Tag`, `ProspectTag`: etiquetas reutilizables.
- `marketing.Activity`: timeline inmutable de llamadas, notas, cambios y contactos registrados.
- `marketing.FollowUp` y `marketing.Task`: agenda de seguimiento y trabajo diario.

El score se calcula con reglas transparentes de la primera fase: WhatsApp, redes, web, delivery,
  locales, reseñas, rating e hitos comerciales. El valor queda almacenado para filtrar y podrá
  configurarse en Fase 2 mediante `LeadScoreRule` / historial.

## API de Fase 1

- `POST /api/marketing/auth/login`, `refresh`, `logout`, `me`
- `GET /api/marketing/dashboard`
- `GET|POST /api/marketing/prospects`
- `GET|PUT|DELETE /api/marketing/prospects/{id}`
- `POST /api/marketing/prospects/{id}/activities`
- `GET|POST /api/marketing/followups`, `PATCH /{id}/complete`
- `GET|POST /api/marketing/tasks`, `PATCH /{id}/complete`
- `GET|POST /api/marketing/tags`

Las listas usan paginación, búsqueda y filtros en servidor. Los cambios de etapa, creación,
edición, actividad y cierre generan auditoría; las integraciones externas se incorporarán detrás
de interfaces, no dentro de controllers.

## Páginas de Fase 1

- Login privado de Marketing.
- Dashboard: KPIs, embudo, prioridades y siguiente acción basada en reglas.
- Prospectos: tabla/tarjetas, búsqueda, filtros, tags, estado y alta manual.
- Detalle del prospecto: datos, contexto comercial, actividades y seguimientos.
- Seguimientos y Tareas: hoy, próximos, vencidos y completados.
- Configuración básica: equipo, etiquetas e integraciones con estado “No configurado”.

## Integraciones previstas

Se definen contratos `ILeadDiscoveryProvider`, `IMessageProvider`, `IEmailProvider`,
`IWhatsAppProvider` e `IMarketingAIProvider`. En la Fase 1 no hay proveedor conectado; la UI
debe decirlo explícitamente. Fase 3 añadirá importación CSV/Excel, OpenStreetMap/API autorizada,
deduplicación y exportación. Google Places se evaluará únicamente mediante su API oficial.

## Hoja de ruta

1. **Fase 1 — base operativa:** identidad comercial, layout, dashboard, prospectos, filtros,
   tags, actividades, seguimientos y tareas. Incluye seed solo de desarrollo y pruebas de reglas.
2. **Fase 2 — operación comercial:** pipeline/kanban, campañas, segmentos, agenda, plantillas,
   cola diaria, modo prospección y reglas de scoring configurables.
3. **Fase 3 — descubrimiento controlado:** adaptadores de fuentes autorizadas, importación,
   exportación, detección/fusión de duplicados y mapa.
4. **Fase 4 — inteligencia:** métricas de conversión, recomendaciones explicables, IA marcada
   como sugerencia y automatizaciones con consentimiento/proveedor configurado.

## Riesgos y controles

- **Datos de terceros:** conservar fuente, no automatizar envíos ni recolección prohibida.
- **Duplicados:** índice blando y revisión humana en la Fase 1; fusión en Fase 3.
- **Escala:** índices por estado, distrito, score, responsable y fechas; filtros y páginas se
  resuelven en SQL, no cargando toda la tabla en Angular.
- **Operación local:** el PC debe permanecer encendido y el túnel activo; antes de publicar falta
  crear el hostname `marketing.lunalav.pe` hacia `http://localhost:5004` en Cloudflare.
