# 08 — Promoções e Comunicação

Promoções MVP:
- preço promocional;
- percentual;
- valor fixo;
- oferta combinada;
- quantidade por preço fixo;
- compre X e ganhe Y.

Motor suporta vigência, limites, prioridade, acúmulo e melhor benefício.

Aplicação é versionada/snapshotada.

Comunicação:
apenas imagem e vídeo no tablet.
Sem links externos.
Conteúdo cadastrado pelo estabelecimento.
Banners ficam na tela de status e perdem prioridade para ações urgentes.

Os assets utilizados por Communications pertencem ao módulo neutro Media. Communications apenas
referencia `media.asset`; não é proprietário dos metadados nem do armazenamento dos arquivos.
## Fase 6 — contrato normativo mínimo

### Escopo

**MUST:** promoções por estabelecimento com vigência, aplicação autoritativa na submissão do pedido e snapshot do desconto; comunicações de imagem/vídeo publicadas para o Table Device, com expiração e leitura por GET.

**SHOULD:** prioridade, limites de uso, projeção administrativa e invalidação por SignalR.

**FUTURE:** cupons, segmentação, cashback, push/e-mail/SMS, links externos e engine genérica de regras.

### PROPOSED_DECISIONs

- Primeira versão: `percentage`, `fixed_amount`, `bundle` e `buy_x_get_y`.
- Não há acúmulo; vence a maior vantagem determinística e, em empate, a maior prioridade.
- O desconto é calculado na submissão autoritativa e gravado no snapshot; alterações posteriores não reescrevem pedidos.
- Valores usam `numeric(14,2)` e arredondamento ao centavo.
- Promotions e Communications permanecem desacoplados nesta fase.

Essas decisões alteram preço e experiência do cliente; exigem aprovação de produto antes da implementação.

### Promotions

Lifecycle mínimo: `draft -> active -> inactive|expired`, com `version`, vigência UTC, escopo por establishment, elegibilidade explícita, idempotência e concorrência otimista. Ordering persiste `promotionApplicationId`, `promotionVersionId`, desconto por item e total no snapshot. Operations administra; Table Device apenas exibe o resultado.

RBAC proposto: `promotions.view`, `promotions.create`, `promotions.edit`, `promotions.activate`.

### Communications

Lifecycle: `draft -> published -> paused|expired|archived`. Conteúdo é imagem/vídeo referenciando `media.asset`, com `startsAt`, `endsAt`, `priority` e `displayOrder`. O Table Device lê somente comunicações vigentes; offline conserva cache e reconcilia por GET. Operations publica e pausa.

RBAC proposto: `communications.view`, `communications.create`, `communications.edit`, `communications.publish`.

### Schema e aceite

Entidades mínimas: `promotion`, `promotion_version`, `promotion_application`, `communication` e `communication_publication`, todas com `establishment_id`, `version`, timestamps e índices por status/vigência. Migration proposta: `Phase6_PromotionsCommunications`.

Aceite: lifecycle, tenant, snapshot imutável, concorrência/idempotência, leitura vigente, expiração, reconexão e ausência de mutação financeira fora do Ordering.
## Correção normativa — decisões aprovadas

Esta seção substitui as decisões propostas anteriores para a Fase 6:

- tipos MUST: `percentage` e `fixed_amount`;
- `bundle` e `buy_x_get_y` ficam SHOULD/FUTURE, não serão implementados nesta fase;
- promoções são automáticas, sem seleção pelo cliente;
- não há acúmulo; maior benefício vence; empate usa primeiro `priority` e depois ordenação estável por identificador;
- cálculo ocorre no backend na submissão autoritativa;
- aplicação gera snapshot imutável e o Table Device apenas exibe o resultado;
- Promotions e Communications são domínios desacoplados.

### Decisões de produto ainda necessárias

Permanece bloqueante definir:

1. escopo de elegibilidade: pedido inteiro, itens/produtos, categorias ou combinação;
2. semântica de `fixed_amount`: desconto por pedido, por item ou rateio entre itens;
3. limite de uso por promoção, se existir no MUST desta fase.

Essas decisões alteram preço, persistência e critérios de aceite. Nenhuma implementação deve assumir uma opção silenciosamente.
