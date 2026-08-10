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
