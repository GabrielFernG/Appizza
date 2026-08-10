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
