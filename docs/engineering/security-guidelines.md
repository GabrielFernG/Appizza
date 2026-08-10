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
