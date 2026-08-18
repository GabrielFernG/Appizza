# 12 — Catálogo de Eventos

Este documento define eventos de domínio, integração e notificações de UI.

## 1. Regras globais

Eventos representam fatos passados.

Envelope padrão:

```json
{
  "eventId": "uuid",
  "eventType": "order-submitted",
  "schemaVersion": 1,
  "occurredAtUtc": "2026-08-09T15:00:00Z",
  "establishmentId": "uuid",
  "correlationId": "uuid",
  "causationId": "uuid-or-null",
  "actor": {
    "userId": "uuid-or-null",
    "deviceId": "uuid-or-null"
  },
  "data": {}
}
```

Obrigatório:
- `eventId`
- `eventType`
- `schemaVersion`
- `occurredAtUtc`
- `establishmentId`
- `correlationId`

Dados sensíveis não entram em eventos.

## 2. Classes de evento

### Domain Event
Interno ao módulo.

### Integration Event
Contrato estável entre módulos. Quando crítico, passa por Outbox.

### UI Notification
Mensagem leve via SignalR. Nunca substitui leitura da API.

---

# 3. Sessões e Mesas

## TableSessionOpened

Produtor: Tables  
Quando: criação bem-sucedida de nova sessão.  
Outbox: sim.

Payload:

```json
{
  "tableSessionId": "uuid",
  "diningTableId": "uuid",
  "sessionNumber": "20260809-0018",
  "openedAtUtc": "2026-08-09T15:00:00Z",
  "openingMode": "on_start_ordering",
  "openedByDeviceId": "uuid"
}
```

Consumidores:
- Ordering: disponibiliza contexto.
- Reporting: ocupação.
- Operations: mapa de mesas.
- Notifications: SignalR.

Idempotência:
cada consumidor usa `eventId`.

## TableClosingStarted

Produtor: Tables  
Outbox: sim.

Efeitos:
- impedir novos pedidos;
- habilitar conta/pagamento;
- notificar todos os tablets.

Payload inclui `tableSessionId`, `diningTableId`, `initiatedByDeviceId`, `startedAtUtc`.

## TableClosingCancelled

Produtor: Tables  
Outbox: sim.

Pré-condição:
nenhum pagamento aprovado e nenhuma regra financeira impeditiva.

Efeito:
sessão retorna a Open.

## TableSessionReopened

Produtor: Tables  
Outbox: sim.

Usado após pagamento parcial somente com autorização.

Payload adicional:
- `reopenedByUserId`
- `approvedByUserId`
- `reason`

## TableSessionPaid

Produtor: Tables/Payments orchestration  
Outbox: sim.

Significa `remainingAmount == 0`; não implica fechamento imediato.

## TableSessionClosed

Produtor: Tables  
Outbox: sim.

Consumidores:
- Devices/Table UI: limpar sessão;
- Reporting;
- Operations;
- Cleaning flow.

## TableSessionTransferred

Produtor: Tables  
Outbox: sim.

Payload:
`previousTableId`, `newTableId`, `transferredByUserId`, `reason`.

## TableCleaningRequested

Produtor: Tables  
Outbox: sim.

Gerado quando `releaseMode = after_cleaning_confirmation`.

## TableCleaningConfirmed

Produtor: Tables  
Outbox: sim.

Payload inclui `confirmedByUserId`.

## TableReleased

Produtor: Tables  
Outbox: sim.

Significa mesa disponível para nova sessão.

---

# 4. Identificação

## SessionCustomerIdentificationProvided

Produtor: Tables  
Outbox: não obrigatório.

Nunca transportar CPF completo.

Payload:

```json
{
  "identificationId": "uuid",
  "tableSessionId": "uuid",
  "identificationType": "cpf",
  "maskedValue": "***.***.***-09",
  "purpose": "session_identification"
}
```

## SessionCustomerIdentificationSkipped

Produtor: Tables  
Outbox: não.

---

# 5. Ordering

## OrderSubmitted

Produtor: Ordering  
Outbox: obrigatório.

Quando:
pedido, itens, configurações, snapshot, totais da sessão e idempotência foram persistidos na mesma
transação. Promotions não participa na Fase 4.

Payload:

```json
{
  "orderId": "uuid",
  "tableSessionId": "uuid",
  "orderNumber": 154,
  "submittedAtUtc": "2026-08-09T15:10:00Z",
  "subtotalAmount": 131.00,
  "discountAmount": 0.00,
  "totalAmount": 131.00,
  "itemCount": 3,
  "sourceDeviceId": "uuid"
}
```

