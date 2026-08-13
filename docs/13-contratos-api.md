# 13 — Contratos da API

Este documento define a API HTTP conceitual do Appizza.

## 1. Convenções

Base:

```text
/api/v1
```

Formato:
JSON UTF-8.

Datas:
ISO-8601 UTC.

Dinheiro:
decimal JSON, nunca float semântico.

Autenticação:
- funcionário: Bearer token;
- table device: Bearer device token;
- endpoints públicos de bootstrap usam token temporário quando documentado.

## 2. ProblemDetails

Erros usam estrutura:

```json
{
  "type": "https://appizza/errors/session-not-open",
  "title": "Sessão não está aberta",
  "status": 409,
  "errorCode": "SESSION_NOT_OPEN",
  "detail": "O fechamento da mesa já foi iniciado.",
  "correlationId": "uuid"
}
```

`errorCode` é contrato estável.

## 3. Idempotência

Header:

```http
Idempotency-Key: <uuid>
```

Obrigatório em:
- vínculo crítico;
- envio de pedido;
- tentativa de pagamento;
- confirmação manual financeira;
- cancelamento/estorno quando documentado.

Mesma chave + mesmo request -> retornar resultado anterior.  
Mesma chave + request diferente -> `409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST`.

---

# 4. Auth de funcionário

## POST `/api/v1/auth/sign-in`

Ator: funcionário.

Request:

```json
{
  "establishmentCode": "PIZZARIA-CENTRO",
  "login": "joao",
  "password": "..."
}
```

Validações:
- usuário existe no estabelecimento;
- ativo;
- credencial válida;
- proteção de brute force.

Response 200:

```json
{
  "accessToken": "...",
  "refreshToken": "...",
  "expiresInSeconds": 900,
  "user": {
    "id": "uuid",
    "name": "João"
  }
}
```

Erros:
- INVALID_CREDENTIALS
- USER_BLOCKED
- USER_INACTIVE

Nunca retornar motivo que facilite enumeração de usuário.

## POST `/api/v1/auth/token/refresh`

Rotaciona refresh token opaco. O token anterior é revogado e somente seu hash é persistido.

## POST `/api/v1/auth/sign-out`

Revoga a sessão autenticada.

## GET `/api/v1/auth/me`

Retorna usuário, estabelecimento, roles e permissões efetivas da Fase 1.

---

# 5. Registro/configuração de dispositivo

## POST `/api/v1/table-devices/register`

Ator: app ainda não configurado.

Request:

```json
{
  "installationId": "uuid",
  "deviceName": "Tablet Android",
  "platform": "android",
  "model": "SM-X110",
  "operatingSystemVersion": "15",
  "applicationVersion": "1.0.0"
}
```

Response 201:

```json
{
  "deviceId": "uuid",
  "status": "awaitingConfiguration",
  "configurationToken": "...",
  "expiresAt": "2026-08-09T15:30:00Z"
}
```

Eventos:
DeviceRegistered.

## GET `/api/v1/table-devices/configuration/available-tables`

Auth: funcionário.  
Permissão: `devices.table.configure`.

Response:

```json
{
  "tables": [
    {
      "id": "uuid",
      "name": "Mesa 04",
      "sectorName": "Salão",
      "activeDeviceCount": 1,
      "maximumDeviceCount": 2,
      "canBindAnotherDevice": true
    }
  ]
}
```

## POST `/api/v1/table-devices/{deviceId}/bind`

Auth: funcionário + configuration token.  
Idempotency-Key: obrigatório.

Request:

```json
{
  "tableId": "uuid",
  "configurationToken": "...",
  "replaceDeviceId": null,
  "reason": null
}
```

Validações:
- device registrado;
- token válido;
- funcionário tem permissão;
- mesa ativa;
- limite de devices não excedido;
- device não está vinculado a outra mesa;
- concorrência transacional.

Response:

