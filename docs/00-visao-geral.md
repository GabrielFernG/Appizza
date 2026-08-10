# 00 — Visão Geral

Appizza é uma plataforma presencial de autoatendimento e operação para pizzarias/restaurantes.

Componentes:
- Appizza.Table — .NET MAUI para tablets de mesa.
- Appizza.Operations — Vue 3 para cozinha, caixa e administração.
- Appizza.Api — ASP.NET Core.
- Appizza.Worker — jobs e integração assíncrona.

Fluxo principal:
configuração do tablet -> sessão automática -> identificação opcional -> cardápio -> carrinho -> simulação -> pedido -> cozinha -> status -> entrega -> fechamento -> divisão -> pagamento -> encerramento -> limpeza opcional -> nova sessão.

O MVP é presencial. Delivery, fidelidade, fiscal completo, marketplace e expedição dedicada ficam para evolução.