Consumidores registrados na Fase 4:
- `kitchen-intake-v1` -> cria exatamente um ProductionItem por OrderItem;
- `ordering-signalr-v1` -> notifica a mesa sobre mudança reconciliável.

Tables já foi atualizado na transação do pedido. Reporting e Notifications somente se tornam
consumidores quando suas fases forem implementadas.

Não transportar configuração completa da pizza por evento; consumidores usam contratos/projeções apropriados.

## OrderSubmissionRejected

Produtor: Ordering  
Outbox: não.

Usado para telemetria/domínio quando submissão não passa validação. O erro HTTP continua sendo a resposta principal.

## OrderChanged

Produtor: Ordering  
Outbox: sim.

Payload:
- orderId;
- orderItemId;
- previousVersion;
- newVersion;
- priceDifference;
- approvedBy;
- reason.

## OrderCancelled

Produtor: Ordering  
Outbox: sim.

Pouco comum; normalmente cancelamento é por item.

---

# 6. Requests de Item

## OrderItemCancellationRequested
Outbox: sim quando exige decisão humana.

Payload:
`requestId`, `orderItemId`, `tableSessionId`, `requiredApprovalLevel`, `reasonCode`.

Consumidores:
Kitchen/Operations/Notifications.

## OrderItemCancellationApproved
Outbox: sim.

## OrderItemCancellationRejected
Outbox: sim.

## OrderItemCancelled
Outbox: obrigatório.

Consumidores:
- Kitchen: encerrar produção;
- Reporting;
- Notifications.

Payments não é consumidor na Fase 5.

## OrderItemChangeRequested
Outbox: sim quando exige aprovação.

## OrderItemChangeApproved
Outbox: sim.

Inclui `productionAction = continue | restart | reject` quando decisão Kitchen for necessária.

## OrderItemChangeRejected
Outbox: sim.

## OrderItemChanged
Outbox: obrigatório.

Consumidores:
Kitchen, Reporting, Notifications. Ordering atualiza os totais da TableSession na transação de efeito;
Promotions não participa da Fase 5.

---

# 7. Promoções

## PromotionApplied

Produtor: Promotions/Ordering  
Outbox: não obrigatório se persistido no mesmo bounded context; sim se outros módulos dependem.

Payload:
`promotionId`, `promotionVersionId`, `promotionApplicationId`, valores e affectedOrderItemIds.

## PromotionActivated
Outbox: sim.

## PromotionPaused
Outbox: sim.

## PromotionExpired
Outbox: sim.

## PromotionUsageLimitReached
Outbox: sim.

## MenuRefreshRequired

UI notification, não evento de domínio obrigatório.

---

# 8. Produção

## ProductionItemCreated

Produtor: Kitchen  
Causa: OrderSubmitted.  
Outbox: sim.

Payload:
`productionItemId`, `orderItemId`, `stationId`, `queuePosition`, `estimatedPreparationMinutes`.

Consumidor da Fase 4: `kitchen-signalr-v1`, que invalida a fila. O efeito Kitchen e a conclusão de
Inbox são atômicos. Duplicatas não criam outro item.

## ProductionItemAccepted
Outbox: sim.

Na Fase 4 registra a passagem por `accepted` e o estado operacional resultante
`awaiting_preparation`. Consumidor: `kitchen-signalr-v1`.

## Semântica multi-consumer da Fase 4

Cada tipo possui um conjunto versionado de nomes de consumidores. A conclusão de cada consumidor é
registrada em Inbox. `outbox_message.processed_at` somente é preenchido quando todos os nomes
registrados concluíram. Falha parcial, retry, duplicata e restart executam apenas consumidores sem
Inbox concluída. A entrega SignalR não é exactly-once e nunca substitui GET de reconciliação.

Na Fase 5, os eventos abaixo tornam-se parte do contrato planejado; permanecem não implementados enquanto
somente o Checkpoint A estiver concluído.

## ProductionItemRejected
Outbox: obrigatório.

Consumidor crítico `ordering-kitchen-rejection-v1` cria e efetiva exatamente um cancelamento comercial
com origem `kitchen_rejection`, incluindo a request auditável já aprovada. Kitchen nunca altera
diretamente valores ou estados de Ordering.

Payload inclui motivo público e interno por código/referência, nunca detalhe sensível.

## ProductionItemPreparationStarted
Outbox: sim.

## ProductionItemReturnedToQueue
Outbox: sim.