```json
{
  "deviceId": "uuid",
  "bindingId": "uuid",
  "table": {
    "id": "uuid",
    "name": "Mesa 04"
  },
  "deviceAccessToken": "...",
  "refreshToken": "...",
  "accessTokenExpiresInSeconds": 1800
}
```

Erros:
- DEVICE_BLOCKED
- TABLE_DEVICE_LIMIT_REACHED
- DEVICE_ALREADY_BOUND
- TABLE_NOT_AVAILABLE
- CONFIGURATION_TOKEN_EXPIRED
- INSUFFICIENT_PERMISSION

Eventos:
DeviceBoundToTable / DeviceReplaced.

O bind atribui `establishment_id` ao dispositivo pendente. Um estabelecimento já atribuído não pode
ser trocado sem revogação/reset explícito.

## GET `/api/v1/table-devices/me`

Auth: device.

Retorna configuração atual, vínculo, versão mínima e sessão ativa resumida.

## POST `/api/v1/table-devices/token/refresh`

Rotação obrigatória do refresh token.

## POST `/api/v1/table-devices/heartbeat`

Auth: device.

Request:

```json
{
  "applicationVersion": "1.0.0",
  "batteryPercentage": 72,
  "networkStatus": "online",
  "kioskModeActive": true,
  "lastCatalogSyncAt": "2026-08-09T15:00:00Z"
}
```

Sem Outbox por heartbeat.

## Operações de dispositivo

```text
POST /api/v1/operations/table-devices/{deviceId}/unbind
POST /api/v1/operations/table-devices/{deviceId}/revoke-configuration
POST /api/v1/operations/table-devices/{deviceId}/block
POST /api/v1/operations/table-devices/{deviceId}/unblock
```

Todas exigem autenticação de funcionário, tenant resolvido pelo token e permissão correspondente.
Revogação aumenta `credential_version`, encerra sessões de dispositivo e invalida credenciais antigas.

---

# 6. Sessão

## GET `/api/v1/table-device/session`

Auth: device.

Retorna mesa e sessão ativa ou null.

## POST `/api/v1/table-device/session/open-or-get`

Auth: device.  
Idempotency-Key: recomendado.

Sem sessão:
cria.

Com sessão ativa:
retorna a existente.

Concorrência:
índice único parcial garante uma sessão ativa por mesa.

Response:

```json
{
  "session": {
    "id": "uuid",
    "number": "20260809-0018",
    "status": "open",
    "openedAt": "2026-08-09T15:00:00Z",
    "createdNow": true,
    "version": 1
  },
  "nextStep": "customerIdentification"
}
```

O modo de abertura registrado é `on_start_ordering`.

Eventos:
TableSessionOpened somente quando criada.

## POST `/api/v1/table-device/session/customer-identification`

Request:

```json
{
  "sessionId": "uuid",
  "identificationType": "cpf",
  "value": "12345678909",
  "purposeAcknowledged": true
}
```

Nunca devolver CPF completo.

O mesmo CPF normalizado após `provided` é idempotente. CPF diferente ou tentativa após `skipped`
retorna `CUSTOMER_IDENTIFICATION_ALREADY_RESOLVED`. Em corrida entre provide e skip, apenas uma
operação vence.

Erros:
- CUSTOMER_IDENTIFICATION_ALREADY_RESOLVED
- INVALID_CUSTOMER_IDENTIFICATION
- CUSTOMER_IDENTIFICATION_DISABLED

## POST `/api/v1/table-device/session/customer-identification/skip`

Resolve a etapa como skipped.

## GET `/api/v1/table-device/session/snapshot`

Retorna:
- sessão;
- pedidos;
- conta;
- entregas pendentes;
- requests pendentes;
- tentativas de pagamento relevantes;
- versão;
- serverTime.

Usado em reconexão/restart.

---

# 7. Administração de Catalog e Media

