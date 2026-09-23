# ADR-026 — Identidade lógica do PaymentPlan

Status: Accepted (implemented; documented in P7-L1)

## Contexto

`PaymentPlan` é persistido como versões físicas imutáveis. Cada row possui um
`Id` físico, um `Version` e allocations ligadas por `PaymentPlanId`. O modelo
também possui `LogicalPlanId`, que identifica a cadeia lógica compartilhada
pelas versões.

Essa capacidade já está implementada na migration
`20260827100000_Phase7_3_1A_PaymentPlanLogicalIdentity`, no modelo Payments,
no mapping EF, nos endpoints de criação e nos testes de Payments.

## Contrato

- `Id` físico é `Guid` e identifica uma versão específica.
- `LogicalPlanId` é `Guid` obrigatório e identifica a cadeia lógica do plano.
- Um novo plano lógico gera um novo `LogicalPlanId`.
- Uma nova versão do mesmo plano preserva o `LogicalPlanId` e recebe novo `Id` físico.
- `Version` é único por `EstablishmentId + TableSessionId + LogicalPlanId`.
- Allocations continuam ligadas ao `PaymentPlan.Id` físico da versão utilizada.
- `PaymentAttempt` continua apontando para `PaymentPlanId` físico e registra `PaymentPlanVersion`.
- PaymentAttempt, provider execution e refunds não precisam carregar `LogicalPlanId`.

## Persistência e isolamento

O mapping usa `uuid`/`Guid` e a constraint única
`(establishment_id, table_session_id, logical_plan_id, version)`.
As queries continuam tenant-scoped por `EstablishmentId`; um logical ID de
outro estabelecimento não autoriza acesso nem resolução de versão.

## Backfill realizado

A migration adicionou a coluna inicialmente nullable e executou:

```sql
update payments.payment_plan set logical_plan_id = id where logical_plan_id is null;
```

Cada plano físico existente tornou-se uma cadeia lógica própria, sem inventar
agrupamentos históricos. Depois do backfill a coluna tornou-se `NOT NULL`.

## Impacto de contratos

Não é necessário alterar refunds, provider recovery, Worker, Operations ou
Table. A API de criação retorna `planId` físico e `version`; o read model de
Operations também expõe `logicalPlanId` sem substituir referências físicas.
Não há endpoint adicional obrigatório para consultar histórico por logical ID.

## Testes e invariantes

Os testes existentes cobrem identidade compartilhada entre versões, distinção
dos IDs físicos e o índice/mapping esperado. Uma futura alteração de
versionamento deve preservar tenant isolation, uniqueness, PaymentAttempt
ownership e allocations da versão efetivamente utilizada.

## Consequência para o roadmap

O item `LogicalPlanId` não é uma pendência de implementação. Referências que o
descrevem como ausente estão stale e devem ser lidas como histórico anterior à
migration. Não criar Migration 20 para este contrato.
