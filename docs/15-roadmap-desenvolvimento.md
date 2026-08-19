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

## Fase 2 — Catalog

Status: **concluída e formalmente aprovada em 2026-08-11**.

Entregáveis:
- backend administrativo completo de Catalog e Media;
- categorias, produtos, variantes, ingredientes, atributos e personalizações;
- pizzas, multissabores iguais, Monte sua Pizza, massas, bordas e tamanhos;
- combos integralmente validados;
- lifecycle administrativo `active`, `inactive`, `archived`;
- revisão publicada imutável com snapshot JSONB e `catalogVersion` monotônico;
- disponibilidade explícita e efetiva com `availabilityVersion` independente;
- assets reutilizáveis dentro do estabelecimento via `IObjectStorage`;
- RBAC, Outbox, idempotência, concorrência e isolamento cross-tenant;
- migrations `Phase2_CatalogCore`, `Phase2_CatalogPizzaCombos` e
  `Phase2_CatalogPublicationMedia`.

Não pertencem à Fase 2: menu/cache/carrinho do Appizza.Table, UI administrativa do
Appizza.Operations, Promotions, Ordering, Kitchen e funcionalidades das fases seguintes.

Fechamento dos checkpoints:
- **Checkpoint A — CatalogCore:** concluído, migrado e validado contra PostgreSQL real;
- **Checkpoint B — CatalogPizzaCombos:** concluído, incluindo pizzas multissabor, Monte sua Pizza e
  validação de combos;
- **Checkpoint C — CatalogPublicationMedia:** concluído, incluindo publicação, disponibilidade,
  Media, Outbox, idempotência, concorrência e integração real com SeaweedFS;
- suíte .NET, PostgreSQL/Testcontainers, API, arquitetura, MAUI Android/Windows e regressão Vue
  aprovadas;
- nenhum item de menu/cache/carrinho do Appizza.Table, UI administrativa do Appizza.Operations ou
  funcionalidade da Fase 3 foi antecipado.

## Fase 3 — Tablet menu + carrinho

Status: **concluída e formalmente aprovada em 2026-08-12**.

Escopo: read model público da revisão publicada, overlay independente de disponibilidade, ETag
composto, schema 1, hash semântico, mídia autenticada, SignalR como invalidação, SQLite/cache LRU,
offline/reconciliação, UX MAUI de menu e configuração completa e carrinho local por sessão.

Fechamento dos checkpoints:
- **A — Menu Contract & Server Read Model:** concluído e aprovado;
- **B — SQLite & Synchronization:** concluído e aprovado;
- **C — Appizza.Table Menu & Configuration UX:** concluído e aprovado.

Não pertencem à Fase 3: `/cart/simulate` autoritativo, Order, `POST /orders`, reserva, envio/fila
offline, Promotions, Kitchen, Payments e qualquer funcionalidade da Fase 4 ou posterior.

## Fase 4 — Ordering + Kitchen

Status: **concluída e formalmente aprovada em 2026-08-12**.

Objetivo: transformar a intenção do carrinho em pedido historicamente imutável, com preço e
configuração revalidados pelo servidor, submissão idempotente e intake assíncrono de todos os itens
na fila operacional da cozinha.

Fechamento dos checkpoints:
- **A — Documentação e decisões:** concluído e aprovado, com contratos, modelo, eventos, RBAC e ADRs consolidados;
- **B — Ordering Simulation:** concluído e aprovado, incluindo simulação autoritativa persistida, pricing e revalidação;
- **C — Idempotent Order Submission:** concluído e aprovado, incluindo pedido, snapshots, totais da sessão, idempotência e concorrência;
- **D — Kitchen Intake:** concluído e aprovado, incluindo estação, ProductionItem, consumidor Inbox e FIFO;
- **E — Kitchen Acceptance & Realtime:** concluído e aprovado, incluindo fila, detalhe, aceite, RBAC, SignalR e UI operacional mínima;
- **F — Appizza.Table Submission UX:** concluído e aprovado, incluindo simulação, review, envio, `submission_unknown` e reconciliação;
- **G — Auditoria final:** concluído e aprovado, com a matriz integral de testes, builds e auditoria de escopo verdes;
- matriz residual encerrada com **pendências obrigatórias = 0**.

Entregáveis:
- `ordering.cart_simulation` temporária, sem reserva, com validade configurável (300 segundos por
  padrão) e snapshots JSONB de intenção e resultado;