Todos os endpoints desta seção usam autenticação de funcionário, tenant do token, RBAC e 404 para
recursos fora do estabelecimento. Mutação usa `version`/`If-Match`; publicação, disponibilidade e
upload usam `Idempotency-Key`.

Recursos administrativos ficam sob:

```text
/api/v1/operations/catalog/categories
/api/v1/operations/catalog/products
/api/v1/operations/catalog/product-variants
/api/v1/operations/catalog/ingredients
/api/v1/operations/catalog/customization-groups
/api/v1/operations/catalog/pizza-sizes
/api/v1/operations/catalog/doughs
/api/v1/operations/catalog/crusts
```

Cada coleção suporta listagem e criação; recursos suportam leitura, atualização e ações explícitas
`activate`, `deactivate` e `archive` quando aplicáveis. Configurações compostas:

```text
PUT /api/v1/operations/catalog/products/{id}/categories
PUT /api/v1/operations/catalog/products/{id}/ingredients
PUT /api/v1/operations/catalog/products/{id}/customization-groups
PUT /api/v1/operations/catalog/products/{id}/pizza-configuration
PUT /api/v1/operations/catalog/products/{id}/combo-configuration
```

Disponibilidade:

```text
POST /api/v1/operations/catalog/availability/ingredients/{id}/change
POST /api/v1/operations/catalog/availability/products/{id}/change
POST /api/v1/operations/catalog/availability/variants/{id}/change
GET  /api/v1/operations/catalog/availability
```

Request de mudança contém `available` e `reasonCode`. Response contém `explicitAvailable`,
`effectiveAvailable` e `availabilityVersion`. Repetição sem mudança é idempotente e não incrementa.

Publicação:

```text
POST /api/v1/operations/catalog/validate
POST /api/v1/operations/catalog/publish
GET  /api/v1/operations/catalog/publication
```

Publicação válida retorna `catalogRevisionId`, `catalogVersion`, `semanticHash` e `publishedAt`.
Sem alteração semântica retorna 409 `CATALOG_NO_CHANGES_TO_PUBLISH`. Falha de validação retorna 422
`CATALOG_VALIDATION_FAILED` e persiste tentativa `rejected` com erros estruturados.

Media:

```text
POST /api/v1/operations/media/assets
PUT  /api/v1/operations/media/assets/{id}/content
GET  /api/v1/operations/media/assets/{id}
POST /api/v1/operations/media/assets/{id}/archive
```

Criação retorna asset `pending_upload`. Envio valida MIME, tamanho, checksum e chave segura; sucesso
leva a `ready`. Falha leva a `failed`. Tipos específicos do provider não aparecem no contrato.

# 7A. Menu

Contrato inicial: `schemaVersion = 1`. Campos JSON desconhecidos compatíveis são ignorados. Versão de
schema não suportada não substitui cache conhecido e, sem cache compatível, leva a estado seguro de
indisponibilidade/retry.

## GET `/api/v1/table-device/menu`

Auth: device.

Suporta:
- `If-None-Match`;
- `knownCatalogVersion` e `knownAvailabilityVersion` como fallback ao header.

ETag semântico: `"catalog-{catalogVersion}-availability-{availabilityVersion}-schema-1"`.
Retorna 304 somente quando os três componentes coincidirem. Mudança apenas de disponibilidade deve
ser obtida pelo endpoint incremental; o menu completo permanece fallback.

Response conceitual:

```json
{
  "menu": {
    "catalogRevisionId": "uuid",
    "catalogVersion": 42,
    "availabilityVersion": 19,
    "schemaVersion": 1,
    "publishedAt": "2026-08-09T12:00:00Z"
  },
  "navigation": [],
  "products": {},
  "sections": [],
  "mediaManifest": [],
  "settings": {}
}
```

304 quando aplicável.

Cada item configurável inclui `configurationVersion` no formato
`appizza-config-v1:{sha256-hex}`. O hash usa JSON canônico de produto, variante, tamanho, sabores,
ingredientes, grupos/opções, massas, bordas, preços, limites e regras relevantes; não inclui
timestamps, auditoria nem versão física do banco.

