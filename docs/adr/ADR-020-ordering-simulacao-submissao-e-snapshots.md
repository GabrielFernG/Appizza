# ADR-020 — Simulação, submissão idempotente e snapshots de Ordering

Status: Aceita  
Data: 2026-08-12

## Contexto

O carrinho do tablet é estado local e seus valores são estimativas. A criação de um pedido precisa
resistir a mudanças de catálogo/disponibilidade, double tap, timeout e resposta perdida, sem confiar em
preços enviados pelo cliente e sem alterar o histórico quando o catálogo evoluir.

## Decisão

- `/cart/simulate` recalcula a intenção no servidor e persiste por prazo curto em
  `ordering.cart_simulation`, com request hash, versões, `simulationVersion`, review e snapshots JSONB.
- A validade inicial é 300 segundos, configurável em `ordering.simulation_validity_seconds` por
  estabelecimento. Simulação não reserva nada e submissão sempre revalida definitivamente.
- Review usa a tupla exata `simulationId`, `simulationVersion`, `acceptedReview`; diferença apenas de
  contador atualiza versões, mas somente diferença material exige nova confirmação.
- `POST /orders` exige device/vínculo válidos, sessão Open, `Idempotency-Key` e
  `clientSubmissionId`. A identidade lógica da chave é
  `(establishment_id, operation_type, idempotency_key)` e a submissão também é única por
  `(establishment_id, source_device_id, client_submission_id)`.
- O servidor usa a intenção/configuração e ignora todo valor monetário do cliente no cálculo.
- A transação persiste Order, itens, configurações estruturadas, snapshot imutável, totais da
  sessão, `OrderSubmitted` na Outbox e resultado de idempotência.
- `order_number` vem de sequence PostgreSQL global, admite gaps e não é número fiscal.
- O snapshot registra schema, revisão e versões, nomes, preços, quantidade, ingredientes,
  opções, pizza/frações/Monte sua Pizza/massa/borda, combo/seleções, observações e política de
  cálculo. Nunca é reconstruído do catálogo atual.

Combos suportam apenas `fixed_price` e `calculated`; descontos permanecem fora. Pizzas usam divisões
iguais, média aritmética com precisão intermediária e arredondamento monetário final
`AwayFromZero`. Uma parte montada do zero contribui com seu preço integral de referência no tamanho.

## Consequências

Há armazenamento temporário adicional e limpeza posterior, mas reconciliação e auditoria ficam
determinísticas. Promotions, Payments e transições comerciais posteriores não participam da Fase 4.
