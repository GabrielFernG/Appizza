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