## GET `/api/v1/table-device/menu/availability`

Auth: device. Suporta `If-None-Match` próprio no formato
`"availability-{availabilityVersion}-schema-1"` e `knownAvailabilityVersion`. Retorna overlay de
ingredientes, produtos e variantes com disponibilidade explícita/efetiva e `reasonCode`. Retorna 304
quando não mudou. Se `catalogVersion` informado pelo cliente não for a revisão corrente, retorna 409
`CATALOG_VERSION_MISMATCH`, levando o tablet a buscar o menu completo.

## GET `/api/v1/table-device/menu/products/{productId}`

Retorna configuração detalhada do produto.

Erros:
- PRODUCT_NOT_FOUND
- PRODUCT_NOT_PUBLISHED

## GET `/api/v1/table-device/menu/combos/{productId}`

Retorna grupos/restrições do combo.

Combo dentro de combo retorna configuração inválida e nunca integra uma revisão consumível no MVP.

## GET `/api/v1/table-device/media-assets/{assetId}/content`

Auth: device. A API valida credencial, tenant, ownership, estado `ready` e referência pela revisão
publicada antes de fazer streaming via `IObjectStorage`. O tablet nunca recebe bucket, object key ou
credenciais do provider. Suporta `If-None-Match` baseado no checksum. Falhas usam
`MEDIA_ASSET_NOT_FOUND` ou `MEDIA_ASSET_NOT_PUBLISHED`, sem revelar recurso cross-tenant.

---

# 8. Simulação do carrinho

Este contrato pertence à Fase 4. Na Fase 3 não há endpoint de mutação/simulação do carrinho, Order,
reserva ou envio: o carrinho é local e valores são estimativas não autoritativas.

## POST `/api/v1/table-device/cart/simulate`

Auth: device.

Não altera estado comercial, mas persiste a simulação temporária para review e submissão.

Request:

```json
{
  "sessionId": "uuid",
  "localCartId": "uuid",
  "catalogVersion": 42,
  "availabilityVersion": 19,
  "items": [
    {
      "localCartItemId": "local-1",
      "productId": "uuid",
      "productVariantId": null,
      "productType": "pizza",
      "quantity": 1,
      "configurationVersion": "sha256-v1:hex",
      "configuration": {}
    }
  ]
}
```

Valida:
- sessão open;
- produto/configuração;
- disponibilidade;
- preço;
- pizza/frações;
- combo;
- promoções;
- limites.

Response:

```json
{
  "simulationId": "uuid",
  "simulationVersion": "sha256-v1:hex",
  "validUntil": "2026-08-09T15:10:00Z",
  "catalogVersion": 42,
  "availabilityVersion": 19,
  "items": [],
  "totals": {
    "subtotalAmount": 131.00,
    "discountAmount": 0.00,
    "totalAmount": 131.00
  },
  "warnings": [],
  "requiresReview": false,
  "canSubmit": true
}
```

`estimatedUnitAmount`, quando enviado, serve somente para comparação de UX. `requiresReview` é
verdadeiro somente por diferença material de intenção, configuração, disponibilidade ou preço;
mudança isolada de contador apenas atualiza as versões retornadas. A validade inicial é 300 segundos
configuráveis, sem reserva, e nunca dispensa revalidação final.

---

# 9. Envio de pedido

## POST `/api/v1/table-device/orders`

Auth: device.  
Idempotency-Key: obrigatório.

Request:

```json
{
  "sessionId": "uuid",
  "localCartId": "uuid",
  "clientSubmissionId": "uuid",
  "simulationId": "uuid",
  "simulationVersion": "sha256-v1:hex",
  "acceptedReview": true
}
```

A intenção completa está vinculada ao snapshot persistido da simulação; nenhuma quantia enviada
pelo cliente é entrada do cálculo autoritativo.

