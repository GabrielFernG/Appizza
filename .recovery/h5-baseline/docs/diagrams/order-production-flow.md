# Pedido e Produção

```mermaid
stateDiagram-v2
  [*] --> AwaitingAcceptance
  AwaitingAcceptance --> Accepted
  Accepted --> AwaitingPreparation
  AwaitingPreparation --> InPreparation
  InPreparation --> Paused
  Paused --> InPreparation
  InPreparation --> Rejected
  Paused --> Rejected
  InPreparation --> Ready
  Ready --> AwaitingDeliveryConfirmation
  AwaitingDeliveryConfirmation --> Delivered
  Delivered --> AwaitingDeliveryConfirmation: contestação após auto-confirmação
  AwaitingAcceptance --> Cancelled
  Accepted --> Cancelled
  AwaitingPreparation --> Cancelled
  InPreparation --> Cancelled
  Paused --> Cancelled
  Ready --> Cancelled: gerente
```

`Rejected` é consequência operacional; Ordering cancela comercialmente por evento idempotente. A seta
de `Delivered` só existe durante a janela configurada após auto-confirmação, nunca após confirmação manual.