- `/cart/simulate` autoritativo e `POST /orders` com `Idempotency-Key`, `clientSubmissionId` e
  confirmação versionada de revisão;
- Order e OrderItems com configurações estruturadas, snapshot JSONB completo e imutável e número
  obtido de sequence PostgreSQL global;
- combo `fixed_price` ou `calculated`, pizzas de divisões iguais e Monte sua Pizza conforme as regras
  publicadas, sempre recalculados no servidor;
- Station default por estabelecimento, intake eventual de todo OrderItem em um ProductionItem e fila
  FIFO;
- aceite `awaiting_acceptance -> accepted -> awaiting_preparation`, sem iniciar preparo;
- Outbox multi-consumer com conclusão individual em Inbox e finalização do evento somente após
  todos os consumidores registrados;
- UI operacional mínima no Appizza.Operations e fluxo de submissão/reconciliação no Appizza.Table.

Não pertencem à Fase 4: cancelamento ou alteração de pedido, rejeição da cozinha, preparo,
pausa, restart, Ready, entrega, contestação, fechamento de sessão, Promotions, Payments, Delivery
ou qualquer funcionalidade da Fase 5 ou posterior.

## Fase 5 — Status + requests + Delivery

Status: **Fase 5 — concluída e formalmente validada.** Delivery implementado end-to-end no backend, Operations e Table Device.

Objetivo: completar o lifecycle operacional iniciado na Fase 4, expor status público determinístico,
processar solicitações de cancelamento/alteração de item com histórico imutável e concluir a entrega
com confirmação e contestação auditáveis.

Escopo: lifecycle/rejeição de `ProductionItem`; status público; requests exclusivamente de cancelamento
integral e alteração de `OrderItem`; `OrderItemRevision` append-only; consequência comercial idempotente
de rejeição Kitchen; confirmação/auto-confirmação/contestação de entrega; UX Table e Operations.

Fora do escopo: service requests, chamar garçom, `operations.occurrence`, cancelamento parcial por
quantidade, Promotions, Payments, Closing, prioridade/reordenação, scope por estação e UI administrativa.

Checkpoints: **A — Documentação e decisões: concluído**; **B — Production Lifecycle: concluído e
validado**; **C — Order Status Read Model: concluído e validado**; **D — Cancellation Requests:
concluído e validado**; **E — Change Requests: concluído e validado**; **F — Delivery: concluído e validado**;
**G — Appizza.Table: concluído e validado**; **H — Appizza.Operations: concluído e validado**;
**I — Auditoria final: concluída e validada**.

Migrations criadas: `Phase5_KitchenProduction` (Checkpoint B) e `Phase5_OrderingRequests`
(Checkpoint D, infraestrutura de requests/cancellation). Migration incremental autorizada para o
Checkpoint E: `Phase5_OrderItemRevisions` (criada e validada). Migration criada e validada para o Checkpoint F:
`Phase5_Delivery`.
`Phase4_OrderingKitchen` não será editada.

Evidência final do encerramento: API/Testcontainers 250/250; Delivery E2E 4/4;
Unit 85/85; Infrastructure 4/4; Architecture 2/2; Operations frontend 10/10;
builds e validações de backend, Table Device, realtime, Outbox/Inbox e
reconciliação aprovados. Não há bug funcional aberto.
## Fase 6 — Promotions + Communications

Status: especificação normativa preparada; implementação condicionada a decisões de produto.

Objetivo: promoções autoritativas na submissão do pedido e comunicações multimídia vigentes no Table Device.

MUST: lifecycle, RBAC, idempotência, concorrência, snapshot imutável de desconto, publicação/expiração e read model.
SHOULD: limites, prioridade, projeções administrativas e invalidação SignalR.
FUTURE: cupons, segmentação, cashback, push/e-mail/SMS e vínculo Promotion-Communication.

Macro-unidades: Promotions vertical; Communications vertical; integração/clientes e E2E; certificação final.
### Bloqueio normativo remanescente

As decisões de tipos, automaticidade, não acumulação, desempate determinístico e snapshot foram aprovadas. A implementação permanece `BLOCKED_BY_DOCUMENTATION` até decisão explícita sobre escopo de elegibilidade, semântica de `fixed_amount` e eventual limite de uso. Essas decisões têm impacto financeiro direto.
