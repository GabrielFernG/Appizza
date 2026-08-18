# Appizza — Especificação Oficial v5

Este repositório de documentação é a fonte de verdade funcional e técnica do Appizza.

## Ordem de leitura para humanos

1. `docs/00-visao-geral.md`
2. `docs/01-principios-do-produto.md`
3. `docs/02-fluxos-do-cliente.md`
4. `docs/03-catalogo.md`
5. `docs/04-cozinha.md`
6. `docs/05-mesas-sessoes-dispositivos.md`
7. `docs/06-usuarios-permissoes.md`
8. `docs/07-pagamentos.md`
9. `docs/08-promocoes-comunicacao.md`
10. `docs/09-relatorios-indicadores.md`
11. `docs/10-arquitetura-tecnica.md`
12. `docs/11-modelo-de-dados.md`
13. `docs/12-eventos.md`
14. `docs/13-contratos-api.md`
15. `docs/14-ux-ui-guidelines.md`
16. `docs/15-roadmap-desenvolvimento.md`
17. `docs/engineering/`
18. `docs/adr/`

## Ordem de leitura para Codex

O Codex deve iniciar por `AGENTS.md`, que referencia os documentos obrigatórios por tarefa.

## Autoridade documental

Em caso de conflito:

1. regra funcional explícita mais recente na documentação;
2. ADR aceita;
3. contrato da API / evento;
4. modelo de dados;
5. Figma;
6. implementação existente.

O Figma é referência visual, não especificação rígida.

## Alterações

Toda mudança significativa deve seguir:

`Decisão -> documentação -> ADR se necessário -> implementação -> testes`.

Não introduzir regra de negócio apenas no código.
