# ADR-018 — Menu publicado, cache e versões

Status: Aceito.

O menu do tablet é a composição local da `CatalogRevision` publicada com o overlay operacional de
disponibilidade. Estrutura/preços usam `catalogVersion`; disponibilidade usa
`availabilityVersion`; o contrato inicial usa `schemaVersion = 1`. O ETag semântico contém os três
valores. Alteração apenas de disponibilidade usa endpoint incremental e não baixa novamente a
estrutura inteira; o menu completo permanece fallback de reconciliação.

SQLite é cache/local state, segregado por tenant e device. Payload de versão desconhecida não
substitui cache compatível. SignalR somente invalida, e a API/PostgreSQL permanecem fonte de verdade.

Configuração de item usa SHA-256 sobre JSON canônico conforme `appizza-config-v1`, sem timestamps,
auditoria ou detalhes físicos do banco. O identificador detecta mudanças sem transformar o tablet em
autoridade comercial.
