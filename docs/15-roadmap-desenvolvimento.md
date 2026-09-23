# 15 — Roadmap

## Visão geral

| Fase | Nome | Status |
|---|---|---|
| 0 | Fundação | COMPLETE |
| 1 | Establishments + Identity + Devices + Tables | COMPLETE |
| 2 | Catalog | COMPLETE |
| 3 | Tablet menu + carrinho | COMPLETE |
| 4 | Ordering + Kitchen | COMPLETE |
| 5 | Status + requests + Delivery | COMPLETE |
| 6 | Promotions + Communications | COMPLETE no escopo certificado; evoluções deferred |
| 7 | Closing + Payments + SoftPOS | COMPLETE no escopo aprovado; SoftPOS comercial deferred |
| 8 | Admin + Reporting Foundation | IN PROGRESS — Macro A / A1 COMPLETE |
| 9 | Reporting | Histórico original; não aprovado |
| 10 | Hardening | Histórico original; não executado como fase dedicada |

## Estado atual

- Último checkpoint certificado: Phase 7.5.
- Phase 7.5: COMPLETE e certificada externamente.
- Phase 7: COMPLETE no escopo aprovado; integração comercial SoftPOS deferred por decisão de provider/hardware.
- Migrations de produção: 19; Migration 20: não criada.
- EF pending model changes: NO.
- Bugs funcionais abertos pela certificação final: 0.
- Próxima fase aprovada: Phase 8 — Admin + Reporting Foundation.
- Phase 8: IN PROGRESS; Macro A IN PROGRESS; A1 — Admin architecture + Catalog audit COMPLETE.
- Primeiro vertical slice: `ADMIN-CATALOG-01`.

## Fases concluídas 0–5

### Fase 0 — Fundação

Status: COMPLETE. API e Worker, módulos físicos, PostgreSQL/EF, Outbox/Inbox, idempotência, ProblemDetails, correlation, OpenAPI, health, OpenTelemetry, object storage e testes estruturais foram estabelecidos.

### Fase 1 — Establishments + Identity + Devices + Tables

Status: COMPLETE, aprovada em 2026-08-11. Inclui estabelecimentos, JWT/refresh, RBAC, dispositivos, bind/revogação/bloqueio, mesas, sessões, concorrência e isolamento por estabelecimento.

### Fase 2 — Catalog

Status: COMPLETE, aprovada em 2026-08-11. Inclui Catalog/Media administrativos, categorias, produtos, variantes, ingredientes, personalizações, pizzas, combos, publicação, disponibilidade, RBAC, Outbox, idempotência e object storage.

### Fase 3 — Tablet menu + carrinho

Status: COMPLETE, aprovada em 2026-08-12. Inclui read model publicado, disponibilidade, ETag, mídia autenticada, SignalR como invalidação, cache SQLite/LRU, offline/reconciliação, menu e carrinho.

### Fase 4 — Ordering + Kitchen

Status: COMPLETE, aprovada em 2026-08-12. Inclui simulação autoritativa, pricing/revalidação, submissão idempotente, snapshots imutáveis, intake de cozinha, Outbox/Inbox, FIFO, aceite e realtime.

### Fase 5 — Status + requests + Delivery

Status: COMPLETE e formalmente validada. Inclui lifecycle de produção, status público, cancelamento/alteração, revisões append-only, rejeição de cozinha, entrega, confirmação, contestação e UX Table/Operations.

## Fase 6 — Promotions + Communications

Status: COMPLETE no escopo certificado.

Promotions, Communications, Operations relacionado, Table.Core, Table Device, realtime/reconnect, API, builds e regressões foram certificados em 2026-08-24.

### Deferred / futuras evoluções

Elegibilidade adicional, semântica expandida de `fixed_amount`, limite de uso, cupons, segmentação, cashback, push/e-mail/SMS e integrações futuras entre Promotion e Communication permanecem fora do escopo certificado. Não invalidam o status COMPLETE entregue.

## Fase 7 — Closing + Payments + SoftPOS

