# Estrutura de Módulos

Exemplo:

```text
Modules/Catalog/
├── Domain/
├── Application/
│   ├── Commands/
│   └── Queries/
├── Infrastructure/
├── Contracts/
└── Endpoints/
```

Domain:
invariantes e modelos.

Application:
casos de uso e autorização de aplicação.

Infrastructure:
EF, providers e adaptadores.

Contracts:
integração pública deliberada.

Endpoints:
HTTP apenas.

Evitar referências circulares.

Na Fundação, cada módulo é um assembly mínimo. Não criar pastas, classes ou interfaces vazias apenas
para antecipar uma arquitetura futura. Novas estruturas surgem quando houver comportamento real.

`Media` é um módulo neutro. Communications, Catalog e Establishments referenciam seus contratos,
mas não são proprietários dos assets.

## Composição de persistência

Cada módulo mantém entidades e `IEntityTypeConfiguration<T>` em seu próprio assembly. `Appizza.Persistence` referencia os módulos para compor o único `AppizzaDbContext` e aplicar os mappings. Os módulos não referenciam `Appizza.Persistence`; casos de uso dependem de contratos do próprio módulo, implementados pela composição.
