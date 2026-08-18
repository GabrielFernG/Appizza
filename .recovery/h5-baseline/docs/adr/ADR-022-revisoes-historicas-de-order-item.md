# ADR-022 — Revisões históricas de OrderItem

Status: Aceita  
Data: 2026-08-12

## Contexto

O snapshot criado na submissão da Fase 4 precisa permanecer imutável, mas a Fase 5 permite alterar a
configuração comercial do mesmo `OrderItem`. Sobrescrever o snapshot ou criar outro item destruiria a
identidade e a interpretação histórica do que foi vendido.

## Decisão

- `ordering.order_item.snapshot` permanece invariavelmente imutável e representa a revisão original.
- Alterações efetivadas criam `ordering.order_item_revision`; nunca criam outro `OrderItem`.
- `OrderItem.current_revision_number` identifica a configuração comercial efetiva. O valor `0`
  referencia o snapshot e os valores originais do próprio item; revisões posteriores são numeradas a
  partir de `1` e são únicas por item.
- Cada revisão posterior referencia o `OrderItemRequest` que a originou e preserva snapshot JSONB,
  configuração, valores anteriores e novos, diferença financeira, ator/origem e timestamps.
- Uma revisão é append-only: depois de efetivada, não é atualizada nem removida. Request rejeitado,
  expirado ou retirado não cria revisão.
- Catalog valida a nova intenção contra revisão publicada, configuração, disponibilidade e pricing
  atuais. O catálogo atual nunca reinterpreta nem apresenta o snapshot original.
- A efetivação da revisão, atualização do ponteiro corrente, valores do item/pedido/sessão, históricos
  e Outbox ocorre em uma transação Ordering.
- A materialização é incremental: `Phase5_OrderingRequests` contém requests/cancellation do Checkpoint D;
  `Phase5_OrderItemRevisions` adiciona no Checkpoint E a tabela de revisões e o ponteiro do item. A
  migration anterior, já aplicada, não é reescrita.

## Consequências

Consultas correntes usam a revisão efetiva; consultas históricas podem reconstruir toda a sequência sem
Catalog. Há armazenamento duplicado deliberado: JSONB preserva a representação histórica integral e
estruturas relacionais suportam consultas e integração operacional.
