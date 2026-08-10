# 05 — Mesas, Sessões e Dispositivos

DiningTable representa o ponto físico.
TableSession representa um atendimento.

Uma mesa não pode possuir mais de uma sessão ativa.

Sessão abre automaticamente em "Fazer pedido".

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
