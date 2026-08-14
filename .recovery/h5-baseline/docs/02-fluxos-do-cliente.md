# 02 — Fluxos do Cliente

## Entrada
Tablet vinculado -> Bem-vindo -> Fazer pedido -> abrir ou restaurar sessão.

## Identificação
CPF opcional de um responsável, com finalidade apresentada, proteção e opção de pular.

## Cardápio
Página vertical, categorias como âncoras, produtos em carrosséis horizontais.

Na Fase 3, o tablet combina localmente a revisão publicada com o overlay de disponibilidade. Com
cache compatível, o menu e o carrinho local continuam utilizáveis durante perda temporária de rede;
sem cache compatível, a interface apresenta indisponibilidade e retry, sem inventar catálogo. Após
reconexão, o dispositivo valida credencial, vínculo e sessão, reconcilia versões e marca escolhas que
exigem revisão, sem removê-las ou substituí-las silenciosamente.

## Carrinho
Local por dispositivo até o envio. Outros tablets da mesma mesa só veem pedidos enviados.

O carrinho pertence à sessão atual e persiste IDs, configuração e versões usadas. Carrinho de sessão
anterior permanece `session_mismatch`, nunca se torna ativo automaticamente e é retido localmente por
sete dias. Valores locais são apenas estimativas; simulação e envio autoritativos começam na Fase 4.

## Simulação e envio da Fase 4

Ao avançar, o tablet envia a intenção para simulação autoritativa. Se preço, configuração ou
disponibilidade materialmente divergirem, preserva as escolhas, destaca os itens e exige review da
versão exata. O envio usa simulação válida, `clientSubmissionId` e `Idempotency-Key`.

`201 Created` confirma o pedido, ainda que o intake de Kitchen esteja pendente. Em timeout/resposta
perdida, o carrinho entra em `submission_unknown` e reconcilia pela chave; nunca cria nova submissão
automaticamente. Depois da confirmação, o carrinho local somente é marcado como submetido pela
resposta/reconciliação da API, não por SignalR.

## Pedido
Simulação server-side -> revisão -> submissão idempotente -> snapshot -> produção.

## Status
Cards exibem status/substatus públicos derivados. O detalhe mostra cada item, composição histórica e
atenção pendente. Estado Kitchen não é copiado para `Order.status`; SignalR apenas solicita GET.

## Alteração/cancelamento
Requests da Fase 5 são somente cancelamento integral ou alteração de item. Antes do preparo podem ser
automáticos; em preparo/pausa dependem de Kitchen; Ready exige gerente para cancelamento e não aceita
alteração. Aumento exige confirmação da versão exata; redução não. O snapshot original nunca muda.

## Entrega
Funcionário autorizado envia à mesa. Cliente, funcionário autorizado ou fallback automático confirmam.
Auto-confirmação e janela posterior de contestação têm defaults de 5 minutos, configuráveis. Contestação
reabre a entrega operacional e não cancela nem altera preço.

## Fechamento
Bloqueia novos pedidos. Pode voltar enquanto não houver pagamento aprovado.

## Pagamento
Total, participantes, itens, valor ou divisão igual; Pix, cartão, SoftPOS, dinheiro.

## Pós-sessão
Liberação imediata ou AwaitingCleaning conforme configuração.
