# 06 — Usuários e Permissões

Perfis padrão: Administrador, Gerente, Caixa, Cozinha, Garçom, Atendente.

Usuário pode ter múltiplos perfis, grants extras e denies.

Permissões podem ter escopo por estabelecimento, setor, estação, categoria ou recurso.

PIN de 6 dígitos pode reautorizar ações sensíveis.

Modo Aprovação:
usuário sem permissão solicita; gerente autoriza temporariamente; ação é auditada sem conceder permissão permanente.

Backend sempre valida.

## Decisões da Fase 1

O login recebe `establishmentCode`, `login` e `password`. O servidor resolve o estabelecimento; nenhum ID enviado pelo cliente é autoridade de tenant.

Na Fase 1, permissões têm somente escopo de estabelecimento. A precedência é: deny direto do usuário, allow direto do usuário, allows das roles e deny implícito.

Matriz inicial:
- Administrador e Gerente: todas as permissões da Fase 1;
- Atendente: visualizar mesas e configurar dispositivos;
- Garçom: visualizar mesas/sessões e confirmar limpeza;
- Caixa: visualizar mesas e sessões;
- Cozinha: nenhuma permissão administrativa da Fase 1.

Senha e PIN usam `PasswordHasher<TUser>`. PIN tem limitação de tentativas. Access tokens são curtos; refresh tokens são opacos, rotativos e armazenados somente como hash. `TemporaryApproval` permanece fora da Fase 1.

## Matriz incremental da Fase 4

Permissões no escopo do estabelecimento:
- `kitchen.queue.view`: consultar estações e fila;
- `kitchen.production.view`: consultar detalhe de ProductionItem;
- `kitchen.production.accept`: aceitar ProductionItem aguardando aceite.

Administrador e Gerente recebem as três permissões. Cozinha recebe as três. Os demais perfis não
as recebem por padrão. A precedência RBAC e o isolamento por estabelecimento definidos na Fase 1
continuam obrigatórios.

## Matriz incremental planejada da Fase 5

Permissions no escopo do estabelecimento:
- `kitchen.production.start`, `kitchen.production.pause`, `kitchen.production.resume`;
- `kitchen.production.fail`, `kitchen.production.restart`, `kitchen.production.ready`;
- `kitchen.production.reject` (reservada ao checkpoint que implementar rejeição);
- `kitchen.order_item_request.decide`;
- `ordering.order_item_request.view`, `ordering.order_item_request.decide`;
- `ordering.order_item.cancel_ready`;
- `kitchen.delivery.send`, `kitchen.delivery.confirm`, `kitchen.delivery.resolve`.

Seeds default planejados: Administrador e Gerente recebem todas; Cozinha recebe lifecycle, decisão
operacional, envio e confirmação; Garçom recebe visualização aplicável, envio e confirmação; Atendente
recebe visualização/decisão comercial conforme política. Caixa não recebe novas permissões por padrão.
`kitchen.delivery.resolve` e `ordering.order_item.cancel_ready` ficam por padrão com Administrador/Gerente.
São atribuições alteráveis; autorização nunca compara nome de role. Não há scope por estação na Fase 5.