Status: COMPLETE no escopo aprovado. Closing, Payments, PaymentAttempt, providers/Worker, Operations, refunds e certificação 7.5 estão concluídos. SoftPOS comercial permanece deferred por decisão externa de provider/hardware.

### 7.0 — Contract & Product Decisions

Status: COMPLETE. Contratos de Closing/Payments, estados, reservas, Unknown, idempotência, providers, refunds, RBAC e invariantes financeiras foram definidos.

### 7.1 — não utilizado

Não há checkpoint 7.1 consolidado. A numeração foi pulada; nenhum conteúdo é inferido.

### 7.2 — Closing + authoritative balance

Status: COMPLETE e certificado. Lifecycle de sessão, saldo autoritativo, fechamento, finalização idempotente, expected version, reservas e bloqueios de operações pendentes.

### 7.3 — Contract Reconciliation

Status: COMPLETE. A reconciliação operacional e a identidade lógica do PaymentPlan estão implementadas; a antiga classificação PARTIAL era stale.

#### 7.3.1 — PaymentPlan command/query + allocations

Status: COMPLETE. `PaymentPlan` possui `Id` físico, `Version` e `LogicalPlanId` `Guid`; allocations pertencem à versão física; há backfill, coluna obrigatória, tenant isolation e constraint `ux_payment_plan_logical_version`. O contrato está documentado em `docs/adr/ADR-026-payment-plan-logical-identity.md`.

Evoluções opcionais futuras: command público de revisão/versionamento, consulta latest por logical ID e consulta de histórico por logical ID. Não são dívida obrigatória nem bloqueiam a fase.

#### 7.3.2 — PaymentAttempt lifecycle

Status: COMPLETE. Criação, reservas, estados, aprovação, falha, cancelamento, expiração, cash confirmation, concorrência, auditoria e Outbox.

#### 7.3.3 — Providers / Worker

Status: COMPLETE. Abstração provider, execuções duráveis, recovery, reconciliação, crash recovery, Unknown e composição de `IPaymentProvider`.

### 7.4 — Operations + Table UX

Status: COMPLETE no escopo certificado. Operations possui Foundation, Kitchen, Communications, Closing Sessions, Closing Payments, payment details, refunds, Cash confirmation, convergência de provider e UI por permissões.

### 7.5 — Refund + final certification

Status: COMPLETE e CERTIFIED.

Macros A–C não estão consolidadas nos documentos disponíveis; consultar checkpoints históricos se necessário. Macro D — Operations Refund UI + E2E real-browser: 6/6. Macro E — certificação final: COMPLETE.

Evidência final: builds Solution/API/Worker/Fixture/Operations PASS; typecheck PASS; Unit 16/16; Architecture 3/3; Operations Vitest 31/31; Infrastructure integration 13/13; API refund integration 48/48; real-browser E2E 6/6; EF sem pending changes; 19 migrations; Migration 20 ausente; schema de produção inalterado; required gates 15/15.

### SoftPOS — estado atual

Status: CONTRACT_ONLY; `DEFERRED / BLOCKED_BY_PROVIDER_DECISION`.

Existem contratos, capabilities, abstração provider-agnostic e Fake/Test Provider. Não existe provider/adquirente comercial, hardware, SDK ou UX final dependente do hardware. Isso não é bug e não reabre a certificação 7.5.

## Estado transversal

### Operations e Admin

Operations operacional foi antecipado e certificado nas fases 4–7: Foundation, Kitchen, Communications, Closing Sessions, Closing Payments, payment details, refunds, Cash confirmation, provider convergence e permission-aware UI. Admin, como superfície integrada de gestão de estabelecimento, usuários/RBAC, devices/tables, catalog/media e configurações, ainda não está implementado como produto completo.

### Reporting

Status: INFRASTRUCTURE_ONLY. Existe `Appizza.Modules.Reporting` e referências conceituais a projeções; não existem projeções persistidas completas, endpoints/queries finais, UI ou critérios de aceitação.

### Hardening

Status: PARTIAL. Authorization, idempotência, concorrência, recovery, migrations, Testcontainers, E2E, tenant isolation, health e observability já foram aplicados transversalmente. A fase dedicada ainda não foi executada formalmente.

