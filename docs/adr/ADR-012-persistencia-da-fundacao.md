# ADR-012 — Persistência da Fundação

Status: Aceito.

O Appizza usa um `AppizzaDbContext` físico e um único histórico central de migrations. Cada módulo
continua proprietário de suas configurações EF Core.

A migration `Foundation` cria somente os schemas e as tabelas técnicas de Outbox, Inbox e
idempotência. O restante do modelo conceitual entra nas fases funcionais correspondentes.

`version bigint` é token de concorrência incrementado pela camada de persistência/EF Core em
atualizações relevantes. Não há trigger PostgreSQL nem incremento manual obrigatório no domínio.

Na Fundação, o isolamento multiestabelecimento usa contexto de estabelecimento, filtros e validações
de aplicação, acompanhado de testes contra vazamento. PostgreSQL RLS não será implementado nesta fase.
