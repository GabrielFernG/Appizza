# Observabilidade

Usar logs estruturados, métricas, tracing e health checks.

Métricas mínimas:
- request duration/error rate;
- DB latency;
- Outbox backlog;
- failed jobs;
- SignalR connections;
- payment unknown count;
- device offline count;
- kitchen queue age.

Tracing:
propagar CorrelationId entre HTTP, eventos e jobs.
