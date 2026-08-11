# ADR-013 — Autenticação, tenant e dispositivos

Status: Aceito.

Funcionários autenticam com `establishmentCode`, login e senha. `establishment.public_code` é único e
amigável; o servidor resolve `establishment_id`, e IDs do cliente nunca são autoridade de tenant.

Dispositivo nasce sem estabelecimento em `awaiting_configuration`. O bind atribui o tenant e essa
atribuição só muda após revogação/reset explícito. Funcionários e dispositivos usam access tokens
curtos e refresh tokens opacos, aleatórios, rotativos e armazenados somente como hash. Sessões de
dispositivo ficam em `devices.device_session`; `credential_version` invalida credenciais antigas.

Na Fase 1, RBAC opera somente no estabelecimento. A precedência é deny direto, allow direto, allows
das roles e deny implícito. Scopes mais granulares não têm semântica funcional nesta fase.
