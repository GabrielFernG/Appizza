# ADR-023 — Lifecycle de produção, requests e fronteira Ordering–Kitchen

Status: Aceita  
Data: 2026-08-12

## Contexto

Cancelamento, alteração e rejeição operacional afetam Ordering e Kitchen, mas nenhum módulo pode
assumir silenciosamente a autoridade do outro.

## Decisão

- Na Fase 5, `requests` significa somente cancelamento ou alteração integral de `OrderItem`.
- Ordering é autoridade do item comercial, revisões, preço e totais. Kitchen é autoridade do
  `ProductionItem`, tentativas, pausas e decisão operacional.
- Antes de `in_preparation`, cancelamento e alteração válida podem ser efetivados automaticamente;
  aumento de preço continua exigindo confirmação versionada do cliente.
- Em `in_preparation` ou `paused`, Kitchen decide explicitamente `continue`, `restart` ou `reject` para
  alteração. Em `ready`, cancelamento exige decisão gerencial e alteração é proibida.
- Rejeitar produção emite `ProductionItemRejected`. Um consumidor Ordering idempotente cria e efetiva
  exatamente um cancelamento comercial com origem `kitchen_rejection`; Kitchen não altera preço nem
  totais.
- Cancelamento é sempre do item inteiro. `OrderItem.partially_cancelled` não é produzido na Fase 5;
  `Order.partially_cancelled` ocorre quando há itens cancelados e ativos.
- FIFO permanece; prioridade manual, reordenação e scope por estação ficam fora.

## Consequências

A consistência entre módulos é eventual e auditável. Durante o intervalo após rejeição operacional, o
read model público mostra `attention_required` até a consequência comercial ser persistida.