Inclui `previousStatus` e `reasonCode`.

## ProductionItemPaused
Outbox: sim.

## ProductionItemResumed
Outbox: sim.

## ProductionAttemptFailed
Outbox: sim.

Usado para queimado/erro operacional.

## ProductionAttemptRestarted
Outbox: sim.

## ProductionItemReady
Outbox: obrigatório.

## ProductionItemSentToTable
Outbox: obrigatório.

Efeitos:
- cria/ativa DeliveryConfirmation;
- agenda auto confirmação;
- notifica tablets.

## ProductionItemDelivered
Outbox: obrigatório.

---

# 9. Entrega

## DeliveryConfirmationRequested

Produtor: Kitchen  
Outbox: sim.

Payload:
`deliveryConfirmationId`, `productionItemId`, `orderItemId`, `autoConfirmationDueAt`.

## DeliveryConfirmedByCustomer
Outbox: sim.

## DeliveryConfirmedByEmployee
Outbox: sim.

## DeliveryAutoConfirmed
Outbox: sim.

## DeliveryContested
Outbox: obrigatório.

Consumidores:
Kitchen -> alerta;
Worker -> cancela auto-confirmação pendente;
Notifications.

Não cria `operations.occurrence` na Fase 5.

## DeliveryAttemptRestarted
Outbox: sim.

## DeliveryContestResolved
Outbox: sim.

---

# 10. Disponibilidade

## CatalogPublished

Produtor: Catalog
Outbox: obrigatório, na mesma transação da publicação.

Payload: `catalogRevisionId`, `catalogVersion`, `semanticHash`, `publishedByUserId`.

Consumidores: Menu da Fase 3, Reporting futuro e notificações SignalR. Publicações semanticamente
iguais não geram revisão, evento ou incremento de versão.

## IngredientAvailabilityChanged

Produtor: Catalog/Kitchen operations  
Outbox: obrigatório.

Payload:
`ingredientId`, `isAvailable`, `reasonCode`, `changedByUserId`.

Consumidores:
Catalog projection, Menu/SignalR, Kitchen, Reporting.

## ProductAvailabilityChanged
Outbox: sim.

## ProductVariantAvailabilityChanged
Outbox: sim.

## StationAvailabilityChanged
Outbox: sim.

## CatalogAvailabilityRecalculated
Pode ser interno ou UI notification.

Eventos de disponibilidade carregam `availabilityVersion`, disponibilidade explícita e efetiva.
Só são emitidos quando houver mudança real persistida.

## Notificações de menu da Fase 3

O dispatcher da Outbox traduz fatos persistidos em notificações pequenas:

```json
{ "type": "CatalogPublished", "catalogVersion": 43 }
{ "type": "CatalogAvailabilityChanged", "availabilityVersion": 20 }
```

O tablet não aplica o payload como verdade: invalida o cache correspondente e consulta a API. Perda,
duplicação, reordenação ou salto de versão são recuperados por startup, reconexão, resume,
foreground e reconciliação periódica. O catálogo completo nunca trafega por SignalR.

---

# 11. Payments

## PaymentAttemptCreated

Produtor: Payments  
Outbox: sim.

Payload:
`paymentAttemptId`, `paymentId`, `tableSessionId`, `method`, `requestedAmount`, `reservedAmount`.

## PaymentAttemptProcessing
Outbox: não obrigatório.

## PaymentAttemptApproved

Outbox: obrigatório e crítico.

Consumidores:
- Tables -> saldo;
- Reporting;
- Notifications;
- Ordering/Payments allocation projection.

Aprovação repetida não pode reduzir saldo duas vezes.

## PaymentAttemptDeclined
Outbox: sim.

## PaymentAttemptExpired
Outbox: sim.

## PaymentAttemptCancelled
Outbox: sim.

## PaymentAttemptStatusUnknown

Outbox: obrigatório.

Efeitos:
- manter/bloquear reserva adequada;
- abrir alerta;
- agendar reconciliação;
- impedir tentativa duplicada.

## PaymentReconciled

Outbox: obrigatório.

Payload deve indicar `previousStatus`, `resolvedStatus`, `providerReference`.

## CashPaymentRequested
Outbox: sim.

## CashPaymentConfirmed
Outbox: obrigatório.

## PaymentDuplicitySuspected
Outbox: obrigatório.

## PaymentDuplicityResolved
Outbox: sim.

## RefundRequested
Outbox: sim.

## RefundCompleted
Outbox: obrigatório.

