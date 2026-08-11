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

## Fase 1 — Establishments + Identity + Devices + Tables

Status: **concluída e aprovada em 2026-08-11**.

Entregáveis:
- estabelecimento, configurações, endereço e horários;
- autenticação de funcionário com `establishmentCode`, JWT e refresh rotativo;
- RBAC no escopo do estabelecimento e matriz inicial de roles;
- registro, bind, autenticação, refresh, heartbeat, unbind, revogação e bloqueio de tablets;
- múltiplos tablets por mesa com limite transacional;
- setores, mesas, abertura/restauração concorrente de sessão e identificação opcional;
- CPF protegido por AES-256-GCM/HMAC-SHA256 e expurgo pelo Worker;
- limpeza/liberação configurável, eventos Outbox e notificações SignalR;
- migrations `Phase1_EstablishmentsIdentity` e `Phase1_DevicesTables`;
- testes obrigatórios de concorrência e isolamento entre estabelecimentos.

Não pertencem à Fase 1: `TemporaryApproval`, `SessionTransfer`, catálogo, menu, carrinho, pedidos,
cozinha, pagamentos e onboarding público de estabelecimento.

Critérios de encerramento aprovados:
- migrations incrementais aplicadas e modelo EF Core sem alterações pendentes;
- autenticação, refresh rotativo, RBAC e isolamento por estabelecimento validados em API real;
- concorrência de bind, limite, substituição, identificação e abertura/restauração de sessão validada
  contra PostgreSQL 18.4 via Testcontainers;
- revogação, bloqueio e invalidação de credenciais validados;
- estado físico e Outbox conferidos nos cenários críticos;
- suíte .NET, arquitetura, MAUI Android/Windows e regressão Vue aprovadas.
