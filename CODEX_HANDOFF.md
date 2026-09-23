# Appizza — Handoff de Continuidade do Codex

## 1. Visão geral

Appizza é uma plataforma modular para operação de restaurante: estabelecimento, catálogo, pedidos, cozinha, mesas/sessões, pagamentos, promoções, comunicações, auditoria e integrações. A solução é um monólito modular em ASP.NET Core/.NET, PostgreSQL como fonte de verdade, EF Core, Worker hospedado, Outbox/Inbox e SignalR apenas para notificações/invalidação. Há clientes Web (Vue/TypeScript) e tablet (.NET MAUI), além da API e Worker.

Princípios congelados: regras financeiras no backend; UUIDs e dinheiro `numeric(14,2)`; isolamento por `EstablishmentId`; idempotência nas mutações; transações e locks PostgreSQL para decisões financeiras; histórico nunca é apagado; SignalR não confirma persistência; eventos críticos passam por Outbox.

## 2. Fases e estado

- Foundation/Phases 1–6: COMPLETE/CERTIFIED conforme as suítes existentes de estabelecimentos, identidade, devices, catálogo, pedidos, cozinha, produção, promoções e comunicações.
- Macro 7-A1 (PaymentAttempt + reservation): COMPLETE/CERTIFIED. Inclui plano físico/lógico, allocations, tentativa autoritativa, ownership, reservas, idempotência, segurança e concorrência.
- Macro 7-A2 (settlement/lifecycle): COMPLETE/CERTIFIED. Inclui Cash, lifecycle, Unknown, transições financeiras, concorrência e Audit/Outbox.
- Documentação 7-A: COMPLETE.
- Macro 7-B: IN_PROGRESS. O núcleo provider-agnostic inicial existe, mas processamento completo, endpoint, persistência adicional, Worker, reconciliação e certificação ainda faltam.
- Macro 7-C: NOT_STARTED.

## 3. Arquitetura atual

`src/Backend/Appizza.Api` compõe a API, endpoints Phase 7, serviços financeiros e DI. `src/Modules` contém os módulos de domínio/contratos. `src/BuildingBlocks` contém abstrações comuns. `src/BuildingBlocks/Appizza.Persistence` contém `AppizzaDbContext`, configurações EF e migrations. `src/Backend/Appizza.Worker` executa workers existentes, incluindo monitor/dispatcher de Outbox. PostgreSQL é acessado por Npgsql/EF Core. Outbox é persistido em `integration.outbox_message`; Inbox garante idempotência por consumidor. Auditoria usa `auditing.audit_entry`. SignalR publica notificações, nunca é fonte de verdade.

## 4. Regras que não podem ser quebradas

- Todo dado financeiro é tenant-scoped e validado no servidor.
- Nunca confiar em amount, status, PaidAmount ou ReservedAmount enviados pelo tablet.
- Idempotency-Key reutilizada com intenção diferente retorna o erro canônico; mesma intenção é replay seguro.
- Operações concorrentes usam transação, advisory lock tenant/sessão, reload do attempt após lock e `TableSession FOR UPDATE`.
- `ReservedAmount` é compromisso; `PaidAmount` é dinheiro liquidado.
- Unknown mantém reserva e não é convertido silenciosamente em Declined.
- Outbox/Audit são gravados atomicamente com a mudança de estado e não duplicam transições reais.
- Não modificar migrations históricas; novas mudanças exigem migration posterior legítima.
- Nunca persistir/logar PAN, CVV, tokens, credenciais ou secrets.

## 5. Fase 7 — pagamentos

Fluxo: `TableSession -> PaymentPlan -> PaymentPlanAllocation -> PaymentAttempt -> PaymentAttemptAllocation -> provider processing`.

`PaymentPlan.Id` é a identidade física da versão; `LogicalPlanId` agrupa versões; `Version` identifica a versão. `PaymentAttempt.PaymentPlanId` aponta para o Id físico. `PaymentAttemptAllocation` registra a ownership das allocations selecionadas e preserva histórico.

