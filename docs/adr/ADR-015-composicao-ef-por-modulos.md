# ADR-015 — Composição EF Core por módulos

Status: Aceito.

Existe um único `AppizzaDbContext` físico e um histórico central de migrations. Cada módulo mantém
suas entidades e `IEntityTypeConfiguration<T>` no próprio assembly.

`Appizza.Persistence` é a camada de composição autorizada a referenciar módulos e aplicar seus
mappings. Módulos não dependem de `Appizza.Persistence`; seus casos de uso usam contratos próprios,
implementados na composição. Isso preserva um assembly por módulo sem criar dependência circular.

As migrations da Fase 1 são incrementais: `Phase1_EstablishmentsIdentity` e
`Phase1_DevicesTables`. A migration `Foundation` não será editada.
