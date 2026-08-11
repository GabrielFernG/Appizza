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
