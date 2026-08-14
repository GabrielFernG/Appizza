# ADR-024 — Confirmação e contestação de entrega

Status: Aceita  
Data: 2026-08-12

## Contexto

Enviar um item à mesa não prova que o cliente o recebeu. A entrega precisa admitir confirmação manual,
fallback automático e contestação limitada sem transformar SignalR em confirmação.

## Decisão

- Funcionário autorizado por `kitchen.delivery.send` envia o item à mesa; não há regra por nome de role.
- `ProductionItemSentToTable` leva o item a `awaiting_delivery_confirmation` e cria uma confirmação.
- Cliente, funcionário com `kitchen.delivery.confirm` ou Worker podem confirmar. Auto-confirmação é
  fallback após `delivery.auto_confirmation_minutes`, inicialmente 5 e configurável por estabelecimento.
- Worker e PostgreSQL processam prazos; não há scheduler externo.
- Confirmação manual explícita encerra o fluxo normal. Após auto-confirmação, o cliente pode contestar
  durante `delivery.auto_contestation_window_minutes`, inicialmente 5 e configurável.
- Contestação não cancela item, não altera preço e não refaz produto. Ela coloca a entrega em disputa.
  Funcionário com `kitchen.delivery.resolve` confirma a entrega ou determina nova tentativa operacional.
- Nova tentativa cria nova `DeliveryConfirmation` numerada; o histórico anterior permanece imutável.
- Locks e tokens de concorrência serializam confirmação automática/manual, contestação e resolução.

## Consequências

GET/reconciliação é fonte de verdade. SignalR somente invalida. Expiração, confirmação e contestação
continuam recuperáveis após restart do Worker.