Modos públicos: `total`, `equal_split`, `by_item`, `custom_amount`. Não existe Participant no MVP: não criar `PaymentParticipant`, `participantId` público, `localParticipantId`, debt ownership ou `by_participant`.

O cliente envia IDs de allocation e método. O servidor soma `PaymentPlanAllocation.Amount`; o cliente não fornece valor autoritativo. `OutstandingAmount = max(0, TotalAmount - PaidAmount)`. `AvailableToReserveAmount = max(0, TotalAmount - PaidAmount - ReservedAmount)`.

Statuses de tentativa: `Created`, `AwaitingCustomerAction`, `Processing`, `Approved`, `Declined`, `Expired`, `Cancelled`, `Unknown`. Created/AwaitingCustomerAction/Processing/Unknown seguram reserva. Approved move reserva para Paid. Declined/Cancelled/Expired liberam reserva e mantêm Paid. Unknown segura reserva.

Criação: `POST /api/v1/table-device/payments/attempts`, com device ativo, binding atual, sessão/plano/allocation pertencentes ao tenant e Idempotency-Key. Cash é confirmado por funcionário ativo com `payments.confirm_cash` em `POST /api/v1/payments/attempts/{attemptId}/confirm-cash`; device não confirma Cash. Pix/Credit/Debit/SoftPos ainda não têm provider comercial.

## 6. Macro 7-A certificado

A1 implementou `PaymentAttemptReservationService`, amount autoritativo, ownership, reservas atômicas, equal split/by item/custom amount, idempotência, isolamento e corridas. A2 implementou `PaymentAttemptLifecycleService`, Cash confirmation, Approved/Declined/Cancelled/Expired/Unknown, Unknown resolution, efeitos financeiros, locks pós-reload e observabilidade.

Endpoints principais: criação de attempt e confirmação Cash. Migrations A1/A2 preservam o modelo. Certificações históricas: PaymentPlan 27/27, PaymentAttempt 39/39, Lifecycle 42/42 PASS (Failed 0, Skipped 0), Audit/Outbox 7/7, Full API 436/436, Foundation isolado 3/3. Macro 7-A = CERTIFIED.

## 7. Migrations

Ordem atual (17 migrations):

1. 20260810173759_Foundation
2. 20260811195741_Phase1_EstablishmentsIdentity
3. 20260811195901_Phase1_DevicesTables
4. 20260811225737_Phase2_CatalogCore
5. 20260811230734_Phase2_CatalogPizzaCombos
6. 20260811231138_Phase2_CatalogPublicationMedia
7. 20260812164458_Phase4_OrderingKitchen
8. 20260813011830_Phase5_KitchenProduction
9. 20260813025525_Phase5_OrderingRequests
10. 20260813040730_Phase5_OrderItemRevisions
11. 20260815033939_Phase5_Delivery
12. 20260818165751_Phase6_PromotionsCommunications
13. 20260819172354_Phase6_Communications
14. 20260825175708_Phase7_PaymentsFoundation
15. 20260825175739_Phase7_2_AuditTrail
16. 20260827100000_Phase7_3_1A_PaymentPlanLogicalIdentity
17. 20260828120000_Phase7_A1_PaymentAttemptAllocation

Latest é `20260828120000_Phase7_A1_PaymentAttemptAllocation`; anterior é `20260827100000_Phase7_3_1A_PaymentPlanLogicalIdentity`. Não alterar migrations/snapshot certificados. EF factory exige `ConnectionStrings__Appizza`; histórico usa `integration.__ef_migrations_history`. Verificações externas anteriores reportaram pending changes NONE, empty→latest PASS, previous→latest PASS e history verificada; `dotnet-ef` não estava disponível neste ambiente de handoff.

## 8. Baselines de testes

- Phase7PaymentPlanApiTests: 27/27 PASS.
- Phase7PaymentAttemptApiTests: 39/39 PASS.
- Phase7PaymentLifecycleApiTests: 42/42 PASS (Failed 0, Skipped 0).
- Phase7PaymentAuditOutboxTests: 7/7 PASS.
- Full API: 436/436 PASS.
- Foundation isolado: 3/3 PASS.

## 9. Macro 7-B — estado atual

