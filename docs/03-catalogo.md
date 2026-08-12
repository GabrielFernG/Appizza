# 03 — Catálogo

Tipos de produto:
- simple
- configurable
- pizza
- custom_pizza
- combo

Product é a raiz comercial. ProductVariant representa variações como Coca-Cola 350 ml/600 ml/2 L.

Ingredient é reutilizável entre vários produtos. A tabela `catalog.product_ingredient` contém o comportamento do ingrediente naquele produto.

Pizza suporta:
- tamanho;
- massa;
- borda;
- ingredientes removíveis/adicionais;
- quantidade;
- observação;
- múltiplos sabores sem limite de negócio obrigatório.

Preço multissabores:
média aritmética dos preços integrais dos sabores no tamanho escolhido.

Monte sua Pizza:
cada fração pode ser sabor cadastrado ou montagem do zero com ingredientes.

Combo:
grupos, restrições, escolhas, acréscimos e configuração interna.

Catálogo administrativo é separado do menu publicado.
Disponibilidade operacional pode mudar sem republicar o catálogo inteiro.

## Decisões da Fase 2

O modelo relacional administrativo é a fonte de edição. Entidades individuais usam `active`,
`inactive` e `archived`; não existe estado `draft`. `archived` é terminal na operação administrativa
normal e conteúdo historicamente publicado não é apagado fisicamente.

Publicar cria uma `CatalogRevision` imutável por estabelecimento. Seu snapshot JSONB contém somente
estrutura, configuração e preços. `catalogVersion` é monotônico; publicação semanticamente idêntica
à última revisão não cria versão e retorna `CATALOG_NO_CHANGES_TO_PUBLISH`. Tentativas persistidas
passam por `validating` e terminam em `published` ou `rejected`; a revisão publicada anterior torna-se
`superseded` quando uma nova é publicada.

Disponibilidade operacional não integra o snapshot. Cada ingrediente, produto e variante possui
disponibilidade explícita e efetiva/derivada. Mudança efetiva incrementa somente
`availabilityVersion`. Ingrediente obrigatório indisponível torna indisponíveis os produtos e
variantes que dependem obrigatoriamente dele; ingrediente apenas opcional ou adicional não.

## Ingredientes

- `required_for_recipe = true` exige `included_by_default = true` e `can_be_removed = false`;
- ingrediente opcional incluído por padrão pode ser removível;
- adicional usa `can_be_added`, preço adicional e quantidade máxima quando aplicável;
- o mesmo `ingredient` é reutilizado por vários produtos através de `product_ingredient`;
- não duplicar ingrediente apenas por participar de produtos diferentes.

## Pizza multissabor e Monte sua Pizza

No MVP, N sabores ocupam partes iguais de `1/N`. O preço-base é a média aritmética dos preços dos
sabores aplicáveis ao tamanho. Proporções arbitrárias não pertencem à Fase 2.

O modelo administrativo representa “Monte sua Pizza” assim:
- `pizza_product_size` define tamanhos aceitos e `maximum_flavor_count`;
- `pizza_flavor` e `pizza_flavor_price` representam sabores existentes e preço por tamanho;
- uma seleção pode usar sabor existente ou uma parte montada do zero;
- `custom_pizza_base_price` define a base da parte montada do zero por tamanho;
- `product_ingredient`, com `application_scope = fraction` ou `both`, define ingredientes adicionáveis,
  preço adicional e quantidade máxima;
- `pizza_dough`/`dough_size_price` definem massas e acréscimos por tamanho;
- `pizza_crust`/`crust_size_price` definem bordas e acréscimos por tamanho;
- a quantidade de sabores deve respeitar `maximum_flavor_count`, quando preenchido.

A Fase 2 implementa domínio, persistência, administração e validação dessa configuração. Menu e
carrinho que a consomem pertencem à Fase 3.

Combos são validados integralmente antes da publicação: limites dos grupos, obrigatoriedade,
repetição, critérios de inclusão e restrições precisam formar ao menos uma escolha válida.
