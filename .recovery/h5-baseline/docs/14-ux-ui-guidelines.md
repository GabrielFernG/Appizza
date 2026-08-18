# 14 — UX/UI Guidelines

Figma é referência visual, não especificação.

Tablet:
- responsivo;
- carrosséis horizontais;
- categorias como âncoras;
- ações críticas visíveis;
- modo quiosque;
- cache de mídia.

Fase 3:
- produto indisponível permanece visível, marcado e não selecionável;
- categoria sem produto efetivamente visível é ocultada;
- mudança durante configuração não fecha a tela nem escolhe alternativa: destaca a seleção e exige
  revisão;
- estado offline e instante da última sincronização são perceptíveis;
- sem cache compatível, mostrar indisponibilidade/retry;
- preços do menu/carrinho são identificados como estimativas até validação do servidor;
- mídia ausente usa placeholder e não bloqueia navegação.

Status:
card resumido clicável -> detalhamento por item.

Cozinha:
poucos toques, alta legibilidade, ordem de fila clara.

Fase 5 — Appizza.Table:
- cards usam status público derivado e detalhe histórico; nunca exibem enum interno cru da Kitchen;
- cancelamento/alteração preserva a intenção, mostra autoridade/estado pendente e não promete conclusão
  por SignalR;
- aumento mostra valor anterior, novo e diferença e exige confirmação da versão exata; redução informa o
  total atualizado sem fluxo financeiro;
- entrega oferece confirmar/contestar apenas dentro do estado e prazo retornados pela API;
- offline permite consultar estado previamente reconciliado, mas não enfileira mutações de request ou
  entrega; ações exigem conexão e reconciliação.

Fase 5 — Appizza.Operations:
- fila FIFO sem drag-and-drop, prioridade manual ou scope por estação;
- ações explícitas start, pause/resume, restart, Ready, reject e send-to-table conforme permission;
- decisão de alteração em produção exige escolha visível `continue`, `restart` ou `reject`;
- requests, rejeições e contestações destacam atenção necessária; fallback GET preserva correção quando
  SignalR falhar.

Acessibilidade MVP:
fonte maior, contraste, áreas de toque adequadas, não depender apenas de cor.
