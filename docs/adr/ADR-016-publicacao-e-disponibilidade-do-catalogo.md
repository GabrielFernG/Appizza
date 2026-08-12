# ADR-016 — Publicação e disponibilidade do catálogo

Status: Aceito.

O modelo relacional administrativo é a fonte de edição. Publicação cria uma `CatalogRevision`
imutável por estabelecimento com snapshot JSONB de estrutura, configuração e preços. O snapshot é
uma projeção histórica e não substitui o modelo relacional.

`catalogVersion` e `availabilityVersion` são monotônicos, independentes e serializados por
estabelecimento. Publicação semanticamente idêntica não cria versão. Tentativas persistidas usam
`validating -> published | rejected`; a revisão anterior torna-se `superseded`.

Disponibilidade operacional não integra o snapshot. Recursos possuem valores explícito e efetivo.
Ingrediente obrigatório indisponível torna indisponíveis seus produtos/variantes dependentes;
ingrediente opcional/adicional não. Mudança efetiva incrementa apenas `availabilityVersion`.

`CatalogPublished` é evento crítico persistido na Outbox na mesma transação da publicação.