Validação definitiva.
Não confiar em preço enviado pelo cliente.

Transação:
1. Order;
2. OrderItems;
3. configurações;
4. snapshots;
5. atualização dos totais da sessão;
6. Outbox OrderSubmitted;
7. idempotência e commit.

Response 201:

```json
{
  "order": {
    "id": "uuid",
    "number": 154,
    "status": "submitted",
    "submittedAt": "2026-08-09T15:12:00Z",
    "totalAmount": 131.00,
    "kitchenIntakeStatus": "pendingKitchenIntake"
  },
  "session": {
    "id": "uuid",
    "totalAmount": 284.00,
    "remainingAmount": 284.00,
    "version": 12
  },
  "nextStep": "orderStatus"
}
```

Erros:
- SESSION_NOT_OPEN
- CART_EMPTY
- PRODUCT_NOT_AVAILABLE
- PRODUCT_CONFIGURATION_CHANGED
- ORDER_REQUIRES_REVIEW
- IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST
- CLIENT_SUBMISSION_ALREADY_USED
- SIMULATION_NOT_FOUND
- SIMULATION_EXPIRED
- SIMULATION_VERSION_MISMATCH
- REVIEW_CONFIRMATION_REQUIRED
- NO_ACTIVE_PRODUCTION_STATION

## GET `/api/v1/table-device/orders/submissions/{idempotencyKey}`

Reconcilia resposta perdida.

O escopo da busca é sempre o tenant e o device autenticados. Responde o mesmo envelope persistido da
submissão ou 404 `ORDER_SUBMISSION_NOT_FOUND`, sem revelar chaves de outro tenant.

### Intenção canônica de item (schema 1)

Cada item de `/cart/simulate` possui `localCartItemId`, `productId`, `productVariantId` opcional,
`quantity`, `configurationVersion` string e `estimatedUnitAmount` opcional. `configuration` pode
conter somente os blocos aplicáveis:

```json
{
  "selectedOptions": [{ "optionId": "uuid", "quantity": 1 }],
  "ingredients": [{ "ingredientId": "uuid", "action": "remove|add", "quantity": 1 }],
  "pizza": {
    "sizeId": "uuid",
    "doughId": "uuid",
    "crustId": "uuid",
    "fractions": [
      { "position": 1, "flavorId": "uuid" },
      { "position": 2, "custom": { "ingredients": [{ "ingredientId": "uuid", "quantity": 1 }] } }
    ]
  },
  "combo": {
    "groups": [{
      "groupId": "uuid",
      "selections": [{ "comboGroupItemId": "uuid", "quantity": 1, "configuration": {} }]
    }]
  },
  "notes": ["texto"]
}
```

IDs precisam pertencer à revisão publicada e ao mesmo tenant. Ingredientes required não podem ser
removidos; limites, opções, pizza, massas/bordas, frações iguais e combo não recursivo são revalidados.
Campos monetários dentro de `configuration` são rejeitados.

## GET `/api/v1/operations/kitchen/stations`

Auth: funcionário com `kitchen.queue.view`. Lista somente estações ativas do tenant.

## GET `/api/v1/operations/kitchen/production-items?stationId={uuid}`

Auth: `kitchen.queue.view`. Retorna fila `awaiting_acceptance`/`awaiting_preparation` ordenada por
`queuePosition`, com fallback de polling; 404 é usado para station cross-tenant.

## GET `/api/v1/operations/kitchen/production-items/{productionItemId}`

Auth: `kitchen.production.view`. Retorna snapshot operacional do item, mesa, pedido e histórico
permitido, sem consultar o catálogo atual.

## POST `/api/v1/operations/kitchen/production-items/{productionItemId}/accept`

