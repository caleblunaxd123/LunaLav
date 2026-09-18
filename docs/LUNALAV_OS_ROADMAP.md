# LunaLav OS — roadmap ejecutable

## Fase 0: fundación

1. Schemas `core`, `automation`, `agents`, `communication` y tablas de auditoría/eventos/jobs.
2. Contratos de proveedores (`IEmailProvider`, `ILeadDiscoveryProvider`, `ILLMProvider`) y políticas.
3. Outbox/job runner persistente, idempotencia, reintentos y notificaciones internas.
4. Feature flags y pantallas de integración con estado **No configurado**.

**Criterio de salida:** ninguna automatización puede ejecutar acciones fuera de la política,
cada acción deja auditoría y el worker sobrevive a reinicios.

## Fase 1: Marketing operativo

Completar prospectos, detalle, actividades, tareas, seguimientos, pipeline, daily queue, lead
scoring configurable, campañas/segmentos y modo prospección. Los descubrimientos se importan por
CSV/manual o proveedores autorizados; no scraping.

## Fase 2: Communication Hub

Inbox, threads, drafts, plantillas, CRM linking, consentimiento/supresión, límites y proveedor
OAuth/SMTP. Envío únicamente después de que una cuenta real sea conectada y la política lo permita.

## Fase 3: Agents Control Center

Agentes, tools, runs, permisos, schedules, budgets, approval center y activity feed. No se conecta
un proveedor LLM hasta tener credenciales configuradas y un presupuesto definido.

## Fase 4: Admin y Luna Supervisor

Dashboard ejecutivo, notificaciones, approvals transversales y brief diario basado en datos reales.

## Fases posteriores

Demo analytics/agent, onboarding, support, customer success, finance y analytics avanzados. Cada
módulo se habilita detrás de feature flag y con pruebas de aislamiento/permiso.

## Prioridad inmediata

1. Cerrar por completo Fase 1 de Marketing antes de construir Inbox o agentes.
2. Construir Fase 0 en paralelo solo en contratos y cola durable que necesitarán Inbox/agentes.
3. Conectar correo real únicamente cuando exista buzón de salida, OAuth/proveedor y política de
consentimiento aprobada por LunaLav.
