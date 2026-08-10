# 15 — Roadmap

0. Fundação
1. Establishments + Identity + Devices + Tables
2. Catalog
3. Tablet menu + carrinho
4. Ordering + Kitchen
5. Status + requests + Delivery
6. Promotions + Communications
7. Closing + Payments + SoftPOS
8. Operations/Admin
9. Reporting
10. Hardening

Cada fase deve terminar compilável, testada e documentada.

## Fase 0 — Fundação

Entregáveis:
- estrutura física definitiva e solução compilável;
- um assembly mínimo por módulo, sem scaffolding vazio especulativo;
- API ASP.NET Core e Worker como composition roots;
- Appizza.Table em `src/Clients/Appizza.Table` com targets Android e Windows;
- Appizza.Operations em `src/Web/Appizza.Operations`, usando Vue 3 e npm;
- SDK fixado em `global.json`, Central Package Management e versões não flutuantes;
- PostgreSQL 18.4 e SeaweedFS em `compose.yaml` para Development;
- `AppizzaDbContext` físico e histórico central de migrations;
- migration `Foundation` limitada aos schemas e à infraestrutura técnica de Outbox, Inbox e idempotência;
- ProblemDetails, CorrelationId, OpenAPI, health checks e OpenTelemetry sem backend externo obrigatório;
- abstração `IObjectStorage` compatível com S3, sem tipos de SeaweedFS em Domain/Application;
- testes de arquitetura, persistência, API, Worker, object storage e isolamento multiestabelecimento.

Não pertencem à Fase 0:
- regras funcionais dos módulos;
- login, usuários, roles, permissions e refresh tokens;
- tabelas funcionais de mídia;
- antecipação do modelo conceitual completo em migrations;
- definição do provider de object storage de produção.