## RefundFailed
Outbox: sim.

## ManualDiscountApplied
Outbox: sim.

## ComplimentaryItemGranted
Outbox: sim.

---

# 12. Devices

## DeviceRegistered
Outbox: não obrigatório.

## DeviceBoundToTable
Outbox: sim.

## DeviceReplaced
Outbox: sim.

## DeviceUnboundFromTable
Outbox: sim.

## DeviceConfigurationRevoked
Outbox: obrigatório.

Consumidor crítico: tablet, via SignalR/token validation.

## DeviceBlocked
Outbox: sim.

## DeviceUnblocked
Outbox: sim.

## DeviceWentOffline
Outbox: não necessariamente; pode ser evento operacional derivado.

## DeviceCameOnline
Outbox: não necessariamente.

## DeviceAppVersionOutdated
Outbox: não obrigatório.

Heartbeat não deve gerar Outbox individualmente.

Na Fase 1, `DeviceBoundToTable`, `DeviceReplaced`, `DeviceUnboundFromTable`,
`DeviceConfigurationRevoked`, `DeviceBlocked` e `DeviceUnblocked` passam pela Outbox. `DeviceRegistered`
permanece evento local. Revogação e bloqueio também geram notificação SignalR pequena para
`device:{deviceId}`; a API continua fonte de verdade.

---

# 13. Identity e Segurança

## UserCreated
## UserActivated
## UserBlocked
## UserDisabled
## RoleAssignedToUser
## PermissionGranted
## PermissionRevoked

Outbox depende de necessidade de outros módulos.

## TemporaryApprovalRequested
## TemporaryApprovalGranted
## TemporaryApprovalRejected

Importantes para Operations/Notifications.

## SensitiveDataAccessed

Não transportar valor sensível.

Na Fase 1, eventos de identificação nunca transportam CPF completo, valor criptografado ou hash.

---

# 14. Communications

## CommunicationPublished
Outbox: sim.

## CommunicationPaused
Outbox: sim.

## CommunicationExpired
Outbox: sim.

## CommunicationDisplayed
Alta frequência; preferir batching/projeção.

## CommunicationPlaybackCompleted
Alta frequência; pode ser agregado.

## CommunicationPlaybackFailed
Operacional.

---

# 15. Occurrences

Occurrences e service requests não pertencem à Fase 5; esta seção permanece conceitual para fase futura.

## OccurrenceOpened
## OccurrenceAssigned
## OccurrenceStatusChanged
## OccurrenceResolved
## OccurrenceCancelled

Outbox conforme necessidade de notificação.

---

# 16. Retry e Dead Letter

Outbox consumer:
- retries com backoff;
- `retry_count`;
- `next_retry_at`;
- erro técnico mascarado;
- após limite configurado, marcar como falha operacional e gerar alerta.

Não descartar evento crítico silenciosamente.

---

# 17. Versionamento

Nome lógico:

```text
order-submitted.v1
payment-attempt-approved.v1
```

`schemaVersion` também faz parte do envelope.

Mudança incompatível:
criar v2; não reinterpretar payload histórico.

---

# 18. SignalR

Grupos sugeridos:

```text
establishment:{id}
table:{tableId}
table-session:{sessionId}
kitchen-station:{stationId}
device:{deviceId}
user:{userId}
```

Notificação SignalR deve ser pequena:

```json
{
  "type": "ProductionStatusChanged",
  "resourceId": "uuid",
  "version": 22
}
```

Se o cliente detectar lacuna de versão, buscar snapshot pela API.

---

# 19. Eventos Críticos Obrigatórios em Outbox

- OrderSubmitted
- OrderItemCancelled
- OrderItemChanged
- ProductionItemRejected
- ProductionItemReady
- ProductionItemSentToTable
- ProductionItemDelivered
- DeliveryContested
- PaymentAttemptApproved
- PaymentAttemptStatusUnknown
- PaymentReconciled
- RefundCompleted
- TableClosingStarted
- TableSessionClosed
- IngredientAvailabilityChanged
- CatalogPublished
- DeviceConfigurationRevoked

---

# 19.1 Matriz normativa de eventos da Fase 5

Todos usam envelope versionado, tenant, correlation/causation e Outbox multi-consumer da ADR-021.