Já implementado: `IPaymentProvider`, `PaymentProviderCapabilities`, `StartPaymentRequest`, `PaymentProviderStatus`, `FakePaymentProvider` interno, capabilities para pix/credit/debit, rejeição explícita de Cash no provider, chave estável baseada no AttemptId e `PaymentProcessingService`.

Arquivos relevantes: `src/Modules/Appizza.Modules.Payments/PaymentFoundation.cs`, `src/Backend/Appizza.Api/PaymentProcessingService.cs`, `src/Backend/Appizza.Api/Program.cs` (DI `IPaymentProvider -> FakePaymentProvider`). O fake retorna `processing` no início e `unknown` para status/reconciliação; não é provider comercial nem endpoint público. `PaymentProcessingService.StartAsync` resolve attempt por tenant, envia amount autoritativo ao provider e persiste Provider/ProviderReference; `ApplyResultAsync` traduz approved/declined/unknown para o lifecycle certificado. Ainda não há endpoint público de início de processamento nem Worker provider dedicado.

## 10. Fronteira 7-A × 7-B

`Provider -> Provider Result -> PaymentProcessingService/trusted orchestration -> PaymentAttemptLifecycleService -> settlement financeiro`. Provider nunca altera diretamente `PaidAmount`, `ReservedAmount`, status financeiro da sessão ou ownership. Não criar segundo settlement. Unknown continua segurando reservation.

## 11. Trabalho pendente 7-B

Próximo: endpoint explícito e testes determinísticos do FakePaymentProvider. Depois: persistência provider adequada, Worker assíncrono, retry seguro, reconciliação, crash recovery, concorrência provider, segurança, observabilidade adicional, Audit/Outbox provider, documentação 7-B, regressão e certificação consolidada.

## 12. Próximo step exato

Implementar `PROCESS ENDPOINT -> PaymentProcessingService -> FakePaymentProvider determinístico -> PaymentAttemptLifecycleService -> focused tests`. Em seguida implementar persistência provider, Worker, reconciliação e crash recovery, sempre preservando o núcleo financeiro certificado.

## 13. Não fazer

Não criar Participant; não alterar migrations; não duplicar settlement; não permitir tablet declarar Approved/Declined; não transformar Unknown em Declined nem liberar sua reserva; não criar fake provider público; não iniciar 7-C; não implementar refund; não armazenar PAN/CVV; não inventar provider comercial; não reabrir A1/A2 certificados sem evidência concreta.

## 14. Worktree atual

O worktree contém alterações acumuladas legítimas de Macro 7-A e arquivos novos não commitados de A2/7-B. Há também `appizza.patch` não rastreado, criado para handoff/migração de ambiente. `git status --short` deve ser reavaliado antes de qualquer alteração. Não executar reset/checkout destrutivo. O diff semântico inclui docs Phase 7, módulos Payments/Auditing, Persistence snapshot/migrations, endpoints/serviços lifecycle e testes de integração.

## 15. Ambiente e comandos

O projeto usa .NET SDK indicado por `global.json`, PostgreSQL 18.4 em Compose/Testcontainers, Docker, Npgsql e EF Core. Compose usa `127.0.0.1:5432:5432`, banco/usuário/senha via variáveis `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`; não registrar valores secretos.

Builds:

```powershell
dotnet build .\src\Modules\Appizza.Modules.Payments\Appizza.Modules.Payments.csproj --no-restore
dotnet build .\src\BuildingBlocks\Appizza.Persistence\Appizza.Persistence.csproj --no-restore
dotnet build .\src\Backend\Appizza.Api\Appizza.Api.csproj --no-restore
dotnet build .\tests\Appizza.Api.IntegrationTests\Appizza.Api.IntegrationTests.csproj --no-restore
```

Testes focados usam `dotnet test ... --filter "FullyQualifiedName~Classe" --no-restore`. EF exige `ConnectionStrings__Appizza`; nunca apontar migrations destrutivas para banco persistente sem confirmação explícita.

## 16. Checklist novo notebook

