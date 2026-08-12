# ADR-017 — Lifecycle e upload de mídia

Status: Aceito.

Media é proprietário de `media.asset`; Catalog apenas referencia assets do mesmo estabelecimento.
Estados: `pending_upload -> ready | failed` e `ready -> archived`. Não há thumbnails ou estado
intermediário de processamento na Fase 2.

Upload valida ownership, MIME permitido, tamanho máximo, checksum e segurança básica da chave/nome.
Checksum não implica deduplicação automática. Um asset pode ser reutilizado no mesmo tenant, nunca
cross-tenant, e não pode ser apagado fisicamente quando referenciado por revisão publicada.

O acesso ao arquivo usa `IObjectStorage` compatível com S3. SeaweedFS permanece detalhe exclusivo do
Development e seus tipos não podem aparecer em Catalog ou Media.