## Pendências e decisões futuras

### Payments

Nenhuma pendência de implementação de `LogicalPlanId`. O contrato está implementado; revisões/history/latest são `OPTIONAL_FUTURE_EVOLUTION`.

### SoftPOS

Provider/adquirente, hardware, capabilities comerciais e UX final dependem de decisão futura. Não bloqueiam a operação atual.

### Promotions

Elegibilidade, `fixed_amount`, limites e escopos adicionais são deferred e não bloqueiam o escopo certificado.

### Admin

Contrato inicial aprovado na Phase 8: escopo MUST definido, RBAC existente reutilizado e implementação ainda não iniciada.

### Reporting

Cinco KPIs iniciais, filtros, consistência `EVENTUAL`, estratégia `HYBRID` e retenção sem purge específico aprovados; implementação ainda não iniciada.

## Roadmap histórico e futuro

O roadmap histórico preserva 8 — Operations/Admin, 9 — Reporting e 10 — Hardening. Essa sequência não aprova automaticamente a próxima fase; Operations já foi parcialmente entregue nas fases anteriores.

## Fase 8 — Admin + Reporting Foundation

Status: IN PROGRESS. Phase 8 foi aprovada; Macro A está IN PROGRESS e A1 — Admin architecture + Catalog audit está COMPLETE. `ADMIN-CATALOG-01` permanece NOT STARTED até a implementação funcional.

### Objetivo

Tornar administráveis, por uma superfície integrada protegida por RBAC, as principais capacidades existentes do estabelecimento e disponibilizar uma fundação de Reporting com indicadores autoritativos derivados dos domínios existentes.

### Escopo Admin

MUST: Admin shell, navegação, tenant context, route guards, estados de UX, Establishment/settings existentes, usuários e roles existentes, Devices, Tables/Sectors e Catalog (categorias, produtos, variantes/configurações, leitura/escrita, publicação e availability).

SHOULD: Media, Promotions no escopo certificado, Communications administrativas e auditoria administrativa quando suportada.

LATER: convites avançados, custom RBAC, editor arbitrário de permissions, providers comerciais e automações administrativas.

OUT OF SCOPE: SoftPOS comercial, novos roles, super-admin global, cross-establishment management, novas regras financeiras, BI genérico e evoluções deferred de Promotions.

Autorização reutiliza permissions existentes, establishment scope e backend como autoridade. `NEW_ROLES_REQUIRED = NO` e `NEW_PERMISSIONS_REQUIRED = NO inicialmente`. Personas administrativas: Administrador e Gerente; roles operacionais permanecem operacionais.

### Reporting inicial

Consistência: `EVENTUAL`. Estratégia: `HYBRID`, com queries simples transacionais e projections idempotentes/rebuildable para agregações. Timezone: `Establishment.Timezone`, com persistência UTC e DST conforme configuração. Retenção: sem purge específico nesta fase.

Filtros MUST: date range e tenant implícito; payment method e order status quando aplicável. Product/category ficam posteriores.

### Macro A — A1 — Admin architecture + Catalog audit

Status: COMPLETE. O Admin será uma área `/admin` dentro do frontend existente `src/Web/Appizza.Operations`, reutilizando Vue 3, Vue Router, Pinia, Vuetify, `AuthContext`, `fetch`/`parseProblem` e o mesmo token/tenant (`establishment_id`). Não é necessário um novo frontend, um segundo login ou um segundo sistema de permissões.

O shell deverá usar layout aninhado, navegação permission-aware e outlet de conteúdo; `/admin/catalog` será a primeira rota. Sem `catalog.read`, a rota deve ser recusada; `catalog.write`, `catalog.publish` e `catalog.availability.manage` controlam respectivamente edição, publicação e disponibilidade. O backend permanece autoridade.

As APIs existentes de `Phase2Endpoints` são suficientes: `/api/v1/operations/catalog/categories`, `products`, `product-variants`, configurações/recursos de pizza, combos, `publication`, `availability`, `validate` e `publish`. Elas já aplicam `establishment_id`, permissions e ProblemDetails. Categories/products/ingredients respeitam `Version` em updates; publicação exige `Idempotency-Key` e cria snapshot/version; availability usa `Idempotency-Key`, versionamento e lock transacional. Não há gap de backend nem necessidade de migration para o slice.

