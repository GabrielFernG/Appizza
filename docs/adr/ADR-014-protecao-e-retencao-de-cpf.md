# ADR-014 — Proteção e retenção de CPF

Status: Aceito.

CPF opcional de sessão é cifrado com AES-256-GCM. A chave fica fora do banco e do repositório. Quando
comparação for necessária, usar HMAC-SHA256 com chave separada. CPF completo nunca é retornado,
logado, enviado em evento ou exposto por endpoint de decriptação na Fase 1.

A coleta em produção exige retenção configurada pelo estabelecimento. O Worker anonimiza conteúdo
cifrado e hash após o prazo, preservando metadados não sensíveis e o histórico operacional.

O estado da etapa fica em `table_session`: `pending`, `provided` ou `skipped`. Corridas admitem um
vencedor. Reenvio do mesmo CPF após `provided` é idempotente; valor diferente é conflito.
