# 10 — Arquitetura Técnica

Monólito modular.

Stack:
- .NET 10 / ASP.NET Core / EF Core / SignalR
- .NET MAUI + SQLite
- Vue 3 + TypeScript + Vite + Vuetify + Pinia
- PostgreSQL
- object storage acessado por abstração `IObjectStorage` compatível com S3

Em Development, o object storage local é SeaweedFS executado em container e acessado pela API S3.
O provider de produção não está definido. Tipos específicos do SeaweedFS não podem aparecer em
Domain ou Application.

REST para operações; SignalR para notificações.
Outbox/Inbox para confiabilidade.
BackgroundService/worker para jobs.
OpenTelemetry/logs estruturados/health checks.

Persistência:
- um `AppizzaDbContext` físico;
- um histórico central de migrations;
- configurações EF Core pertencem aos respectivos módulos;
- `version bigint` é token de concorrência e é incrementado pela camada de persistência/EF Core
  em atualizações relevantes, sem trigger PostgreSQL e sem incremento manual pelo domínio;
- isolamento multiestabelecimento usa contexto de estabelecimento, filtros/validações na aplicação
  e testes contra vazamento; PostgreSQL RLS não faz parte da Fundação.
- cada módulo mantém suas `IEntityTypeConfiguration<T>`;
- `Appizza.Persistence` é a composição autorizada a referenciar os módulos e aplicar seus mappings;
- módulos não dependem de `Appizza.Persistence`.

Mídia pertence ao módulo neutro `Media`, não a Communications. Metadados ficam no schema `media`;
os binários ficam no object storage.

Sem microsserviços, Kubernetes, broker ou Redis obrigatórios no MVP.

## Menu e estado local da Fase 3

O Appizza.Table usa SQLite exclusivamente para cache e estado local. A versão física usa
`PRAGMA user_version` e migrations incrementais transacionais. O cache ativo é instalado por troca
atômica e identificado por tenant, device, `schemaVersion`, `catalogVersion` e
`availabilityVersion`; carrinho inclui também a sessão. Mantêm-se, quando possível, catálogo atual e
imediatamente anterior apenas para recuperação.

Mídia é obtida por endpoint autenticado da API e armazenada com checksum e LRU. Limite inicial é 512
MB, configurável, sujeito a espaço livre mínimo do dispositivo. O tablet não conhece bucket, chave,
credenciais S3 ou SeaweedFS.

SignalR invalida; não transporta catálogo nem é fonte de verdade. O fluxo confiável é persistência,
Outbox, dispatcher, SignalR e reconciliação por GET. Startup, resume, foreground, reconexão e
revalidação periódica detectam mensagens perdidas.

## Decisões confirmadas pela validação da Fase 1

- O pipeline JWT preserva os nomes originais dos claims (`MapInboundClaims = false`), inclusive `sub`,
  `establishment_id`, `token_type`, `session_id` e `credential_version`.
- O customizador global de ProblemDetails adiciona `UNEXPECTED_ERROR` somente quando o endpoint não
  forneceu um `errorCode` de negócio.
- Bind e abertura/restauração de sessão serializam pela linha física relevante com `FOR UPDATE` e
  isolamento `ReadCommitted`; índices únicos parciais permanecem como defesa final de consistência.
- Substituição de dispositivo encerra o vínculo anterior, incrementa `credential_version`, revoga as
  sessões antigas e persiste `DeviceReplaced` pela Outbox na mesma transação.

## Ordering, Kitchen e Outbox multi-consumer da Fase 4

Ordering é autoridade da simulação, do preço final, do pedido e do snapshot histórico. Catalog
fornece a revisão publicada e a disponibilidade; Tables fornece a sessão e recebe seus totais na mesma
transação de submissão. Kitchen não participa dessa transação: consome `OrderSubmitted` depois do
commit e cria o intake operacional. Um `201 Created` significa pedido persistido e pode retornar
`pendingKitchenIntake`; SignalR nunca confirma persistência nem intake.

A Outbox possui registro único do evento, mas seu processamento é por consumidor registrado. Cada
consumidor grava sua conclusão em `integration.inbox_message(event_id, consumer_name)`. O dispatcher
ignora consumidores concluídos, reexecuta somente pendentes/falhos e preenche `outbox_message.processed_at`
apenas quando todos os consumidores registrados para o tipo concluíram. Assim, SignalR não pode
finalizar `OrderSubmitted` antes de Kitchen. Reinício entre efeito e finalização preserva idempotência;
notificações podem ser repetidas e os clientes reconciliam pela API.
