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
- cada módulo mantém suas `IEntityTypeConfiguration<T>`;
- `Appizza.Persistence` é a composição autorizada a referenciar os módulos e aplicar seus mappings;
- módulos não dependem de `Appizza.Persistence`.

Mídia pertence ao módulo neutro `Media`, não a Communications. Metadados ficam no schema `media`;
os binários ficam no object storage.

Sem microsserviços, Kubernetes, broker ou Redis obrigatórios no MVP.

## Decisões confirmadas pela validação da Fase 1

- O pipeline JWT preserva os nomes originais dos claims (`MapInboundClaims = false`), inclusive `sub`,
  `establishment_id`, `token_type`, `session_id` e `credential_version`.
- O customizador global de ProblemDetails adiciona `UNEXPECTED_ERROR` somente quando o endpoint não
  forneceu um `errorCode` de negócio.
- Bind e abertura/restauração de sessão serializam pela linha física relevante com `FOR UPDATE` e
  isolamento `ReadCommitted`; índices únicos parciais permanecem como defesa final de consistência.
- Substituição de dispositivo encerra o vínculo anterior, incrementa `credential_version`, revoga as
  sessões antigas e persiste `DeviceReplaced` pela Outbox na mesma transação.
