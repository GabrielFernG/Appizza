# 07 — Pagamentos

Pagamento ocorre no fechamento.

Divisões MVP:
- total;
- igualitária;
- itens;
- valor personalizado.

Métodos MVP:
- Pix;
- crédito;
- débito;
- dinheiro;
- SoftPOS/NFC;
- terminal externo/manual.

Tentativas têm reserva financeira e estados:
Created, AwaitingCustomerAction, Processing, Approved, Declined, Expired, Cancelled, Unknown.

Unknown bloqueia nova cobrança equivalente até reconciliação.

Pix depende de confirmação do provedor.
Dinheiro depende de funcionário.
Estorno não apaga pagamento.

SoftPOS usa abstração de provedor e depende de hardware/provedor compatível.

## Contrato normativo da Fase 7

### Closing

`TableSession` usa `open`, `closing`, `awaiting_payment`, `partially_paid`, `paid` e `closed`.

- `open -> closing` é permitido somente em `open`.
- `closing -> awaiting_payment` ocorre atomicamente quando não há operações impeditivas e os totais foram recalculados.
- `closing -> open` e `awaiting_payment -> open` exigem `PaidAmount == 0`, `ReservedAmount == 0` e nenhuma PaymentAttempt ativa ou inconclusiva.
- Tentativas `Declined`, `Expired` e `Cancelled` não impedem reabertura.
- Operações impeditivas bloqueiam Closing com `SESSION_HAS_PENDING_OPERATIONS`; não há espera assíncrona implícita.
- `partially_paid` ocorre após pagamento aprovado com saldo pendente; `paid` ocorre somente quando `OutstandingAmount == 0`.
- `closed` é encerramento operacional explícito após `paid`; cleaning/release permanece etapa posterior.
- Novos pedidos são permitidos apenas em `open`; pagamentos em `awaiting_payment` e `partially_paid`.

### Invariantes financeiras

`Ordering` é autoridade sobre pedidos e preços; `Payments` sobre tentativas, reservas e refunds; `TableSession` persiste totais.

```text
OutstandingAmount = max(0, TotalAmount - PaidAmount)
AvailableToReserveAmount = max(0, TotalAmount - PaidAmount - ReservedAmount)
```

`ReservedAmount` nunca determina quitação. Valores financeiros não podem ser negativos. Refunds são históricos e separados:

```text
RefundableAmount = ApprovedAmount - EffectiveRefundedAmount
```

### PaymentPlan e divisão

PaymentPlan é persistido com identidade lógica e versões históricas imutáveis. A representação JSON canônica do MVP é `total`, `equal_split`, `by_item` e `custom_amount`; `by_participant`, `participants`, `items` e `amount` não são modos públicos do MVP.

O schema atual ainda não possui `LogicalPlanId`; a evolução mínima da 7.3.1 deverá adicioná-lo sem reescrever a migration histórica, preservando a identidade física de cada versão.

`total` não recebe valor financeiro; `equal_split` recebe `partCount` inteiro positivo; `by_item` referencia `OrderItem.Id` inteiro, sem divisão por unidade nesta fase; `custom_amount` representa uma única allocation parcial positiva, limitada a `AvailableToReserveAmount`. Ordering permanece autoridade sobre pedidos, itens e preços. Divisões usam `decimal`, ordem canônica estável e residual determinístico: `100,00 / 3 = 33,34; 33,33; 33,33`.

### PaymentAttempt e Unknown

Estados: `Created`, `AwaitingCustomerAction`, `Processing`, `Approved`, `Declined`, `Expired`, `Cancelled` e `Unknown`. `Unknown` é não terminal, mantém reserva, bloqueia cobrança equivalente e exige reconciliação conclusiva; nunca significa `Declined`.

Tentativas aprovadas não são apagadas nem reescritas. Refund é operação separada, suporta refunds parciais/múltiplos até o limite aprovado, exige permission, idempotência e auditoria, inclusive após `closed`, sem reabrir a sessão.

### Idempotência, providers e offline

Mutações financeiras exigem `Idempotency-Key`; mesma chave e payload reproduzem status/body, payload diferente retorna `409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST`. Callbacks usam identidade idempotente do provider.

Pix e SoftPOS são provider-agnostic. A implementação inicial usa Fake/Test Provider. A abstração SoftPOS expõe `DiscoverCapabilities`, `StartPayment`, `GetStatus`, `CancelPayment` e `ReconcilePayment`; nenhum SDK ou provider real é selecionado.

Nenhuma mutação financeira é executada ou enfileirada silenciosamente offline. O cliente pode exibir último read model marcado como stale; reconnect exige GET. Retry explícito pode reutilizar a chave persistida da intenção original.
### PaymentAttempt e reserva (Macro 7-A1)

