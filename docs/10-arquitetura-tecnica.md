# 10 — Arquitetura Técnica

Monólito modular.

Stack:
- .NET 10 / ASP.NET Core / EF Core / SignalR
- .NET MAUI + SQLite
- Vue 3 + TypeScript + Vite + Vuetify + Pinia
- PostgreSQL
- object storage acessado por abstração `IObjectStorage` compatível com S3

Em Development, o object storage local é SeaweedFS executado em container e acessado pela API S3.
O provider de produção não está definido. Tipos específicos do SeaweedFS não podem aparecer em
Domain ou Application.

REST para operações; SignalR para notificações.
Outbox/Inbox para confiabilidade.
BackgroundService/worker para jobs.
OpenTelemetry/logs estruturados/health checks.

Persistência:
- um `AppizzaDbContext` físico;
- um histórico central de migrations;
- configurações EF Core pertencem aos respectivos módulos;
- `version bigint` é token de concorrência e é incrementado pela camada de persistência/EF Core
  em atualizações relevantes, sem trigger PostgreSQL e sem incremento manual pelo domínio;
- isolamento multiestabelecimento usa contexto de estabelecimento, filtros/validações na aplicação
  e testes contra vazamento; PostgreSQL RLS não faz parte da Fundação.

Mídia pertence ao módulo neutro `Media`, não a Communications. Metadados ficam no schema `media`;
os binários ficam no object storage.

Sem microsserviços, Kubernetes, broker ou Redis obrigatórios no MVP.