`ADMIN-CATALOG-01` começa com leitura de categories/products/variants, estados de lifecycle, availability e publication; criação/edição de todas as configurações de pizza/combos, upload de Media e demais áreas administrativas ficam para follow-up. Media não é obrigatória para o primeiro slice, pois referências de mídia são opcionais e o catálogo pode ser lido sem upload.

KPIs aprovados:

1. **Pedidos válidos:** Orders com pelo menos um `OrderItem.CommercialStatus != 'cancelled'`; parcialmente cancelado conta uma vez, totalmente cancelado não conta.
2. **Valor bruto de pedidos:** soma do `Order.SubtotalAmount` efetivo dos pedidos válidos; descontos permanecem separados.
3. **Pagamentos aprovados por método:** soma de `PaymentAttempt.Amount` agrupada por método somente para `PaymentAttemptStatus.Approved`.
4. **Refunds concluídos:** soma de `Refund.Amount` somente para `RefundStatus.Completed`.
5. **Valor líquido coletado:** `approvedPayments - completedRefunds`.

`orderedGross`, `discounts`, `orderNet`, `approvedPayments`, `refunded`, `netCollected` e `outstanding` são métricas distintas. Reporting não reimplementa regras financeiras de Payments/Closing.

### Macros

- **Macro A — Contracts + Admin Foundation Architecture:** IA/navigation, tenant context, authorization, contratos Admin/Reporting, arquitetura e migrations esperadas. Status: IN PROGRESS; A1 — Admin architecture + Catalog audit: COMPLETE.
- **Macro B — Admin Foundation:** MUST administrativos, começando por `ADMIN-CATALOG-01`. Status: NOT STARTED.
- **Macro C — Reporting Foundation:** projections/queries/endpoints, cinco KPIs, timezone e consistency metadata. Status: NOT STARTED.
- **Macro D — Admin + Reporting UI Integration:** páginas, dashboard, filtros, UX e permission-aware integration. Status: NOT STARTED.
- **Macro E — Certification:** backend, persistence, RBAC, tenant isolation, projections, migrations, frontend, regression e E2E. Status: NOT STARTED.

### Primeiro vertical slice — ADMIN-CATALOG-01

Status: NOT STARTED. Objetivo: primeira superfície Admin funcional reutilizando contratos existentes de Catalog.

Escopo: entrada/navegação Admin, route guard, tenant context, permission-aware access, Catalog read, availability, publication state/control, loading/error/empty e testes.

APIs: categories, products, variants/configuration, publication e availability de `Phase2Endpoints`. Permissions: `catalog.read`, `catalog.write`, `catalog.publish`, `catalog.availability.manage` e `media.*` somente quando necessário. Migration esperada: NO.

### Gates da Phase 8

Backend: builds, API/application integration, persistence, RBAC, tenant isolation, EF pending model changes e projection idempotency/replay quando Reporting existir. Frontend: build, typecheck, Vitest, permission-aware navigation e estados de UX. E2E: Admin Catalog permission/tenant e Reporting KPI/filter flows. Regression: refund E2E 6/6 quando mudanças compartilhadas justificarem.

### Roadmap futuro

O histórico original preserva 8 — Operations/Admin, 9 — Reporting e 10 — Hardening. A sequência atual substitui a antiga Phase 8 por Admin + Reporting Foundation. Phase 9 é `UNDEFINED / TO BE PLANNED AFTER PHASE 8`; Hardening permanece referência futura a revisar.

## Histórico de auditorias

- R1 — reorganização documental do roadmap.
- P7-L1 — confirmou que `LogicalPlanId` já estava implementado; ADR-026 consolidou o contrato.
- P7-L2 — reconciliou 7.3, 7.3.1 e o status global da Fase 7.
- E3 — certificação final da Phase 7.5 sem pendências.
- P8-D2 — aprovou normativamente Phase 8 — Admin + Reporting Foundation.