Auth: `kitchen.production.accept`. `Idempotency-Key` obrigatório. Aceita apenas
`awaiting_acceptance`, registra as transições `accepted` e `awaiting_preparation` e emite
`ProductionItemAccepted`. Replay idêntico retorna o resultado anterior; aceitação concorrente gera
um único efeito. Erros: `PRODUCTION_ITEM_NOT_FOUND`, `PRODUCTION_ITEM_ALREADY_ACCEPTED`,
`PRODUCTION_ITEM_INVALID_STATE` e `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST`.

A idempotência é por requisição identificada pela `Idempotency-Key`, não por recurso. Se duas
chaves distintas concorrerem, somente a vencedora retorna sucesso. A perdedora registra e retorna
`409 PRODUCTION_ITEM_ALREADY_ACCEPTED`; replays posteriores dessa chave reproduzem o mesmo conflito,
sem criar nova transição, histórico ou `ProductionItemAccepted`.

SignalR publica payloads mínimos `KitchenQueueChanged` e `OrderSubmissionChanged`; eles são apenas
invalidação. A UI sempre reconcilia pelos GETs.

---

# 10. Acompanhamento

## GET `/api/v1/table-device/session/orders/status`

Resposta otimizada com status principal/substatus por pedido e item.

## GET `/api/v1/table-device/orders/{orderId}`

Detalhe:
- itens;
- snapshots;
- valores;
- histórico público;
- requests;
- promoções.

Autorização:
pedido precisa pertencer à sessão vinculada.

---

# 11. Alteração e cancelamento

## POST `/api/v1/table-device/order-items/{orderItemId}/cancellation-requests`

Request:

```json
{
  "reasonCode": "ORDERED_BY_MISTAKE",
  "customerNote": "..."
}
```

Regras:
- estágio inicial -> automático;
- in preparation/paused -> cozinha;
- ready -> gerente.

## POST `/api/v1/table-device/order-items/{orderItemId}/change-requests`

Enviar configuração completa desejada.

Se preço aumentar:
requer confirmação explícita do cliente.

## POST `/api/v1/table-device/order-item-requests/{requestId}/withdraw`

Só enquanto pendente e ainda retirável.

## POST `/api/v1/operations/order-item-requests/{requestId}/approve`

Auth: funcionário.
Permissão depende do nível.

Para mudança em preparo, request pode incluir:

```json
{
  "productionAction": "continue",
  "reason": "Alteração ainda possível."
}
```

`productionAction`:
- continue
- restart

## POST `/api/v1/operations/order-item-requests/{requestId}/reject`

Motivo obrigatório.

---

# 12. Entrega

## POST `/api/v1/table-device/order-items/{id}/delivery-confirmation`

Auth: device.

Request:

```json
{
  "confirmation": "received",
  "source": "customer"
}
```

O backend deriva/valida fonte quando houver autenticação de funcionário.

## POST `/api/v1/table-device/order-items/{id}/delivery-contestation`

Request:

```json
{
  "reasonCode": "NOT_RECEIVED"
}
```

Efeitos:
- não marcar entregue;
- abrir contestação;
- cancelar auto-confirmação;
- notificar operação.

Configuração do estabelecimento define:
- modos de confirmação;
- auto confirmação;
- minutos;
- destinatário da contestação.

---

# 13. Fechamento

## POST `/api/v1/table-device/session/start-closing`

Auth: device.

Efeito:
Open -> Closing.

Erros:
- SESSION_ALREADY_CLOSING
- SESSION_NOT_OPEN
- PAYMENT_ALREADY_IN_PROGRESS

## POST `/api/v1/table-device/session/cancel-closing`

Permitido enquanto regras financeiras permitirem.

## GET `/api/v1/table-device/session/account`

Response:

```json
{
  "sessionId": "uuid",
  "orders": [],
  "adjustments": [],
  "totals": {
    "subtotal": 180.00,
    "promotionDiscount": 20.00,
    "manualDiscount": 0.00,
    "serviceCharge": 0.00,
    "coverCharge": 0.00,
    "total": 160.00,
    "paid": 40.00,
    "reserved": 0.00,
    "remaining": 120.00
  }
}
```

