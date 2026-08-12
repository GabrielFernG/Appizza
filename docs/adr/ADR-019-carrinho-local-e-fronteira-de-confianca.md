# ADR-019 — Carrinho local e fronteira de confiança

Status: Aceito.

Na Fase 3, o carrinho pertence ao dispositivo e à sessão e é persistido somente no SQLite. Mantém IDs,
configuração, hashes e versões necessários à futura revalidação. Carrinho de sessão anterior nunca é
reativado automaticamente; fica `session_mismatch` e pode ser retido por sete dias.

Cálculo local serve apenas à UX. Usa `decimal`, precisão intermediária e apresentação em duas casas
com `MidpointRounding.AwayFromZero`. Não existem simulação autoritativa, Order, reserva, fila offline
ou envio de pedido nesta fase. Toda operação comercial futura exige conexão e validação do servidor.
