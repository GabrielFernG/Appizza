# 04 — Cozinha

ProductionItem é por item.

Estados:
AwaitingAcceptance -> Accepted -> AwaitingPreparation -> InPreparation -> Paused/Resume -> Ready -> AwaitingDeliveryConfirmation -> Delivered.
Cancelled é terminal.

Todo item exige aceite.
FIFO por padrão, reordenação manual auditada.
Pausa exige motivo.
Falha de preparo cria nova ProductionAttempt.

Cliente cancela/altera:
- antes do preparo: automático;
- em preparo/pausado: cozinha aprova;
- pronto: gerente aprova.

Cozinha pode cancelar por motivo operacional e notificar mesa.
Disponibilidade de ingrediente/produto/variação/estação pode ser alterada.

## Recorte implementável da Fase 4

Todo OrderItem, inclusive item sem produção física, entra no intake operacional e gera exatamente um
ProductionItem. `requires_production` diferencia apenas o fluxo posterior; nunca remove o item da visão
operacional.

Cada estabelecimento possui uma estação ativa default. A estação específica publicada pelo Catalog
é resolvida por contrato, sem FK Catalog -> Kitchen e sempre no mesmo tenant; se ausente ou inválida,
usa-se a default. A submissão falha somente quando nenhuma estação ativa pode receber o item.

Na Fase 4, ProductionItem nasce em `awaiting_acceptance`. O aceite registra a passagem por `accepted`,
emite `ProductionItemAccepted` e conclui a operação em `awaiting_preparation`. Apenas a fila FIFO,
estação, detalhe, atualização em tempo real com fallback e aceite integram a UI operacional.
Rejeição, prioridade/reordenação, preparo, pausa, Ready e entrega permanecem fora.