| Evento | Producer/transação | Consumidores Inbox obrigatórios da Fase 5 | SignalR mínimo |
|---|---|---|---|
| `ProductionItemPreparationStarted` | Kitchen, transição+attempt | `kitchen-status-projection-v1`, `kitchen-signalr-v1` | `ProductionQueueChanged` |
| `ProductionItemPaused` / `ProductionItemResumed` | Kitchen, status+history | mesmos | `ProductionQueueChanged`, `OrderStatusChanged` |
| `ProductionAttemptFailed` / `ProductionAttemptRestarted` | Kitchen, attempt+status | mesmos | `ProductionQueueChanged`, `OrderStatusChanged` |
| `ProductionItemReady` | Kitchen, attempt+status | `ordering-public-status-v1`, `kitchen-signalr-v1` | `ProductionQueueChanged`, `OrderStatusChanged` |
| `ProductionItemRejected` | Kitchen, status+history | `ordering-kitchen-rejection-v1`, `kitchen-signalr-v1` | `ProductionQueueChanged`, `OrderStatusChanged` |
| `OrderItemCancellationRequested` | Ordering, request | `kitchen-request-v1`, `ordering-signalr-v1` | `OrderItemRequestChanged` |
| `OrderItemCancellationApproved/Rejected` | Ordering, decisão | mesmos | `OrderItemRequestChanged` |
| `OrderItemCancelled` | Ordering, item/order/session totals | `kitchen-commercial-change-v1`, `ordering-signalr-v1` | `OrderStatusChanged` |
| `OrderItemChangeRequested` | Ordering, request/review | `kitchen-request-v1`, `ordering-signalr-v1` | `OrderItemRequestChanged` |
| `OrderItemChangeApproved/Rejected` | Ordering, decisão | mesmos | `OrderItemRequestChanged` |
| `OrderItemChanged` | Ordering, revisão+totais | `kitchen-commercial-change-v1`, `ordering-signalr-v1` | `OrderStatusChanged` |
| `OrderItemRequestWithdrawn` | Ordering, request | `kitchen-request-v1`, `ordering-signalr-v1` | `OrderItemRequestChanged` |
| `ProductionItemSentToTable` | Kitchen, item+confirmation | `ordering-public-status-v1`, `delivery-worker-v1`, `kitchen-signalr-v1` | `DeliveryChanged`, `OrderStatusChanged` |
| `DeliveryConfirmationRequested` | Kitchen, mesma transação | `delivery-worker-v1`, `kitchen-signalr-v1` | `DeliveryChanged` |
| `DeliveryConfirmedByCustomer/Employee` | Kitchen/Delivery, confirmation+item | `ordering-public-status-v1`, `kitchen-signalr-v1` | `DeliveryChanged`, `OrderStatusChanged` |
| `DeliveryAutoConfirmed` | Worker, confirmation+item | `ordering-public-status-v1`, `kitchen-signalr-v1` | `DeliveryChanged`, `OrderStatusChanged` |
| `ProductionItemDelivered` | Kitchen/Delivery, mesma confirmação | `ordering-completion-v1`, `kitchen-signalr-v1` | `OrderStatusChanged` |
| `DeliveryContested` | Kitchen/Delivery, contest | `delivery-worker-v1`, `kitchen-signalr-v1` | `DeliveryChanged`, `OrderStatusChanged` |
| `DeliveryContestResolved` | Kitchen/Delivery, resolução | `ordering-public-status-v1`, `kitchen-signalr-v1` | `DeliveryChanged`, `OrderStatusChanged` |

Payloads mínimos contêm IDs técnicos (`orderId`, `orderItemId`, `productionItemId`, request/revision ou
delivery IDs aplicáveis), versões, previous/new status, reasonCode público quando aplicável e timestamps;
nunca incluem notas internas ou dados sensíveis. Eventos de alteração incluem revision number e diferença
financeira; rejeição inclui apenas códigos de motivo público/interno, não texto sensível.

Retry executa somente consumidores sem Inbox concluída. `ProductionItemRejected` duplicado encontra a
Inbox ou a unicidade da origem e não cancela duas vezes. SignalR pode duplicar, perder ou reordenar e não
finaliza Outbox antes dos consumidores críticos.

---

# 20. Checklist de Implementação de Evento

Antes de adicionar evento:

- [ ] nome representa fato no passado;
- [ ] produtor definido;
- [ ] payload mínimo;
- [ ] sem dados sensíveis;
- [ ] versionado;
- [ ] consumidores identificados;
- [ ] Outbox decidida;
- [ ] idempotência definida;
- [ ] retry definido;
- [ ] SignalR separado de integração;
- [ ] testes de consumidor duplicado.
