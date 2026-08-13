# 05 — Mesas, Sessões e Dispositivos

DiningTable representa o ponto físico.
TableSession representa um atendimento.

Uma mesa não pode possuir mais de uma sessão ativa.

Sessão abre em modo `on_start_ordering` ao tocar em "Fazer pedido".

Uma mesa pode ter múltiplos tablets, limitados por:
`devices.max_active_table_devices_per_table`.

Todos compartilham sessão/pedidos/conta/status, mas mantêm carrinho local independente.

Device usa identidade própria após configuração por funcionário.

Offline:
menu/cache e carrinho funcionam; envio de pedido e pagamento exigem conexão.

Liberação:
- Immediate
- AfterCleaningConfirmation

No segundo modo, mesa fica AwaitingCleaning e só volta a Available após confirmação de funcionário.

## Decisões da Fase 1

- O modo de abertura é `on_start_ordering`: a sessão abre ao tocar em "Fazer pedido".
- `devices.device.establishment_id` fica nulo enquanto o dispositivo está `awaiting_configuration`.
- O estabelecimento é atribuído no vínculo e só muda após revogação/reset explícito.
- Refresh tokens de dispositivo pertencem a `devices.device_session`, são rotativos, opacos e persistidos somente como hash.
- `table_session.customer_identification_status` é `pending`, `provided` ou `skipped`, com instante de resolução.
- Reenvio do mesmo CPF após `provided` é idempotente; valor diferente falha com `CUSTOMER_IDENTIFICATION_ALREADY_RESOLVED`.
- Corridas entre fornecer e pular identificação admitem um único vencedor.
- O número da sessão usa sequence PostgreSQL; a apresentação combina a data local do estabelecimento e o valor da sequence. Gaps são aceitos.
- Retenção de CPF é configuração obrigatória para coleta em produção; o Worker anonimiza dados expirados.
- `SessionTransfer` permanece fora da Fase 1.

## Decisões da Fase 3

Cache e carrinho locais são segregados por estabelecimento, dispositivo e, para carrinho, sessão.
Reset, revogação ou reconfiguração tornam dados do contexto anterior imediatamente inacessíveis ao
fluxo ativo. Carrinho antigo pode ser retido por sete dias para recuperação/diagnóstico, sempre como
`session_mismatch` e sem possibilidade de envio automático. Offline permite navegação e edição local,
mas não simulação autoritativa, reserva ou envio de pedido.
