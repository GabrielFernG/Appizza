# Pedido e Produção

```mermaid
stateDiagram-v2
  [*] --> AwaitingAcceptance
  AwaitingAcceptance --> Accepted
  Accepted --> AwaitingPreparation
  AwaitingPreparation --> InPreparation
  InPreparation --> Paused
  Paused --> InPreparation
  InPreparation --> Ready
  Ready --> AwaitingDeliveryConfirmation
  AwaitingDeliveryConfirmation --> Delivered
  AwaitingAcceptance --> Cancelled
  Accepted --> Cancelled
  AwaitingPreparation --> Cancelled
  InPreparation --> Cancelled
  Ready --> Cancelled: gerente
```
