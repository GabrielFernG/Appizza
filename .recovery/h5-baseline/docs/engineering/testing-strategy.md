# Estratégia de Testes

## Unit
- preço pizza multi-sabores;
- adicionais por fração;
- promoções;
- transições;
- cancelamentos;
- saldo;
- autorização.

## Integration
PostgreSQL real em container:
- migrations;
- unique partial indexes;
- idempotência;
- Outbox/Inbox;
- concorrência;
- endpoints.

### Baseline aprovada da Fase 1

A cobertura funcional/API usa PostgreSQL 18.4 real via Testcontainers e sincronização determinística
das requisições concorrentes, sem `Task.Delay` como mecanismo de coordenação. Foram aprovados:
- dois binds simultâneos com capacidade e disputa pelo último slot disponível;
- tentativa simultânea de dois vínculos para o mesmo dispositivo;
- substituição concorrente com revogação das credenciais substituídas;
- revogação, bloqueio, desbloqueio e refresh concorrente de funcionário e dispositivo;
- leituras e escritas cross-tenant sem revelação nem alteração do recurso estrangeiro;
- `provide/provide` e `provide/skip` concorrentes para identificação do cliente;
- `open-or-get` retornando a mesma sessão, com uma única transição e um único evento na Outbox.

Regressões arquiteturalmente relevantes preservadas por essa suíte:
- desabilitar o remapeamento automático de claims JWT para manter `sub` e os claims próprios;
- códigos de negócio em ProblemDetails não podem ser sobrescritos pelo fallback global;
- operações serializadas por linha usam `FOR UPDATE` com isolamento compatível e verificam o estado
  físico e a Outbox depois do commit;
- violações de índice único esperadas em corridas devem ser traduzidas para o erro de negócio previsto.

## Contract
Providers de pagamento.

## Frontend
Componentes, stores e fluxos críticos.

### Baseline obrigatória da Fase 3

- ETag composto, 304 e overlay incremental de disponibilidade;
- PostgreSQL/Testcontainers para leitura consistente, device auth e cross-tenant;
- SQLite real para migrations, cache atômico, carrinho, restart e `session_mismatch`;
- offline com/sem cache, reconexão, resume e SignalR perdido/duplicado/fora de ordem;
- schema 1, versão futura e campos compatíveis desconhecidos;
- hash semântico determinístico e insensível a metadados técnicos;
- cache de mídia com checksum, falha, LRU, espaço crítico e SeaweedFS real;
- produtos, variantes, ingredientes, pizzas, Monte sua Pizza, combos e estimativa decimal;
- ausência de Order, simulação autoritativa, reserva ou fila offline.

## E2E
Cenário ouro:
abrir sessão -> pedido -> aceitar -> preparar -> entregar -> fechar -> pagar -> liberar.

Cenários de falha:
- resposta perdida após order commit;
- Payment Unknown;
- duplicate event;
- dois tablets abrindo sessão;
- dois pagamentos concorrentes;
- device revogado.

### Matriz obrigatória da Fase 4

- PostgreSQL/Testcontainers real para pricing, snapshots, idempotência, Outbox/Inbox e intake;
- concorrência coordenada sem `Task.Delay`: replay, payload divergente, clientSubmission duplicado,
  dois devices, Closing, revoke/block, publicação/disponibilidade concorrentes e aceite duplo;
- resposta perdida e timeout após commit reconciliados sem outro pedido;
- Outbox multi-consumer: sucesso, falha parcial, retry, concluído ignorado, evento duplicado e restart;
- snapshot permanece idêntico depois de arquivar/republicar o catálogo;
- pricing autoritativo para simples, configurável, pizza, multissabor, Monte sua Pizza e combos
  `fixed_price`/`calculated`, sem Promotions;
- cross-tenant para simulação, submissão, reconciliação, station, fila, detalhe e aceite;
- SQLite/MAUI para review, envio, `submission_unknown`, restart e reconciliação;
- Vue para FIFO, filtro por estação, detalhe, realtime/fallback e aceite;
- auditoria negativa de cancelamento, rejeição, preparo, pausa, Ready, entrega, Payments e
  Promotions.

### Matriz obrigatória da Fase 5

- unitários para todas as máquinas de estado, matriz por estágio, revisão imutável, pricing de alteração,
  confirmação versionada e algoritmo de status público com combinações heterogêneas;
- API/PostgreSQL 18.4/Testcontainers para lifecycle, attempts, pausas, requests, revisões, totais,
  rejeição Kitchen, entrega, constraints, índices e rollback sem efeitos parciais;
- concorrência determinística, sem `Task.Delay` como mecanismo principal: start/pause/resume/ready,
  cancel/change contra produção, review obsoleto, duas decisões, rejeição contra cancelamento, locks na
  ordem global, confirmação manual×auto, contestação×confirmação/resolução e Worker concorrente/restart;
- idempotência endpoint por endpoint: replay exato, payload divergente e chave distinta após transição;
- Outbox/Inbox multi-consumer: falha parcial, retry seletivo, duplicata, dois workers e rejeição Kitchen
  produzindo exatamente um cancelamento comercial;
- cross-tenant e RBAC para toda query/mutação, incluindo ausência de mutação quando 403/404;
- imutabilidade do snapshot original e de cada revisão após Catalog mudar/arquivar/republicar;
- MAUI/SQLite para status/reconciliação, requests, review versionado, restart, offline sem fila de mutação,
  entrega/contestação e SignalR perdido/duplicado/fora de ordem;
- Vue para FIFO, lifecycle, decisões explícitas, permissions, requests, entrega/contestação e fallback GET;
- arquitetura: fronteiras Ordering/Kitchen, nenhum acesso indevido, permission em vez de role, nenhum
  Payments/Promotions/Occurrence/Closing/scope por estação/prioridade/reordenação;
- migrations desde zero e upgrade Fase 4→5, EF sem model changes pendentes, builds .NET/MAUI/Vue.
