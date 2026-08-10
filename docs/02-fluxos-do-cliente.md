# 02 — Fluxos do Cliente

## Entrada
Tablet vinculado -> Bem-vindo -> Fazer pedido -> abrir ou restaurar sessão.

## Identificação
CPF opcional de um responsável, com finalidade apresentada, proteção e opção de pular.

## Cardápio
Página vertical, categorias como âncoras, produtos em carrosséis horizontais.

## Carrinho
Local por dispositivo até o envio. Outros tablets da mesma mesa só veem pedidos enviados.

## Pedido
Simulação server-side -> revisão -> submissão idempotente -> snapshot -> produção.

## Status
Cards de pedido exibem status e substatus consolidados.
Card clicável mostra status individual de cada item e composição.

## Alteração/cancelamento
Regra varia com estágio operacional.

## Entrega
Confirmação por cliente/garçom/automática conforme configuração.

## Fechamento
Bloqueia novos pedidos. Pode voltar enquanto não houver pagamento aprovado.

## Pagamento
Total, participantes, itens, valor ou divisão igual; Pix, cartão, SoftPOS, dinheiro.

## Pós-sessão
Liberação imediata ou AwaitingCleaning conforme configuração.