- [ ] clonar/copiar o repositório completo, incluindo arquivos não rastreados necessários;
- [ ] instalar SDK indicado em `global.json`;
- [ ] restaurar dependências (`dotnet restore`);
- [ ] instalar/iniciar Docker Desktop;
- [ ] validar PostgreSQL descartável/Testcontainers;
- [ ] executar builds dos quatro projetos;
- [ ] executar testes focados antes da suíte global;
- [ ] restaurar `dotnet-ef` pelo manifesto raiz `dotnet-tools.json`, se necessário;
- [ ] configurar `ConnectionStrings__Appizza` somente para banco descartável;
- [ ] validar clients Web/MAUI conforme seus projetos em `src/Clients`/`src/Web`;
- [ ] preservar secrets fora do git.

## 17. Instruções para o próximo Codex

1. Leia este arquivo inteiro.
2. Inspecione o repositório atual.
3. Trate o repositório como fonte de verdade para implementação.
4. Trate este handoff como fonte de verdade para decisões históricas.
5. Não reabra decisões certificadas sem evidência concreta.
6. Continue do Macro 7-B.
7. Não inicie Macro 7-C.
8. Antes de modificar código, compare este handoff com o estado real.
9. Se houver divergência, reporte-a; não invente.
10. Continue a partir do próximo Step registrado: endpoint de processamento, provider fake determinístico e testes focados.

Resultado desta revisão: `CODEX_HANDOFF_FINALIZED`. Nenhuma implementação adicional do Macro 7-B deve ser feita durante esta revisão.

## Correções finais de certificação (estado autoritativo)

`MACRO_7_A = CERTIFIED`. A certificação consolidada é: PaymentPlan 27/27 PASS; PaymentAttempt 39/39 PASS (A1 total 66/66 PASS); `Phase7PaymentLifecycleApiTests` 42/42 PASS (Failed 0, Skipped 0); Audit/Outbox 7/7 PASS; concorrência A2 PASS; Foundation isolado 3/3 PASS; Full API 436/436 PASS (Failed 0, Skipped 0).

EF: `EF_PENDING_MODEL_CHANGES = NONE / PASS`; migration list = 17 / PASS. EMPTY -> LATEST = PASS com history 17/17. Checkpoint anterior: `20260827100000_Phase7_3_1A_PaymentPlanLogicalIdentity`, com history 16/16; PREVIOUS -> LATEST = PASS com history 17/17. Latest: `20260828120000_Phase7_A1_PaymentAttemptAllocation`. Os bancos e containers usados foram descartáveis, foram removidos, e a connection string temporária foi removida. `git diff --check` passou; avisos LF/CRLF foram não bloqueantes.

Registro de incidente: uma execução global anterior teve três falhas em `FoundationApiTests` (`System.ObjectDisposedException` / `Microsoft.AspNetCore.TestHost.TestServer`). Os três testes passaram isoladamente (3/3) e a repetição posterior da Full API passou 436/436. Classificação: `FOUNDATION_TRANSIENT_FAILURE = CLOSED`; `PRODUCTION_FIX_REQUIRED = NO`; `TEST_HOST_FIX_REQUIRED = NO`. Não há bug Foundation pendente conhecido.

`appizza.patch` é artefato de transferência/migração de ambiente, não código pendente automático do produto. Não aplicá-lo automaticamente; antes, comparar seu conteúdo com o worktree/repositório atual para evitar duplicação.

Continuação: `MACRO_7_B = IN_PROGRESS`. Já implementados: `IPaymentProvider`, `PaymentProviderCapabilities`, `StartPaymentRequest`, `PaymentProviderStatus`, `FakePaymentProvider`, capabilities Pix/Credit/Debit, Cash rejeitado, chave de idempotência estável baseada em `PaymentAttempt.Id`, `PaymentProcessingService` e DI `IPaymentProvider -> FakePaymentProvider`. Próximo fluxo: PROCESS ENDPOINT -> PaymentProcessingService -> FakePaymentProvider determinístico -> PaymentAttemptLifecycleService -> focused tests; depois PROVIDER PERSISTENCE -> WORKER -> RECONCILIATION -> CRASH RECOVERY. Não iniciar Macro 7-C.
