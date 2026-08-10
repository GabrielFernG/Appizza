# AGENTS.md — Guia obrigatório para Codex e outros agentes

## 1. Antes de qualquer alteração

Leia:
- `README.md`;
- este arquivo;
- o documento funcional do módulo afetado;
- `docs/10-arquitetura-tecnica.md`;
- `docs/11-modelo-de-dados.md`;
- `docs/12-eventos.md`;
- `docs/13-contratos-api.md`;
- ADRs relacionadas;
- `docs/engineering/coding-standards.md`;
- `docs/engineering/testing-strategy.md`.

## 2. Fonte de verdade

Não invente regras quando a documentação já define o comportamento.
Se houver ambiguidade real, registre-a no plano antes de implementar.

## 3. Arquitetura

- Monólito modular.
- ASP.NET Core no backend.
- Vue 3 + TypeScript no painel.
- .NET MAUI no tablet.
- PostgreSQL como fonte de verdade.
- SQLite apenas como cache/local state do tablet.
- SignalR para notificação, nunca como fonte de verdade.
- Outbox para eventos críticos.
- Inbox/idempotência nos consumidores.
- Sem microsserviços, Kafka, Redis ou Kubernetes por padrão.

## 4. Módulos

Respeite os limites:
- Establishments
- Identity
- Catalog
- Ordering
- Kitchen
- Tables
- Payments
- Promotions
- Media
- Communications
- Devices
- Operations
- Reporting
- Auditing
- Integration

Um módulo não deve consultar diretamente tabelas privadas de outro módulo se existir contrato/evento apropriado.

## 5. Banco

- UUID para chave técnica.
- `numeric(14,2)` para dinheiro.
- UTC em timestamps técnicos.
- `establishment_id` em dados da unidade.
- enums estruturais como string + constraint.
- snapshots históricos em JSONB quando documentado.
- não apagar histórico operacional.
- não criar GenericRepository.
- não esconder EF Core atrás de abstração sem benefício.

## 6. API

- `/api/v1`.
- endpoints orientados a ações de negócio.
- evitar `PATCH status = ...`.
- ProblemDetails + `errorCode`.
- Idempotency-Key obrigatório nas operações documentadas.
- autorização sempre no backend.

## 7. Eventos

- fatos no passado.
- payload versionado.
- CorrelationId e CausationId.
- dados sensíveis nunca em payload.
- consumidores idempotentes.
- eventos críticos passam por Outbox.

## 8. Segurança

Nunca logar:
- senha;
- PIN;
- access/refresh token;
- CPF completo;
- dados completos de cartão;
- payload financeiro não mascarado.

## 9. Código

- nomes claros;
- uma responsabilidade por caso de uso;
- Commands alteram estado;
- Queries consultam;
- regras de domínio críticas testadas;
- comentários explicam o porquê, não o óbvio;
- evitar classes "Manager", "Helper" ou "Utils" genéricas sem responsabilidade definida.

## 10. Processo por fase

Antes de implementar:
1. informe a fase e documentos lidos;
2. liste decisões já tomadas;
3. liste arquivos a criar/alterar;
4. liste riscos;
5. implemente em passos pequenos;
6. compile;
7. execute testes;
8. atualize docs se necessário;
9. entregue resumo objetivo.

## 11. Proibido

- alterar regra documentada silenciosamente;
- criar serviço pago sem aprovação;
- tornar Figma autoridade sobre regra funcional;
- duplicar regra de preço no frontend e backend como fontes independentes;
- confiar em preço enviado pelo tablet;
- usar SignalR para confirmar persistência;
- apagar pagamento ao estornar;
- alterar pedido histórico quando catálogo muda;
- criar duas sessões ativas para a mesma mesa;
- permitir dois vínculos ativos para o mesmo dispositivo.

## 12. Definition of Done

Consulte `docs/checklists/definition-of-done.md`.
