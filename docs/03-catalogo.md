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