Cada item deve poder abrir o snapshot detalhado.

---

# 14. Plano de pagamento e participantes

## POST `/api/v1/table-device/session/payment-plan`

Modes:
- total
- participants
- items
- amount
- equal_split

Request participants:

```json
{
  "mode": "participants",
  "participants": [
    {
      "localParticipantId": "p1",
      "displayName": "Pessoa 1"
    },
    {
      "localParticipantId": "p2",
      "displayName": "Pessoa 2"
    }
  ]
}
```

Response cria participant IDs e plano persistido.

Itens podem ser atribuídos a participantes.

---

# 15. Criar tentativa de pagamento

## POST `/api/v1/table-device/payments/attempts`

Auth: device.  
Idempotency-Key: obrigatório.

Request:

```json
{
  "sessionId": "uuid",
  "participantId": "uuid-or-null",
  "amount": 60.00,
  "method": "pix",
  "allocation": {
    "type": "participant"
  }
}
```

Validações:
- sessão no fechamento;
- método habilitado;
- valor > 0;
- não exceder saldo disponível;
- reserva sem conflito;
- participante pertence à sessão.

Response Pix:

```json
{
  "paymentAttemptId": "uuid",
  "status": "awaitingCustomerAction",
  "method": "pix",
  "amount": 60.00,
  "pix": {
    "qrCode": "...",
    "copyPasteCode": "...",
    "expiresAt": "2026-08-09T15:30:00Z"
  }
}
```

Erro crítico:
PAYMENT_STATUS_UNKNOWN.

---

# 16. Cartão / SoftPOS

A API inicia uma tentativa; o app/provider nativo executa o fluxo.

Abstrações:
- SoftPosPaymentProvider
- ExternalTerminalPaymentProvider
- ManualCardPaymentProvider

O resultado do dispositivo nunca substitui verificação server-side quando o provedor disponibilizar consulta/reconciliação.

Erros:
- SOFTPOS_NOT_AVAILABLE
- NFC_NOT_AVAILABLE
- CARD_PROVIDER_UNAVAILABLE
- PAYMENT_DECLINED

---

# 17. Dinheiro

Criar attempt/cash request.

Campos:
- amount;
- changeForAmount.

Funcionário confirma em endpoint operacional protegido, por exemplo:

## POST `/api/v1/operations/cash-payments/{paymentId}/confirm`

Auth: funcionário.
Permissão: `payments.cash.confirm`.

Request:

```json
{
  "receivedAmount": 100.00,
  "deliveredChangeAmount": 60.00
}
```

Idempotency-Key: obrigatório.

---

# 18. Refund

## POST `/api/v1/payments/{paymentId}/refunds`

Auth: funcionário.
Permissão: `payments.refund.create`.

Request:

```json
{
  "amount": 20.00,
  "reason": "Item cancelado após pagamento."
}
```

Valida:
- pagamento elegível;
- soma de refunds <= approved amount;
- provedor suporta operação;
- aprovação adicional conforme perfil.

Response:
refund pending/completed conforme provedor.

---

# 19. Limpeza/liberação

Endpoint operacional sugerido:

## POST `/api/v1/operations/tables/{tableId}/confirm-cleaning`

Auth: funcionário.

Pré-condição:
mesa AwaitingCleaning.

Efeito:
AwaitingCleaning -> Available.

Evento:
TableCleaningConfirmed -> TableReleased.

---

# 20. Chamado de atendimento

MVP recomendado:

## POST `/api/v1/table-device/service-requests`

Tipos:
- waiter
- cutlery
- napkin
- water
- account_help
- other

Request:

```json
{
  "type": "waiter",
  "message": null
}
```

Cria ocorrência operacional leve.

---

# 21. Códigos de erro principais