Uma tentativa referencia a versão física do `PaymentPlan` e suas `PaymentPlanAllocation`s por meio de `PaymentAttemptAllocation`, preservando ownership histórico. A criação deriva o valor no servidor, soma as allocations selecionadas e incrementa atomicamente `ReservedAmount`; `PaidAmount` e o estado da sessão não mudam. `Unknown` mantém a reserva. A confirmação confiável de cash usa `payments.confirm_cash` e pertence ao A2. Não há identidade financeira de participante no MVP.
### Implementação atual A2

`PaymentAttempt` referencia o `PaymentPlan.Id` físico e possui ownership por `PaymentAttemptAllocation`. O cliente envia `tableSessionId`, `paymentPlanId`, `allocationIds` e `paymentMethod`; o valor é calculado no servidor pela soma das allocations persistidas. `ReservedAmount` é compromisso, não pagamento.

Estados `Created`, `AwaitingCustomerAction`, `Processing` e `Unknown` mantêm reserva. `Approved` liquida; `Declined`, `Cancelled` e `Expired` liberam a reserva. A confirmação Cash usa `POST /api/v1/payments/attempts/{attemptId}/confirm-cash`, requer funcionário ativo e `payments.confirm_cash`, e é atômica/idempotente.

Transições comprometidas geram Outbox e auditoria versionados. Replays e concorrência não duplicam observabilidade. Provedores, callbacks, reconciliação, refunds e participantes permanecem fora do Macro 7-A.

### Finalização operacional do fechamento

Operations pode finalizar uma sessão financeiramente liquidada por meio do comando
`POST /api/v1/operations/sessions/{sessionId}/closing/finalize`, com a permissão
`closing.finalize`. O estabelecimento é sempre derivado da claim autenticada
`establishment_id`; o cliente não envia `establishmentId`.

O request contém `{ expectedVersion }` e exige `Idempotency-Key`. A transição
aceita exclusivamente `TableSession.Status == paid` e exige, na mesma transação,
`outstandingAmount == 0`, `reservedAmount == 0`, nenhum `PaymentAttempt` em
`Created`, `AwaitingCustomerAction`, `Processing` ou `Unknown`, nenhum
`PaymentProviderExecution` em `Pending`, `Processing`, `AwaitingCustomerAction`
ou `Unknown`, e nenhuma execução com `ReconciliationRequired == true`.
Nenhuma consulta de rede ao provedor é executada.

Em sucesso, `paid -> closed`, a versão avança segundo a convenção de concorrência,
e são gravados um audit `closing.finalize` e o evento Outbox
`session-closing-finalized.v1`. PaymentPlan, allocations, attempts, executions,
claims e totais financeiros permanecem imutáveis. A DiningTable também não é
alterada; sua liberação continua pertencendo ao fluxo existente de limpeza de
mesa após a sessão fechada.

O comando retorna a representação autoritativa atualizada (HTTP 200). Replays
idênticos retornam o resultado persistido sem reexecutar efeitos; chave reutilizada
com request canônico diferente retorna conflito. Versão obsoleta, estado inválido,
saldo/reserva não liquidados ou processamento/reconciliação pendente retornam
ProblemDetails HTTP 409 com os códigos definidos no contrato da API.
### Refund — contrato normativo da Fase 7.5

Refund é um registro histórico append-only ligado ao `PaymentAttempt.Id`. O
PaymentAttempt original permanece `Approved`, com Amount, status, execução do
provider e histórico imutáveis. Refund não altera TableSession, PaidAmount,
ReservedAmount, pedidos ou DiningTable, e não reabre uma sessão `closed`.

```text
CompletedRefundedAmount = SUM(Refund.Amount WHERE Status == Completed)
InFlightRefundAmount = SUM(Refund.Amount WHERE Status IN (Created, Processing))
AvailableToRefundAmount = max(0, PaymentAttempt.Amount
  - CompletedRefundedAmount - InFlightRefundAmount)
```

Somente `Completed` produz efeito financeiro. `Created` e `Processing` reservam
capacidade; `Failed` e `Cancelled` não contam. Refunds totais, parciais e
múltiplos são permitidos até o limite disponível, inclusive após o fechamento.

Refund Cash nasce `Created` e exige confirmação explícita posterior por
funcionário com `payments.refund`; a confirmação `Created -> Completed` não usa
provider, não altera a TableSession e emite audit/Outbox uma única vez.

Cada Refund não Cash possui uma `RefundProviderExecution` dedicada, sem reutilizar
`PaymentProviderExecution`. A intenção e a chave externa estável são persistidas
antes da chamada de rede. Operações equivalentes a `RefundAsync` e
`LookupRefundAsync` suportam resultado normalizado, claims, Unknown e
`ReconciliationRequired`; resultado ambíguo mantém Refund em `Processing` e não
é repetido cegamente.

O POST exige `Idempotency-Key`; a fingerprint inclui tenant, PaymentAttempt.Id,
amount e reason normalizado. Transições geram eventos versionados
`refund-created.v1`, `refund-completed.v1`, `refund-failed.v1` e
`refund-reconciliation-required.v1`. SoftPOS exige apenas suporte arquitetural,
capabilities e Fake/Test Provider; provider comercial e hardware não são
requisitos para concluir a Fase 7.
