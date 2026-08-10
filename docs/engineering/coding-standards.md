# Coding Standards

## C#
- nullable enabled;
- async/await end-to-end;
- CancellationToken em I/O;
- records para contracts imutáveis quando adequado;
- Value Objects para dinheiro/CPF/IDs apenas quando reduzirem erro real;
- evitar primitives mágicas no domínio crítico;
- exceptions não controlam fluxo esperado;
- endpoints traduzem resultados de aplicação para HTTP.

Naming:
`SubmitOrderCommand`, `GetPublishedMenuQuery`, `OrderSubmittedIntegrationEvent`.

## Vue/TS
- TypeScript strict;
- componentes pequenos;
- Pinia apenas para estado compartilhado;
- composables para comportamento reutilizável;
- API client central;
- tipos de API gerados ou sincronizados;
- não duplicar regra de preço no frontend como autoridade.

## SQL/EF
- snake_case no PostgreSQL;
- PascalCase nas entidades;
- nomes de entidade coerentes com tabela quando possível;
- migrations descritivas;
- índices declarados explicitamente.

## Logging
Structured logging.
Campos:
CorrelationId, EstablishmentId, UserId/DeviceId, Module, Operation.
Sem dados sensíveis.
