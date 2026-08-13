# ADR-021 — Outbox multi-consumer e intake eventual de Kitchen

Status: Aceita  
Data: 2026-08-12

## Contexto

`OrderSubmitted` possui mais de um consumidor. Marcar a Outbox após o primeiro efeito pode perder o
intake da cozinha; reexecutar todos pode duplicar efeitos.

## Decisão

- O registro da Outbox é compartilhado, mas cada consumidor registrado conclui individualmente em
  `integration.inbox_message(event_id, consumer_name)`.
- `processed_at` da Outbox é preenchido somente quando todos os consumidores registrados para o tipo
  concluíram. Retry ignora os concluídos e executa os pendentes/falhos.
- O consumidor Kitchen cria exatamente um ProductionItem por OrderItem; uniques e Inbox protegem
  evento duplicado e workers concorrentes. Outro consumidor publica a invalidação SignalR, que não
  pode finalizar o evento antes de Kitchen.
- `POST /orders` termina antes do intake e pode responder `pendingKitchenIntake`. A API, não SignalR,
  é a fonte para reconciliação.
- Station é entidade de Kitchen. Uma estação específica é resolvida por contrato no mesmo tenant,
  sem FK de Catalog para Kitchen; na ausência usa-se a estação ativa default.
- Todo item gera intake. ProductionItem nasce em `awaiting_acceptance`; o aceite registra `accepted`
  e termina em `awaiting_preparation`. Fluxos posteriores não são implementados nesta fase.

## Consequências

Entrega de notificação continua at-least-once e clientes precisam reconciliar. O registro por
consumidor torna falha parcial e restart recuperáveis sem repetir efeitos persistidos.