## Device
DEVICE_NOT_REGISTERED  
DEVICE_BLOCKED  
DEVICE_ALREADY_BOUND  
TABLE_DEVICE_LIMIT_REACHED  
CONFIGURATION_TOKEN_EXPIRED  
DEVICE_CREDENTIAL_REVOKED  
APPLICATION_UPDATE_REQUIRED  

## Session
SESSION_NOT_FOUND  
SESSION_NOT_OPEN  
SESSION_ALREADY_CLOSING  
SESSION_ALREADY_PAID  
ACTIVE_SESSION_CONFLICT  
TABLE_BLOCKED  

## Catalog
MENU_NOT_PUBLISHED  
PRODUCT_NOT_FOUND  
PRODUCT_NOT_AVAILABLE  
VARIANT_UNAVAILABLE  
INVALID_PRODUCT_CONFIGURATION  
CATALOG_VERSION_OUTDATED  
CATALOG_NO_CHANGES_TO_PUBLISH
CATALOG_VALIDATION_FAILED
CATEGORY_NOT_FOUND
CATEGORY_HIERARCHY_CYCLE
PRODUCT_INTERNAL_CODE_ALREADY_EXISTS
VARIANT_INTERNAL_CODE_ALREADY_EXISTS
PRODUCT_TYPE_CONFIGURATION_MISMATCH
INVALID_INGREDIENT_RULE
INVALID_PIZZA_CONFIGURATION
INVALID_COMBO_CONFIGURATION
CATALOG_CONCURRENCY_CONFLICT
MEDIA_ASSET_NOT_FOUND
MEDIA_UPLOAD_INVALID
MEDIA_CHECKSUM_MISMATCH

## Ordering
CART_EMPTY  
ORDER_REQUIRES_REVIEW  
ORDER_ITEM_HAS_PENDING_REQUEST  
CHANGE_NOT_ALLOWED  
CANCELLATION_NOT_ALLOWED  
PRICE_INCREASE_REQUIRES_CONFIRMATION  

## Delivery
ORDER_ITEM_NOT_READY  
DELIVERY_ALREADY_CONFIRMED  
DELIVERY_NOT_PENDING  
DELIVERY_ALREADY_CONTESTED  

## Payments
INVALID_PAYMENT_AMOUNT  
PAYMENT_AMOUNT_EXCEEDS_AVAILABLE_BALANCE  
PAYMENT_RESERVATION_CONFLICT  
PAYMENT_METHOD_DISABLED  
PAYMENT_ATTEMPT_ALREADY_FINISHED  
PAYMENT_STATUS_UNKNOWN  
REFUND_NOT_ALLOWED  
CASH_CONFIRMATION_REQUIRED  

## Idempotency
IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST  

---

# 22. HTTP Status Guidelines

200 — leitura/ação concluída.  
201 — recurso criado.  
202 — operação externa em processamento.  
204 — ação sem payload.  
304 — menu não modificado.  
400 — payload/formato inválido.  
401 — não autenticado.  
403 — autenticado sem permissão/bloqueado.  
404 — recurso não encontrado no escopo.  
409 — conflito de estado/concorrência/idempotência.  
422 — configuração semanticamente inválida quando apropriado.  
429 — rate limit.  
500 — falha inesperada.  
503 — dependência temporariamente indisponível.

---

# 23. Concorrência

Endpoints que alteram estado devem usar:
- version/ETag quando apropriado;
- transaction;
- índices/constraints no banco;
- tratamento explícito de conflito.

Nunca usar apenas "consultar e depois inserir" para regras únicas críticas.

---

# 24. Segurança

Device token não pode:
- acessar outra mesa;
- acessar outro establishment;
- administrar catálogo;
- consultar usuários.

Funcionário só vê ações permitidas por RBAC e escopo.

CPF completo nunca aparece em payload normal do tablet após coleta.

---

# 25. OpenAPI

A implementação deve gerar OpenAPI/Swagger a partir do código.
Este documento é a especificação de negócio; OpenAPI deve refletir este contrato, não substituí-lo.
