# Segurança

- HTTPS em produção, inclusive rede local quando possível.
- refresh tokens rotativos.
- device credential revogável.
- password/PIN hash robusto.
- segredo fora do repositório.
- RBAC no backend.
- rate limit em login/bootstrap.
- CPF criptografado em repouso e mascarado.
- cartão: não armazenar PAN/CVV.
- logs mascarados.
- auditoria de consulta de dado sensível.
- dependências atualizadas e scan de vulnerabilidade no CI.

## Fase 1

- login resolve tenant por `establishmentCode` amigável e único;
- `PasswordHasher<TUser>` protege senha e PIN, com rate limit para ambos;
- refresh tokens são aleatórios, opacos, rotativos e armazenados somente como hash;
- CPF usa AES-256-GCM em repouso e HMAC-SHA256 com chave separada quando comparação for necessária;
- chaves ficam fora do banco e do repositório;
- não existe endpoint de decriptação de CPF na Fase 1;
- coleta de CPF em produção exige política de retenção configurada, e o Worker anonimiza dados expirados.
