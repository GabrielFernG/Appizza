# ADR-025 — Locking, idempotência e concorrência da Fase 5

Status: Aceita  
Data: 2026-08-12

## Contexto

Requests, produção e entrega atualizam agregados relacionados e podem concorrer com ações do cliente,
Kitchen e Worker. Uma ordem inconsistente de locks produziria deadlocks ou efeitos parciais.

## Decisão

- Ordem global: `TableSession -> Order -> OrderItem -> OrderItemRequest -> ProductionItem ->
  DeliveryConfirmation -> DeliveryContest`. Ao bloquear subconjuntos, preserva-se a ordem relativa.
- Validações demoradas de Catalog ocorrem antes dos locks quando possível; antes do commit são
  revalidados sob os locks os estados e versões capazes de mudar a decisão.
- Mutações documentadas exigem `Idempotency-Key`, com identidade
  `(establishment_id, operation_type, idempotency_key)`.
- Mesma chave e payload reproduz exatamente status e payload persistidos. Mesma chave e payload
  diferente retorna `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST`.
- Chave diferente depois de transição efetiva retorna conflito específico; sucesso convergente só é
  permitido quando explicitamente indicado pelo endpoint.
- Resultado de sucesso ou conflito determinístico é persistido na mesma fronteira transacional da
  decisão. Falha transitória/5xx não é registrada como resultado final.
- Novos eventos críticos preservam Outbox multi-consumer e Inbox por consumidor da ADR-021.

## Consequências

Races obrigatórios são testados com PostgreSQL real e coordenação determinística. Locks não atravessam
chamadas externas nem consultas longas ao Catalog.

