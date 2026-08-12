# 11 — Modelo de Dados

Este documento define o modelo relacional conceitual do PostgreSQL para o Appizza.
Ele deve ser lido em conjunto com:

- `docs/03-catalogo.md`
- `docs/04-cozinha.md`
- `docs/05-mesas-sessoes-dispositivos.md`
- `docs/06-usuarios-permissoes.md`
- `docs/07-pagamentos.md`
- `docs/08-promocoes-comunicacao.md`
- `docs/10-arquitetura-tecnica.md`
- `docs/12-eventos.md`
- `docs/13-contratos-api.md`

O objetivo deste arquivo é reduzir o espaço de decisão durante a implementação.
O Codex não deve reinterpretar regras de domínio já definidas aqui.

---

# 1. Convenções Gerais

## 1.1 Chaves primárias

Usar `uuid` como chave técnica principal, salvo exceção justificada.

Padrão:

```text
id uuid primary key
```

IDs podem ser gerados pela aplicação.

Entidades que precisam de número legível terão um campo adicional.

Exemplos:

```text
ordering.customer_order.id            -> uuid
ordering.customer_order.order_number  -> bigint

tables.table_session.id             -> uuid
tables.table_session.session_number -> varchar/bigint amigável
```

Nunca usar o UUID como número mostrado ao cliente.

---

## 1.2 Multiestabelecimento

Toda entidade pertencente a uma unidade deve conter:

```text
establishment_id uuid not null
```

A FK aponta para:

```text
establishments.establishment.id
```

O backend deve sempre aplicar filtro por estabelecimento.

Índices e uniques devem normalmente considerar `establishment_id`.

---

## 1.3 Datas e horários

Datas técnicas:

```text
timestamp with time zone
```

Persistir em UTC.

Exemplos:

```text
created_at
updated_at
occurred_at
submitted_at
opened_at
closed_at
```

Horários recorrentes sem data:

```text
time
```

Dia da semana:

```text
smallint
```

Convenção:

```text
0 = Sunday
1 = Monday
...
6 = Saturday
```

---

## 1.4 Valores monetários

Usar:

```text
numeric(14,2)
```

Nunca usar `float` ou `double` para valores persistidos de dinheiro.

Cálculos proporcionais podem usar precisão maior na aplicação antes do arredondamento final.

Toda coluna monetária deve ter constraint:

```text
amount >= 0
```

quando valores negativos não fizerem sentido.

---

## 1.5 Quantidades

Quantidades inteiras:

```text
integer
```

Porções ou medidas fracionadas de estoque podem usar `numeric`.

Quantidades de item em pedido devem ser:

```text
quantity > 0
```

---

## 1.6 Status

Estados estruturais serão:

- enums no C#;
- persistidos como `varchar`;
- protegidos por `CHECK CONSTRAINT`.

Exemplo conceitual:

```text
status varchar(40) not null
check (status in ('draft','active','inactive','archived'))
```

Não criar tabelas de status para estados que controlam comportamento do sistema.

Motivos, mensagens e opções configuráveis permanecem em tabelas próprias.

---

## 1.7 Concorrência

Entidades críticas devem possuir:

```text
version bigint not null
```

Incrementado pela camada de persistência/EF Core a cada atualização relevante.
Não usar triggers PostgreSQL e não exigir incremento manual pelo domínio.

Aplicar concorrência otimista em:

- `tables.table_session`
- `ordering.customer_order` (entidade C# `Order`)
- `ordering.order_item`
- `kitchen.production_item`
- `payments.payment`
- `payments.payment_attempt`
- `catalog.product`
- disponibilidade crítica
- vínculos de dispositivos

---

## 1.8 Auditoria básica

Tabelas administrativas relevantes devem ter, quando fizer sentido:

```text
created_at
created_by
updated_at
updated_by
```

Histórico operacional detalhado fica em tabelas próprias de histórico ou auditoria.

---

## 1.9 Exclusão

Evitar exclusão física para dados que participaram de operação.

Preferir:

```text
status = archived
```

ou:

```text
archived_at
archived_by
```

Nunca apagar fisicamente:

- pedidos;
- itens de pedido;
- sessões;
- pagamentos;
- estornos;
- promoções aplicadas;
- usuários utilizados em auditoria;
- ocorrências;
- históricos.

---

## 1.10 JSONB

Usar `jsonb` somente quando adequado:

- snapshots históricos;
- payload de evento;
- metadados técnicos;
- configurações raras/variáveis;
- diffs de auditoria;
- resposta mascarada de integração.

Não usar `jsonb` para substituir estruturas centrais relacionais.

---

# 2. Schemas

O banco será dividido em:

```text
establishments
identity
catalog
promotions
media
communications
tables
ordering
kitchen
payments
devices
operations
reporting
auditing
integration
```

Cada módulo deve preferir acessar suas próprias tabelas.

---

# 3. Schema `establishments`

## 3.1 `establishments.establishment`

### Objetivo
Representar uma unidade/estabelecimento.

### Colunas

```text
id uuid PK
public_code varchar(80) not null
legal_name varchar(200) nullable
trade_name varchar(200) not null
tax_identifier varchar(30) nullable
timezone varchar(80) not null
currency_code varchar(3) not null
status varchar(30) not null
logo_media_id uuid nullable
created_at timestamptz not null
created_by uuid nullable
updated_at timestamptz not null
updated_by uuid nullable
version bigint not null
```

### Regras

- `currency_code` inicialmente `BRL`;
- `timezone` obrigatório;
- `tax_identifier` pode conter CNPJ quando necessário;
- `logo_media_id` referencia mídia cadastrada;
- não excluir fisicamente estabelecimento com histórico.

### Índices

```text
unique(tax_identifier) where tax_identifier is not null
unique(public_code)
index(status)
```

---

## 3.2 `establishments.address`

### Objetivo
Armazenar endereço do estabelecimento.

### Colunas

```text
id uuid PK
establishment_id uuid FK not null
street varchar(200) not null
number varchar(40) nullable
complement varchar(120) nullable
district varchar(120) nullable
city varchar(120) not null
state varchar(80) not null
postal_code varchar(20) nullable
country_code varchar(2) not null
created_at timestamptz not null
updated_at timestamptz not null
```

### Relacionamento

```text
establishment 1 --- N address
```

No MVP pode existir apenas um endereço principal, mas o modelo não deve impedir expansão.

---

## 3.3 `establishments.business_hour`

```text
id uuid PK
establishment_id uuid FK not null
day_of_week smallint not null
opening_time time not null
closing_time time not null
active boolean not null
display_order integer not null
```

### Regras

- permitir mais de uma faixa por dia;
- `opening_time <> closing_time`;
- interpretação de virada de dia deve ser definida na aplicação.

---

## 3.4 `establishments.setting`

### Objetivo
Guardar configurações operacionais simples e extensíveis.

```text
id uuid PK
establishment_id uuid FK not null
setting_key varchar(160) not null
setting_value text nullable
value_type varchar(30) not null
updated_by uuid nullable
updated_at timestamptz not null
```

### Unique

```text
unique(establishment_id, setting_key)
```

### Exemplos de chaves

```text
devices.max_active_table_devices_per_table
session.opening_mode (`on_start_ordering` na Fase 1)
session.closing_mode
table.release_mode
delivery.confirmation_mode
delivery.auto_confirmation_enabled
delivery.auto_confirmation_minutes
catalog.show_unavailable_products
payment.allow_partial
payment.allow_split_by_items
payment.allow_split_by_value
payment.allow_split_equally
payment.allow_participants
```

### Observação
Configurações com crescimento de complexidade devem migrar para tabelas tipadas próprias.

---

# 4. Schema `identity`

## 4.1 `identity.user`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(160) not null
email varchar(200) nullable
phone varchar(40) nullable
login varchar(120) not null
password_hash text not null
pin_hash text nullable
status varchar(30) not null
photo_media_id uuid nullable
last_login_at timestamptz nullable
created_at timestamptz not null
created_by uuid nullable
updated_at timestamptz not null
updated_by uuid nullable
version bigint not null
```

### Unique

```text
unique(establishment_id, login)
unique(establishment_id, email) where email is not null
```

### Regras

- senha e PIN apenas hash;
- PIN nunca armazenado em texto;
- usuário desligado não é excluído;
- CPF de funcionário, se necessário, deve ficar protegido e separado.

---

## 4.2 `identity.role`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(120) not null
description text nullable
is_system_role boolean not null
status varchar(30) not null
created_at timestamptz not null
created_by uuid nullable
updated_at timestamptz not null
updated_by uuid nullable
```

### Unique

```text
unique(establishment_id, name)
```

---

## 4.3 `identity.permission`

### Objetivo
Catálogo fixo de permissões conhecidas pela aplicação.

```text
id uuid PK
code varchar(180) not null
module varchar(80) not null
name varchar(160) not null
description text nullable
```

### Unique

```text
unique(code)
```

### Exemplos

```text
catalog.product.create
catalog.product.publish
kitchen.production.accept
kitchen.production.cancel
payments.refund.create
tables.session.force-close
devices.table.configure
devices.table.replace-active
```

---

## 4.4 `identity.role_permission`

```text
id uuid PK
role_id uuid FK not null
permission_id uuid FK not null
scope_type varchar(40) nullable
scope_id uuid nullable
created_at timestamptz not null
```

### Unique

```text
unique(role_id, permission_id, scope_type, scope_id)
```

A aplicação deve impedir duplicidade lógica quando `scope_type` ou `scope_id` forem nulos, pois a
semântica padrão de `NULL` em unique constraints não garante essa regra isoladamente.

---

## 4.5 `identity.user_role`

```text
id uuid PK
user_id uuid FK not null
role_id uuid FK not null
valid_from timestamptz nullable
valid_until timestamptz nullable
created_at timestamptz not null
created_by uuid nullable
```

### Unique

```text
unique(user_id, role_id, valid_from)
```

Na Fase 1, usar índices separados para atribuição sem prazo e atribuição temporal, evitando que
`valid_from` nulo permita duplicidade lógica.

---

## 4.6 `identity.user_permission`

### Objetivo
Conceder ou negar permissões específicas ao usuário.

```text
id uuid PK
user_id uuid FK not null
permission_id uuid FK not null
effect varchar(10) not null
scope_type varchar(40) nullable
scope_id uuid nullable
valid_from timestamptz nullable
valid_until timestamptz nullable
created_at timestamptz not null
created_by uuid nullable
```

### Constraint

```text
effect in ('allow','deny')
```

---

## 4.7 `identity.user_session`

```text
id uuid PK
user_id uuid FK not null
device_id uuid nullable
refresh_token_hash text not null
started_at timestamptz not null
expires_at timestamptz not null
revoked_at timestamptz nullable
last_activity_at timestamptz nullable
ip_address inet nullable
user_agent text nullable
```

### Índices

```text
index(user_id, revoked_at)
index(expires_at)
```

---

## 4.8 `identity.temporary_approval`

```text
id uuid PK
establishment_id uuid FK not null
requested_by_user_id uuid FK not null
approved_by_user_id uuid nullable
permission_code varchar(180) not null
resource_type varchar(100) not null
resource_id uuid nullable
reason text nullable
status varchar(30) not null
requested_at timestamptz not null
decided_at timestamptz nullable
expires_at timestamptz nullable
```

---

# 5. Schema `catalog`

## 5.1 `catalog.category`

```text
id uuid PK
establishment_id uuid FK not null
parent_category_id uuid FK nullable
name varchar(160) not null
description text nullable
image_media_id uuid nullable
display_order integer not null
status varchar(30) not null
created_at timestamptz not null
created_by uuid nullable
updated_at timestamptz not null
updated_by uuid nullable
version bigint not null
```

### Regras

- `parent_category_id` permite hierarquia;
- impedir ciclo hierárquico na aplicação;
- produto pode ter uma categoria principal e categorias adicionais.

---

## 5.2 `catalog.product`

```text
id uuid PK
establishment_id uuid FK not null
product_type varchar(40) not null
name varchar(180) not null
short_name varchar(100) nullable
description text nullable
internal_code varchar(80) nullable
primary_category_id uuid FK nullable
primary_image_media_id uuid nullable
status varchar(30) not null
display_order integer not null
requires_production boolean not null
requires_operational_acceptance boolean not null
allows_notes boolean not null
maximum_note_length integer nullable
preparation_station_id uuid nullable
estimated_preparation_minutes integer nullable
created_at timestamptz not null
created_by uuid nullable
updated_at timestamptz not null
updated_by uuid nullable
version bigint not null
```

### Product types

```text
simple
configurable
pizza
custom_pizza
combo
```

### Regras

- `internal_code` unique por estabelecimento quando preenchido;
- `maximum_note_length > 0` quando preenchido;
- não apagar produto usado em pedido.

---

## 5.3 `catalog.product_category`

```text
product_id uuid FK not null
category_id uuid FK not null
display_order integer not null
```

### PK

```text
primary key(product_id, category_id)
```

---

## 5.4 `catalog.product_variant`

```text
id uuid PK
product_id uuid FK not null
name varchar(160) not null
short_name varchar(100) nullable
internal_code varchar(80) nullable
barcode varchar(80) nullable
base_price numeric(14,2) not null
image_media_id uuid nullable
status varchar(30) not null
display_order integer not null
estimated_preparation_minutes integer nullable
stock_control_enabled boolean not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Constraints

```text
base_price >= 0
```

### Índices

```text
unique(product_id, internal_code) where internal_code is not null
index(product_id, status, display_order)
```

---

## 5.5 `catalog.ingredient`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(160) not null
kitchen_name varchar(160) nullable
description text nullable
default_additional_price numeric(14,2) not null
unit_of_measure varchar(30) nullable
stock_control_enabled boolean not null
status varchar(30) not null
image_media_id uuid nullable
created_at timestamptz not null
created_by uuid nullable
updated_at timestamptz not null
updated_by uuid nullable
version bigint not null
```

### Regras

Um ingrediente é global ao catálogo do estabelecimento e pode participar de vários produtos.

Exemplo:

```text
Mussarela
→ Pizza Portuguesa
→ Pizza Margherita
→ Pizza Mussarela
→ Batata com Mussarela
```

---

## 5.6 `catalog.ingredient_attribute_definition`

### Objetivo
Definir classificações e alergênicos conhecidos/configuráveis.

```text
id uuid PK
establishment_id uuid nullable
code varchar(100) not null
name varchar(160) not null
attribute_type varchar(40) not null
status varchar(30) not null
```

Exemplos:

```text
contains_gluten
contains_lactose
vegetarian
vegan
spicy
```

---

## 5.7 `catalog.ingredient_attribute`

```text
ingredient_id uuid FK not null
attribute_definition_id uuid FK not null
value_boolean boolean nullable
value_text varchar(200) nullable
```

### PK

```text
primary key(ingredient_id, attribute_definition_id)
```

---

## 5.8 `catalog.product_ingredient`

### Objetivo
Tabela de associação muitos-para-muitos entre produto e ingrediente.

```text
id uuid PK
product_id uuid FK not null
ingredient_id uuid FK not null
included_by_default boolean not null
required_for_recipe boolean not null
can_be_removed boolean not null
can_be_added boolean not null
default_quantity numeric(12,3) nullable
maximum_additional_quantity numeric(12,3) nullable
additional_price numeric(14,2) not null
application_scope varchar(40) not null
display_order integer not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Unique

```text
unique(product_id, ingredient_id)
```

### Regras

- um produto possui vários ingredientes;
- um ingrediente participa de vários produtos;
- regras pertencem ao vínculo;
- remoção não reduz preço;
- ingrediente essencial:
  - `included_by_default = true`
  - `required_for_recipe = true`
  - `can_be_removed = false`
- ingrediente removível:
  - `included_by_default = true`
  - `can_be_removed = true`
- adicional:
  - `included_by_default = false`
  - `can_be_added = true`

### `application_scope`

Exemplos:

```text
whole_product
fraction
both
```

---

## 5.9 `catalog.product_variant_ingredient_override`

### Objetivo
Sobrescrever regra de `product_ingredient` para uma variação específica.

```text
id uuid PK
product_variant_id uuid FK not null
product_ingredient_id uuid FK not null
included_by_default_override boolean nullable
required_for_recipe_override boolean nullable
can_be_removed_override boolean nullable
can_be_added_override boolean nullable
maximum_additional_quantity_override numeric(12,3) nullable
additional_price_override numeric(14,2) nullable
available boolean not null
created_at timestamptz not null
updated_at timestamptz not null
```

### Unique

```text
unique(product_variant_id, product_ingredient_id)
```

---

## 5.10 `catalog.customization_group`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(160) not null
description text nullable
selection_type varchar(30) not null
minimum_selections integer not null
maximum_selections integer nullable
display_type varchar(40) not null
reusable boolean not null
status varchar(30) not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Constraints

```text
minimum_selections >= 0
maximum_selections is null or maximum_selections >= minimum_selections
```

### `selection_type`

```text
single
multiple
quantity
```

---

## 5.11 `catalog.customization_option`

```text
id uuid PK
customization_group_id uuid FK not null
name varchar(160) not null
description text nullable
price_rule_type varchar(40) not null
base_additional_price numeric(14,2) not null
linked_ingredient_id uuid nullable
linked_product_id uuid nullable
status varchar(30) not null
display_order integer not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

---

## 5.12 `catalog.product_customization_group`

```text
id uuid PK
product_id uuid FK not null
customization_group_id uuid FK not null
required_override boolean nullable
minimum_override integer nullable
maximum_override integer nullable
display_order integer not null
status varchar(30) not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Unique

```text
unique(product_id, customization_group_id)
```

---

## 5.13 `catalog.product_customization_variant_rule`

```text
id uuid PK
product_customization_group_id uuid FK not null
product_variant_id uuid FK not null
minimum_selections integer nullable
maximum_selections integer nullable
active boolean not null
created_at timestamptz not null
updated_at timestamptz not null
```

---

# 6. Pizza

## 6.1 `catalog.pizza_size`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(120) not null
short_name varchar(80) nullable
slice_count integer nullable
diameter_cm numeric(8,2) nullable
display_order integer not null
status varchar(30) not null
created_at timestamptz not null
updated_at timestamptz not null
```

---

## 6.2 `catalog.pizza_flavor`

### Objetivo
Marcar um `product` como sabor utilizável no motor de pizza.

```text
id uuid PK
product_id uuid FK not null
status varchar(30) not null
display_order integer not null
created_at timestamptz not null
updated_at timestamptz not null
```

### Unique

```text
unique(product_id)
```

---

## 6.3 `catalog.pizza_flavor_price`

```text
id uuid PK
pizza_flavor_id uuid FK not null
pizza_size_id uuid FK not null
price numeric(14,2) not null
available boolean not null
estimated_preparation_minutes integer nullable
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Unique

```text
unique(pizza_flavor_id, pizza_size_id)
```

---

## 6.4 `catalog.dough`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(120) not null
description text nullable
status varchar(30) not null
is_default boolean not null
created_at timestamptz not null
updated_at timestamptz not null
```

---

## 6.5 `catalog.dough_size_price`

```text
dough_id uuid FK not null
pizza_size_id uuid FK not null
additional_price numeric(14,2) not null
available boolean not null
```

### PK

```text
primary key(dough_id, pizza_size_id)
```

---

## 6.6 `catalog.crust`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(120) not null
description text nullable
image_media_id uuid nullable
status varchar(30) not null
created_at timestamptz not null
updated_at timestamptz not null
```

---

## 6.7 `catalog.crust_size_price`

```text
crust_id uuid FK not null
pizza_size_id uuid FK not null
additional_price numeric(14,2) not null
available boolean not null
```

### PK

```text
primary key(crust_id, pizza_size_id)
```

---

## 6.8 `catalog.pizza_product_size`

```text
product_id uuid FK not null
pizza_size_id uuid FK not null
available boolean not null
maximum_flavor_count integer nullable
display_order integer not null
```

### PK

```text
primary key(product_id, pizza_size_id)
```

### Regras

`maximum_flavor_count = null` significa sem limite de negócio, mas a UI pode impor um máximo prático configurável.

---

## 6.9 `catalog.pizza_dough`

```text
product_id uuid FK not null
dough_id uuid FK not null
available boolean not null
```

### PK

```text
primary key(product_id, dough_id)
```

---

## 6.10 `catalog.pizza_crust`

```text
product_id uuid FK not null
crust_id uuid FK not null
available boolean not null
```

### PK

```text
primary key(product_id, crust_id)
```

---

## 6.11 `catalog.custom_pizza_base_price`

```text
custom_pizza_product_id uuid FK not null
pizza_size_id uuid FK not null
base_price numeric(14,2) not null
available boolean not null
```

### PK

```text
primary key(custom_pizza_product_id, pizza_size_id)
```

---

# 7. Combos

## 7.1 `catalog.combo`

```text
id uuid PK
product_id uuid FK not null
pricing_strategy varchar(40) not null
fixed_price numeric(14,2) nullable
discount_type varchar(30) nullable
discount_value numeric(14,2) nullable
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Unique

```text
unique(product_id)
```

---

## 7.2 `catalog.combo_group`

```text
id uuid PK
combo_id uuid FK not null
name varchar(160) not null
description text nullable
minimum_items integer not null
maximum_items integer not null
allow_repetition boolean not null
required boolean not null
display_order integer not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

---

## 7.3 `catalog.combo_group_item`

```text
id uuid PK
combo_group_id uuid FK not null
product_id uuid nullable
product_variant_id uuid nullable
category_id uuid nullable
inclusion_type varchar(40) not null
additional_price numeric(14,2) not null
fixed_quantity integer nullable
status varchar(30) not null
created_at timestamptz not null
updated_at timestamptz not null
```

### Regra

Exatamente um critério principal deve estar preenchido quando exigido pela estratégia:

- `product_id`
- `product_variant_id`
- `category_id`

A aplicação valida combinações.

---

## 7.4 `catalog.combo_item_restriction`

```text
id uuid PK
combo_group_item_id uuid FK not null
restriction_type varchar(80) not null
referenced_entity_id uuid nullable
value text nullable
created_at timestamptz not null
```

Exemplos:

```text
allowed_pizza_size
max_flavor_count
forbidden_crust
allowed_variant
```

---

# 7A. Publicação e disponibilidade do catálogo

## 7A.1 `catalog.catalog_state`

```text
establishment_id uuid PK/FK
catalog_version bigint not null
availability_version bigint not null
updated_at timestamptz not null
version bigint not null
```

Os contadores são monotônicos e independentes. Publicação semanticamente nova incrementa somente
`catalog_version`; mudança efetiva de disponibilidade incrementa somente `availability_version`.

## 7A.2 `catalog.catalog_revision`

```text
id uuid PK
establishment_id uuid FK not null
catalog_version bigint nullable
status varchar(30) not null
snapshot jsonb nullable
semantic_hash varchar(64) nullable
validation_errors jsonb nullable
created_by uuid not null
created_at timestamptz not null
published_at timestamptz nullable
superseded_at timestamptz nullable
```

Estados: `validating`, `published`, `rejected`, `superseded`. Uma revisão publicada é imutável. O
snapshot contém estrutura, configuração e preços, nunca disponibilidade operacional. Uniques:
`(establishment_id, catalog_version)` quando a versão não for nula e uma única revisão `published`
por estabelecimento por índice único parcial.

## 7A.3 Disponibilidade

`catalog.ingredient_availability`, `catalog.product_availability` e
`catalog.product_variant_availability` possuem:

```text
id uuid PK
establishment_id uuid FK not null
<resource_id> uuid FK not null
explicit_available boolean not null
effective_available boolean not null
reason_code varchar(80) nullable
changed_by uuid not null
changed_at timestamptz not null
version bigint not null
```

Cada recurso possui uma única linha de disponibilidade. `effective_available` inclui dependências.
Ingrediente obrigatório indisponível propaga indisponibilidade; opcional/adicional indisponível não.
Mudança que não altere disponibilidade efetiva nem explícita não incrementa a versão global.

---

# 8. Schema `promotions`

## 8.1 `promotions.promotion`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(180) not null
description text nullable
promotion_type varchar(50) not null
status varchar(30) not null
priority integer not null
stacking_policy varchar(50) not null
start_at timestamptz nullable
end_at timestamptz nullable
usage_limit_total integer nullable
usage_limit_per_session integer nullable
max_discount_amount numeric(14,2) nullable
created_at timestamptz not null
created_by uuid nullable
updated_at timestamptz not null
updated_by uuid nullable
published_at timestamptz nullable
version bigint not null
```

### Tipos MVP

```text
promotional_price
percentage_discount
fixed_discount
combined_offer
quantity_fixed_price
buy_x_get_y
```

---

## 8.2 `promotions.promotion_schedule`

```text
id uuid PK
promotion_id uuid FK not null
day_of_week smallint not null
start_time time not null
end_time time not null
```

---

## 8.3 `promotions.promotion_condition`

```text
id uuid PK
promotion_id uuid FK not null
condition_type varchar(60) not null
operator varchar(30) nullable
quantity numeric(12,3) nullable
amount numeric(14,2) nullable
product_id uuid nullable
variant_id uuid nullable
category_id uuid nullable
payment_method varchar(40) nullable
configuration jsonb nullable
display_order integer not null
```

---

## 8.4 `promotions.promotion_benefit`

```text
id uuid PK
promotion_id uuid FK not null
benefit_type varchar(60) not null
percentage numeric(8,4) nullable
fixed_amount numeric(14,2) nullable
fixed_price numeric(14,2) nullable
product_id uuid nullable
variant_id uuid nullable
category_id uuid nullable
quantity numeric(12,3) nullable
configuration jsonb nullable
display_order integer not null
```

---

## 8.5 `promotions.promotion_compatibility`

```text
promotion_id uuid FK not null
compatible_promotion_id uuid FK not null
allowed boolean not null
```

### PK

```text
primary key(promotion_id, compatible_promotion_id)
```

---

## 8.6 `promotions.promotion_version`

```text
id uuid PK
promotion_id uuid FK not null
version_number integer not null
snapshot jsonb not null
created_at timestamptz not null
created_by uuid nullable
```

### Unique

```text
unique(promotion_id, version_number)
```

---

## 8.7 `promotions.promotion_application`

```text
id uuid PK
promotion_id uuid FK not null
promotion_version_id uuid FK not null
order_id uuid FK not null
original_amount numeric(14,2) not null
discount_amount numeric(14,2) not null
final_amount numeric(14,2) not null
applied_at timestamptz not null
application_count integer not null
```

---

## 8.8 `promotions.promotion_application_item`

```text
promotion_application_id uuid FK not null
order_item_id uuid FK not null
participation_type varchar(40) not null
original_amount numeric(14,2) not null
benefit_amount numeric(14,2) not null
final_amount numeric(14,2) not null
```

### PK

```text
primary key(promotion_application_id, order_item_id, participation_type)
```

---

# 9. Schema `tables`

## 9.1 `tables.sector`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(120) not null
description text nullable
display_order integer not null
status varchar(30) not null
created_at timestamptz not null
updated_at timestamptz not null
```

---

## 9.2 `tables.dining_table`

```text
id uuid PK
establishment_id uuid FK not null
sector_id uuid FK nullable
name varchar(120) not null
internal_code varchar(80) nullable
capacity integer nullable
status varchar(30) not null
display_order integer not null
notes text nullable
created_at timestamptz not null
created_by uuid nullable
updated_at timestamptz not null
updated_by uuid nullable
version bigint not null
```

### Status

```text
available
occupied
closing
awaiting_cleaning
blocked
inactive
```

### Unique

```text
unique(establishment_id, internal_code) where internal_code is not null
```

---

## 9.3 `tables.table_session`

```text
id uuid PK
establishment_id uuid FK not null
dining_table_id uuid FK not null
session_number varchar(80) not null
status varchar(40) not null
opened_at timestamptz not null
customer_identification_status varchar(20) not null
customer_identification_resolved_at timestamptz nullable
closing_started_at timestamptz nullable
paid_at timestamptz nullable
closed_at timestamptz nullable
opened_by_device_id uuid nullable
opened_by_user_id uuid nullable
guest_count integer nullable
subtotal_amount numeric(14,2) not null
discount_amount numeric(14,2) not null
adjustment_amount numeric(14,2) not null
service_charge_amount numeric(14,2) not null
cover_charge_amount numeric(14,2) not null
total_amount numeric(14,2) not null
paid_amount numeric(14,2) not null
reserved_amount numeric(14,2) not null
remaining_amount numeric(14,2) not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Estados

```text
open
closing
awaiting_payment
partially_paid
paid
closed
suspended
cancelled
```

### Identificação

```text
customer_identification_status in ('pending','provided','skipped')
```

`resolved_at` é obrigatório para `provided` e `skipped`, e nulo para `pending`.

### Índice único parcial

Uma mesa não pode possuir mais de uma sessão ativa.

Conceitualmente:

```text
unique(dining_table_id)
where status in (
  'open',
  'closing',
  'awaiting_payment',
  'partially_paid',
  'paid',
  'suspended'
)
```

---

## 9.4 `tables.session_customer_identification`

```text
id uuid PK
table_session_id uuid FK not null
identification_type varchar(30) not null
encrypted_value text not null
value_hash text nullable
masked_value varchar(80) not null
purpose varchar(160) not null
collected_at timestamptz not null
device_id uuid nullable
retention_until timestamptz nullable
```

### Regras

- CPF completo criptografado;
- nunca logar;
- retornar apenas mascarado;
- uma identificação por finalidade, salvo decisão futura.

---

## 9.5 `tables.session_transfer`

```text
id uuid PK
table_session_id uuid FK not null
previous_table_id uuid FK not null
new_table_id uuid FK not null
transferred_by_user_id uuid FK not null
reason text not null
transferred_at timestamptz not null
```

---

## 9.6 `tables.table_session_status_history`

```text
id uuid PK
table_session_id uuid FK not null
previous_status varchar(40) nullable
new_status varchar(40) not null
changed_by_user_id uuid nullable
changed_by_device_id uuid nullable
reason text nullable
changed_at timestamptz not null
correlation_id uuid nullable
```

---

# 10. Schema `ordering`

## 10.1 `ordering.customer_order`

A entidade C# continua se chamando `Order`. O nome físico `customer_order` evita o uso da palavra
reservada `order` no PostgreSQL e é uma exceção documental deliberada ao alinhamento direto entre
nome da entidade e nome da tabela.

```text
id uuid PK
establishment_id uuid FK not null
table_session_id uuid FK not null
order_number bigint not null
commercial_status varchar(40) not null
submitted_at timestamptz not null
subtotal_amount numeric(14,2) not null
discount_amount numeric(14,2) not null
total_amount numeric(14,2) not null
source_device_id uuid not null
created_at timestamptz not null
version bigint not null
```

### Commercial status

```text
submitted
partially_cancelled
cancelled
completed
```

### Unique

```text
unique(establishment_id, order_number)
```

---

## 10.2 `ordering.order_item`

```text
id uuid PK
order_id uuid FK not null
product_id uuid nullable
product_variant_id uuid nullable
product_type varchar(40) not null
quantity integer not null
unit_base_price numeric(14,2) not null
unit_additional_amount numeric(14,2) not null
unit_discount_amount numeric(14,2) not null
unit_final_price numeric(14,2) not null
total_amount numeric(14,2) not null
commercial_status varchar(40) not null
product_name_snapshot varchar(180) not null
variant_name_snapshot varchar(160) nullable
snapshot_version integer not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Regras

- `product_id` pode permanecer nullable para preservar histórico caso política futura permita desacoplar referências, mas normalmente será preenchido;
- preço do item nunca é recalculado retroativamente por alteração no catálogo;
- alterações aprovadas geram nova versão/histórico.

---

## 10.3 `ordering.order_item_snapshot`

```text
order_item_id uuid PK/FK
snapshot jsonb not null
created_at timestamptz not null
```

### Deve preservar

- nome;
- descrição necessária;
- variação;
- tamanho;
- massa;
- borda;
- sabores;
- ingredientes;
- adicionais;
- remoções;
- valores;
- promoção;
- textos necessários para auditoria/histórico.

---

## 10.4 `ordering.order_item_option`

```text
id uuid PK
order_item_id uuid FK not null
customization_group_id uuid nullable
customization_option_id uuid nullable
group_name_snapshot varchar(160) nullable
option_name_snapshot varchar(160) not null
quantity numeric(12,3) not null
unit_additional_price numeric(14,2) not null
total_additional_price numeric(14,2) not null
display_order integer not null
```

---

## 10.5 `ordering.order_item_ingredient_change`

```text
id uuid PK
order_item_id uuid FK not null
ingredient_id uuid nullable
ingredient_name_snapshot varchar(160) not null
change_type varchar(20) not null
quantity numeric(12,3) nullable
scope_type varchar(40) not null
pizza_fraction_id uuid nullable
additional_amount numeric(14,2) not null
```

### `change_type`

```text
removed
added
```

---

## 10.6 `ordering.order_item_note`

```text
id uuid PK
order_item_id uuid FK not null
note_scope varchar(40) not null
pizza_fraction_id uuid nullable
note_text varchar(1000) not null
created_at timestamptz not null
```

---

## 10.7 `ordering.pizza_configuration`

```text
id uuid PK
order_item_id uuid FK not null
pizza_size_id uuid nullable
size_name_snapshot varchar(120) not null
dough_id uuid nullable
dough_name_snapshot varchar(120) nullable
crust_id uuid nullable
crust_name_snapshot varchar(120) nullable
flavor_count integer not null
base_amount numeric(14,2) not null
dough_amount numeric(14,2) not null
crust_amount numeric(14,2) not null
whole_pizza_additional_amount numeric(14,2) not null
pricing_policy varchar(50) not null
```

### Unique

```text
unique(order_item_id)
```

---

## 10.8 `ordering.pizza_fraction`

```text
id uuid PK
pizza_configuration_id uuid FK not null
position integer not null
fraction_numerator integer not null
fraction_denominator integer not null
pizza_flavor_id uuid nullable
flavor_name_snapshot varchar(180) nullable
is_custom_flavor boolean not null
reference_full_price numeric(14,2) not null
fraction_price numeric(14,2) not null
```

### Constraints

```text
position > 0
fraction_numerator > 0
fraction_denominator > 0
fraction_numerator <= fraction_denominator
```

A soma das frações é validada no domínio.

---

## 10.9 `ordering.order_item_request`

```text
id uuid PK
order_item_id uuid FK not null
request_type varchar(20) not null
requested_configuration jsonb nullable
reason_code varchar(80) nullable
customer_note text nullable
status varchar(40) not null
required_approval_level varchar(40) nullable
price_difference numeric(14,2) not null
requested_at timestamptz not null
withdrawn_at timestamptz nullable
decided_at timestamptz nullable
decided_by_user_id uuid nullable
decision_reason text nullable
version bigint not null
```

### `request_type`

```text
cancel
change
```

### Status

```text
pending
pending_customer_confirmation
approved
rejected
withdrawn
expired
```

### Regra

Um item não pode possuir duas solicitações incompatíveis pendentes ao mesmo tempo.

Criar índice único parcial para requests pendentes por item, conforme os estados definidos.

---

## 10.10 `ordering.order_status_history`

```text
id uuid PK
order_id uuid FK not null
previous_status varchar(40) nullable
new_status varchar(40) not null
substatus_code varchar(80) nullable
customer_message text nullable
reason text nullable
changed_at timestamptz not null
correlation_id uuid nullable
```

---

# 11. Schema `kitchen`

## 11.1 `kitchen.station`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(120) not null
station_type varchar(50) not null
status varchar(30) not null
display_order integer not null
default_target_minutes integer nullable
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

---

## 11.2 `kitchen.production_item`

```text
id uuid PK
establishment_id uuid FK not null
order_item_id uuid FK not null
station_id uuid FK nullable
status varchar(50) not null
queue_position numeric(20,6) not null
manual_priority integer not null
received_at timestamptz not null
accepted_at timestamptz nullable
preparation_started_at timestamptz nullable
ready_at timestamptz nullable
sent_to_table_at timestamptz nullable
delivered_at timestamptz nullable
estimated_preparation_minutes integer nullable
current_attempt_number integer not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Status

```text
awaiting_acceptance
accepted
awaiting_preparation
in_preparation
paused
ready
awaiting_delivery_confirmation
delivered
cancelled
```

### Índices

```text
index(establishment_id, station_id, status, queue_position)
index(order_item_id)
```

---

## 11.3 `kitchen.production_attempt`

```text
id uuid PK
production_item_id uuid FK not null
attempt_number integer not null
status varchar(40) not null
started_at timestamptz nullable
finished_at timestamptz nullable
failure_reason_code varchar(80) nullable
failure_description text nullable
created_by_user_id uuid nullable
effective_duration_seconds integer nullable
created_at timestamptz not null
```

### Unique

```text
unique(production_item_id, attempt_number)
```

---

## 11.4 `kitchen.production_pause`

```text
id uuid PK
production_item_id uuid FK not null
production_attempt_id uuid nullable
reason_code varchar(80) not null
description text nullable
paused_at timestamptz not null
resumed_at timestamptz nullable
paused_by_user_id uuid not null
resumed_by_user_id uuid nullable
```

---

## 11.5 `kitchen.production_status_history`

```text
id uuid PK
production_item_id uuid FK not null
previous_status varchar(50) nullable
new_status varchar(50) not null
user_id uuid nullable
reason_code varchar(80) nullable
reason text nullable
changed_at timestamptz not null
correlation_id uuid nullable
```

---

## 11.6 `kitchen.delivery_confirmation`

```text
id uuid PK
production_item_id uuid FK not null
attempt_number integer not null
status varchar(40) not null
requested_at timestamptz not null
auto_confirmation_due_at timestamptz nullable
confirmation_source varchar(30) nullable
confirmed_at timestamptz nullable
confirmed_device_id uuid nullable
confirmed_by_user_id uuid nullable
contested_at timestamptz nullable
resolved_at timestamptz nullable
resolved_by_user_id uuid nullable
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### `confirmation_source`

```text
customer
waiter
automatic
kitchen
expedition
```

No MVP, customer/waiter/automatic são os principais.

---

## 11.7 `kitchen.delivery_contest`

```text
id uuid PK
delivery_confirmation_id uuid FK not null
reason_code varchar(80) not null
customer_note text nullable
status varchar(30) not null
contested_at timestamptz not null
resolved_at timestamptz nullable
resolved_by_user_id uuid nullable
resolution text nullable
```

---

# 12. Schema `payments`

## 12.1 `payments.payment_participant`

```text
id uuid PK
table_session_id uuid FK not null
display_name varchar(120) nullable
display_order integer not null
status varchar(30) not null
created_at timestamptz not null
updated_at timestamptz not null
```

Participante não é usuário do sistema.

---

## 12.2 `payments.payment_plan`

```text
id uuid PK
table_session_id uuid FK not null
mode varchar(40) not null
status varchar(30) not null
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Modos

```text
total
participants
items
amount
equal_split
```

---

## 12.3 `payments.payment_plan_participant`

```text
id uuid PK
payment_plan_id uuid FK not null
payment_participant_id uuid FK not null
suggested_amount numeric(14,2) not null
assigned_amount numeric(14,2) nullable
```

---

## 12.4 `payments.payment_plan_item`

```text
id uuid PK
payment_plan_id uuid FK not null
payment_participant_id uuid nullable
order_item_id uuid FK not null
assigned_amount numeric(14,2) nullable
```

### Unique lógico

```text
unique(payment_plan_id, order_item_id, payment_participant_id)
```

A aplicação deve impedir duplicidade lógica quando `payment_participant_id` for nulo.

---

## 12.5 `payments.payment`

```text
id uuid PK
establishment_id uuid FK not null
table_session_id uuid FK not null
payment_participant_id uuid nullable
method varchar(40) not null
status varchar(40) not null
amount numeric(14,2) not null
approved_amount numeric(14,2) not null
provider varchar(80) nullable
created_at timestamptz not null
paid_at timestamptz nullable
created_by_user_id uuid nullable
version bigint not null
```

### Status

```text
pending
paid
partially_refunded
refunded
cancelled
failed
```

---

## 12.6 `payments.payment_attempt`

```text
id uuid PK
payment_id uuid FK not null
idempotency_key varchar(120) not null
provider varchar(80) not null
status varchar(50) not null
requested_amount numeric(14,2) not null
reserved_amount numeric(14,2) not null
external_transaction_id varchar(200) nullable
provider_status varchar(120) nullable
expires_at timestamptz nullable
created_at timestamptz not null
updated_at timestamptz not null
response_metadata jsonb nullable
version bigint not null
```

### Status

```text
created
awaiting_customer_action
processing
approved
declined
expired
cancelled
unknown
```

### Unique

```text
unique(idempotency_key)
unique(provider, external_transaction_id)
where external_transaction_id is not null
```

---

## 12.7 `payments.payment_allocation`

```text
id uuid PK
payment_id uuid FK not null
order_item_id uuid nullable
payment_participant_id uuid nullable
amount numeric(14,2) not null
allocation_type varchar(40) not null
created_at timestamptz not null
```

### `allocation_type`

```text
session
item
participant
equal_split
manual_amount
```

---

## 12.8 `payments.payment_reservation`

```text
id uuid PK
table_session_id uuid FK not null
payment_attempt_id uuid FK not null
amount numeric(14,2) not null
expires_at timestamptz nullable
released_at timestamptz nullable
release_reason varchar(80) nullable
created_at timestamptz not null
```

### Índice

```text
index(table_session_id, released_at)
```

---

## 12.9 `payments.cash_payment_detail`

```text
payment_id uuid PK/FK
change_for_amount numeric(14,2) nullable
expected_change_amount numeric(14,2) nullable
received_amount numeric(14,2) nullable
delivered_change_amount numeric(14,2) nullable
confirmed_by_user_id uuid nullable
confirmed_at timestamptz nullable
```

---

## 12.10 `payments.card_payment_detail`

```text
payment_id uuid PK/FK
card_type varchar(30) not null
brand varchar(60) nullable
masked_last_digits varchar(8) nullable
installment_count integer nullable
manual_confirmation boolean not null
softpos boolean not null
confirmed_by_user_id uuid nullable
provider_terminal_reference varchar(160) nullable
```

---

## 12.11 `payments.pix_payment_detail`

```text
payment_id uuid PK/FK
qr_code_reference text nullable
copy_paste_code_encrypted text nullable
expires_at timestamptz nullable
end_to_end_id varchar(200) nullable
confirmed_at timestamptz nullable
```

---

## 12.12 `payments.refund`

```text
id uuid PK
payment_id uuid FK not null
amount numeric(14,2) not null
status varchar(40) not null
reason text not null
external_refund_id varchar(200) nullable
requested_by_user_id uuid not null
approved_by_user_id uuid nullable
requested_at timestamptz not null
completed_at timestamptz nullable
provider_response_metadata jsonb nullable
version bigint not null
```

---

## 12.13 `payments.manual_discount`

```text
id uuid PK
table_session_id uuid FK not null
order_id uuid nullable
order_item_id uuid nullable
discount_type varchar(30) not null
amount numeric(14,2) nullable
percentage numeric(8,4) nullable
reason text not null
applied_by_user_id uuid not null
approved_by_user_id uuid nullable
applied_at timestamptz not null
```

---

# 13. Schema `devices`

## 13.1 `devices.device`

```text
id uuid PK
establishment_id uuid FK nullable
installation_id uuid not null
name varchar(160) not null
device_type varchar(40) not null
platform varchar(40) not null
model varchar(120) nullable
operating_system_version varchar(80) nullable
app_version varchar(40) not null
status varchar(40) not null
credential_hash text nullable
credential_version integer not null
registered_at timestamptz not null
last_seen_at timestamptz nullable
blocked_at timestamptz nullable
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

### Unique

```text
unique(installation_id)
```

`establishment_id` é nulo somente em `awaiting_configuration`. O bind atribui o estabelecimento e
ele não pode ser trocado sem revogação/reset explícito.

---

## 13.2 `devices.device_table_binding`

```text
id uuid PK
device_id uuid FK not null
dining_table_id uuid FK not null
bound_at timestamptz not null
unbound_at timestamptz nullable
bound_by_user_id uuid not null
unbound_by_user_id uuid nullable
unbind_reason text nullable
created_at timestamptz not null
version bigint not null
```

### Regra

Um dispositivo só pode possuir um vínculo ativo.

Índice único parcial:

```text
unique(device_id)
where unbound_at is null
```

A quantidade de dispositivos ativos por mesa não é constraint fixa; é validada pela configuração:

```text
devices.max_active_table_devices_per_table
```

---

## 13.3 `devices.device_session`

```text
id uuid PK
device_id uuid FK not null
refresh_token_hash varchar(160) not null
credential_version integer not null
started_at timestamptz not null
expires_at timestamptz not null
last_activity_at timestamptz nullable
revoked_at timestamptz nullable
replaced_by_session_id uuid nullable
```

Índices:

```text
unique(refresh_token_hash)
index(device_id, revoked_at)
index(expires_at)
```

Refresh tokens são opacos, rotativos e persistidos somente como hash. Rotação revoga a sessão
anterior e referencia a sucessora.

---

## 13.4 `devices.device_heartbeat`

### Objetivo
Guardar apenas o estado operacional mais recente.

```text
device_id uuid PK/FK
received_at timestamptz not null
battery_percentage integer nullable
network_status varchar(30) nullable
storage_available_bytes bigint nullable
kiosk_mode_active boolean nullable
sync_status varchar(40) nullable
last_catalog_sync_at timestamptz nullable
```

Não guardar cada heartbeat histórico indefinidamente.

---

## 13.5 `devices.device_event`

```text
id uuid PK
device_id uuid FK not null
event_type varchar(80) not null
severity varchar(30) not null
details jsonb nullable
occurred_at timestamptz not null
```

---

# 14. Schema `media`

Media é um módulo neutro. Establishments, Identity, Catalog e Communications podem referenciar seus
assets, mas nenhum desses módulos é proprietário dos metadados ou dos arquivos.

Os binários ficam em object storage através de `IObjectStorage`; a tabela contém apenas metadados e
referências. O provider local de Development é SeaweedFS via API S3, sem tipos específicos vazando
para Domain ou Application. O provider de produção permanece indefinido.

## 14.1 `media.asset`

```text
id uuid PK
establishment_id uuid FK not null
media_type varchar(20) not null
storage_path text not null
mime_type varchar(120) not null
file_size bigint not null
duration_seconds integer nullable
checksum varchar(160) not null
processing_status varchar(40) not null
created_by uuid nullable
created_at timestamptz not null
```

### `media_type`

```text
image
video
```

Estados: `pending_upload`, `ready`, `failed`, `archived`. Transições permitidas:
`pending_upload -> ready | failed` e `ready -> archived`. Não há processamento ou thumbnails na
Fase 2. Checksum é obrigatório para integridade, sem deduplicação automática.

Um asset pode ser reutilizado por entidades do mesmo estabelecimento. Não pode ser associado
cross-tenant nem apagado fisicamente quando referenciado por revisão publicada. Upload valida
ownership, MIME permitido, tamanho máximo, checksum e segurança da chave/nome.

Esta tabela é conceitual e não faz parte da migration `Foundation`.

---

# 15. Schema `communications`

## 15.1 `communications.partner`

```text
id uuid PK
establishment_id uuid FK not null
name varchar(180) not null
description text nullable
status varchar(30) not null
internal_notes text nullable
created_at timestamptz not null
updated_at timestamptz not null
```

---

## 15.2 `communications.communication`

```text
id uuid PK
establishment_id uuid FK not null
partner_id uuid nullable
media_id uuid FK not null -> media.asset.id
communication_type varchar(40) not null
internal_title varchar(180) not null
internal_description text nullable
status varchar(30) not null
priority integer not null
start_at timestamptz nullable
end_at timestamptz nullable
display_duration_seconds integer nullable
audio_enabled boolean not null
created_at timestamptz not null
created_by uuid nullable
updated_at timestamptz not null
updated_by uuid nullable
version bigint not null
```

---

## 15.3 `communications.communication_schedule`

```text
id uuid PK
communication_id uuid FK not null
day_of_week smallint not null
start_time time not null
end_time time not null
```

---

## 15.4 `communications.communication_target`

```text
id uuid PK
communication_id uuid FK not null
target_type varchar(60) not null
target_value varchar(200) nullable
configuration jsonb nullable
```

Exemplos:

```text
order_status
minimum_session_amount
category_ordered
category_not_ordered
sector
table
```

---

## 15.5 `communications.communication_display`

```text
id uuid PK
communication_id uuid FK not null
table_session_id uuid nullable
device_id uuid nullable
displayed_at timestamptz not null
playback_completed boolean nullable
displayed_duration_seconds integer nullable
context jsonb nullable
```

Em alto volume, projetar agregações no schema `reporting`.

---

# 16. Schema `operations`

## 16.1 `operations.occurrence`

```text
id uuid PK
establishment_id uuid FK not null
occurrence_type varchar(80) not null
priority varchar(30) not null
status varchar(30) not null
source_module varchar(60) not null
table_session_id uuid nullable
order_id uuid nullable
order_item_id uuid nullable
production_item_id uuid nullable
payment_id uuid nullable
device_id uuid nullable
title varchar(200) not null
description text nullable
assigned_user_id uuid nullable
opened_at timestamptz not null
resolved_at timestamptz nullable
resolution text nullable
created_at timestamptz not null
updated_at timestamptz not null
version bigint not null
```

---

## 16.2 `operations.occurrence_history`

```text
id uuid PK
occurrence_id uuid FK not null
previous_status varchar(30) nullable
new_status varchar(30) not null
user_id uuid nullable
comment text nullable
changed_at timestamptz not null
```

---

# 17. Schema `auditing`

## 17.1 `auditing.audit_entry`

```text
id uuid PK
establishment_id uuid FK not null
user_id uuid nullable
device_id uuid nullable
action varchar(180) not null
resource_type varchar(120) not null
resource_id uuid nullable
old_values jsonb nullable
new_values jsonb nullable
reason text nullable
correlation_id uuid nullable
ip_address inet nullable
occurred_at timestamptz not null
```

### Regras

Nunca armazenar em `old_values`/`new_values`:
- senha;
- PIN;
- token;
- CPF completo;
- dados completos de cartão.

---

## 17.2 `auditing.sensitive_data_access`

```text
id uuid PK
establishment_id uuid FK not null
user_id uuid not null
resource_type varchar(120) not null
resource_id uuid not null
data_type varchar(80) not null
purpose varchar(180) not null
accessed_at timestamptz not null
```

Nunca gravar o valor sensível.

---

# 18. Schema `integration`

## 18.1 `integration.outbox_message`

```text
id uuid PK
establishment_id uuid nullable
event_type varchar(180) not null
schema_version integer not null
payload jsonb not null
occurred_at timestamptz not null
processed_at timestamptz nullable
retry_count integer not null
next_retry_at timestamptz nullable
error_message text nullable
correlation_id uuid nullable
causation_id uuid nullable
```

### Índices

```text
index(processed_at, next_retry_at)
index(event_type, occurred_at)
```

---

## 18.2 `integration.inbox_message`

```text
event_id uuid not null
consumer_name varchar(160) not null
processed_at timestamptz not null
result varchar(40) nullable
error_message text nullable
```

### PK

```text
primary key(event_id, consumer_name)
```

---

## 18.3 `integration.idempotency_record`

```text
idempotency_key varchar(120) not null
establishment_id uuid nullable
operation_type varchar(160) not null
request_hash varchar(160) not null
response_status integer nullable
response_payload jsonb nullable
created_at timestamptz not null
expires_at timestamptz nullable
```

### PK

```text
primary key(idempotency_key, operation_type)
```

### Regra

Mesma chave + mesmo request:
- retornar resultado anterior.

Mesma chave + request diferente:
- rejeitar.

---

# 19. Schema `reporting`

Essas tabelas são projeções e não fonte de verdade.

## 19.1 `reporting.daily_sales`

```text
establishment_id uuid not null
business_date date not null
gross_amount numeric(14,2) not null
discount_amount numeric(14,2) not null
net_amount numeric(14,2) not null
order_count integer not null
session_count integer not null
average_ticket numeric(14,2) not null
updated_at timestamptz not null
```

### PK

```text
primary key(establishment_id, business_date)
```

---

## 19.2 `reporting.product_sales`

```text
establishment_id uuid not null
business_date date not null
product_id uuid not null
variant_id uuid nullable
quantity numeric(14,3) not null
gross_amount numeric(14,2) not null
discount_amount numeric(14,2) not null
net_amount numeric(14,2) not null
updated_at timestamptz not null
```

---

## 19.3 `reporting.kitchen_performance`

```text
establishment_id uuid not null
business_date date not null
station_id uuid not null
accepted_count integer not null
completed_count integer not null
average_wait_seconds numeric(14,2) not null
average_preparation_seconds numeric(14,2) not null
pause_seconds bigint not null
restart_count integer not null
cancellation_count integer not null
updated_at timestamptz not null
```

---

## 19.4 `reporting.communication_metrics`

```text
establishment_id uuid not null
communication_id uuid not null
business_date date not null
display_count bigint not null
completed_playback_count bigint not null
displayed_seconds bigint not null
updated_at timestamptz not null
```

---

# 20. Índices Obrigatórios

Além dos índices já listados, criar índices adequados para os principais filtros operacionais.

## Mesas/sessões

```text
tables.table_session(establishment_id, status)
tables.table_session(dining_table_id, status)
```

## Pedidos

```text
ordering.customer_order(table_session_id, submitted_at)
ordering.customer_order(establishment_id, order_number)
ordering.order_item(order_id)
```

## Produção

```text
kitchen.production_item(station_id, status, queue_position)
kitchen.production_item(order_item_id)
kitchen.production_status_history(production_item_id, changed_at)
```

## Pagamentos

```text
payments.payment(table_session_id, status)
payments.payment_attempt(payment_id, status)
payments.payment_reservation(table_session_id, released_at)
payments.refund(payment_id, status)
```

## Promoções

```text
promotions.promotion(establishment_id, status, start_at, end_at)
promotions.promotion_application(order_id)
```

## Dispositivos

```text
devices.device(establishment_id, status)
devices.device(last_seen_at)
devices.device_table_binding(dining_table_id, unbound_at)
```

## Auditoria

```text
auditing.audit_entry(establishment_id, occurred_at)
auditing.audit_entry(resource_type, resource_id, occurred_at)
```

---

# 21. Constraints Importantes

## 21.1 Sessão ativa única por mesa

Índice único parcial descrito anteriormente.

## 21.2 Vínculo ativo único por dispositivo

Índice único parcial em `device_table_binding`.

## 21.3 Limite de tablets por mesa

Não usar constraint fixa.

Validar transacionalmente com:

```text
establishments.setting
devices.max_active_table_devices_per_table
```

Deve haver proteção contra concorrência para dois vínculos simultâneos.

## 21.4 Solicitação pendente por item

Impedir múltiplos requests incompatíveis pendentes.

## 21.5 Valores monetários

Garantir não negativos quando aplicável.

## 21.6 Frações

Validar numerador/denominador no banco.
Validar soma e regras de sabores no domínio.

## 21.7 Promoção

`end_at > start_at` quando ambos existirem.

## 21.8 Pagamento

`approved_amount <= amount`, salvo regra explícita futura.

## 21.9 Estorno

Soma de estornos concluídos não pode ultrapassar o valor aprovado do pagamento.
Essa regra deve ser garantida transacionalmente na aplicação.

---

# 22. Relacionamentos Centrais

```text
establishments.establishment
├── identity.user
├── tables.dining_table
├── devices.device
├── catalog.product
├── catalog.ingredient
├── promotions.promotion
└── communications.communication
```

```text
tables.dining_table
└── tables.table_session
    ├── ordering.customer_order
    │   └── ordering.order_item
    │       ├── ordering.order_item_snapshot
    │       ├── ordering.pizza_configuration
    │       └── kitchen.production_item
    ├── payments.payment
    ├── payments.payment_participant
    ├── operations.occurrence
    └── tables.session_customer_identification
```

```text
catalog.product
├── catalog.product_variant
├── catalog.product_ingredient
├── catalog.product_customization_group
├── catalog.pizza_flavor
└── catalog.combo
```

```text
catalog.ingredient
└── catalog.product_ingredient
```

---

# 23. Regras de Propriedade por Módulo

## Catalog
É dono de:
- produtos;
- variações;
- ingredientes;
- personalizações;
- regras de pizza;
- combos.

Não altera pedidos históricos.

## Ordering
É dono de:
- pedido;
- item;
- snapshot;
- configuração vendida;
- solicitações de alteração/cancelamento.

## Kitchen
É dono de:
- produção;
- tentativas;
- pausas;
- entrega operacional.

Não altera diretamente valores comerciais do pedido.

## Tables
É dono de:
- mesa;
- sessão;
- transferência;
- estado da sessão;
- identificação opcional.

## Payments
É dono de:
- plano de pagamento;
- participantes;
- tentativa;
- pagamento;
- reserva;
- alocação;
- estorno;
- desconto manual financeiro.

## Promotions
Calcula e versiona benefícios.
Ordering persiste a aplicação final da promoção no pedido.

## Media
É dono dos metadados de assets e do contrato de object storage.
Establishments, Identity, Catalog e Communications apenas referenciam assets.

## Reporting
Somente leitura/projeção.

## Auditing
Registra ação; não decide regra de negócio.

---

# 24. Convenções EF Core

## 24.1 Mapeamento

Cada módulo deve possuir suas próprias configurações EF Core.

Preferir:

```text
IEntityTypeConfiguration<T>
```

Evitar mapping gigante no `DbContext`.

## 24.2 Schema explícito

Toda entidade deve definir seu schema.

Exemplo conceitual:

```csharp
builder.ToTable("product", "catalog");
```

## 24.3 Enum para string

Persistir enums estruturais como string.

## 24.4 Money

Configurar precisão explicitamente:

```text
numeric(14,2)
```

## 24.5 Concorrência

Mapear `version` como token de concorrência.
Um interceptor/camada de persistência do EF Core incrementa o valor em atualizações relevantes.
Não usar trigger PostgreSQL nem exigir incremento manual pelo domínio.

## 24.6 Migrations

Migrations devem:
- ser versionadas;
- nunca editar migration já aplicada;
- incluir constraints e índices relevantes;
- evitar perda silenciosa de dados.

## 24.7 Delete behavior

Evitar cascade delete em histórico operacional.

Cascade pode ser usado apenas em estruturas puramente dependentes e não históricas, após análise.

Por padrão:
- `Restrict`
- ou `NoAction`

para entidades de negócio relevantes.

---

# 25. Decisões Pendentes para Implementação Física

Este documento define o modelo conceitual.
Antes de gerar migrations finais, o Codex deve confirmar apenas decisões físicas que não alterem negócio, como:

- nomes finais de alguns índices;
- comprimento exato de campos textuais não críticos;
- uso de extensão PostgreSQL específica, se necessário;
- estratégia exata de sequence para `order_number`;
- estratégia criptográfica para campos sensíveis.

Essas decisões não podem contradizer as regras deste documento.

---

# 26. Checklist de Validação do Modelo

Antes de considerar o modelo implementado, validar:

- [ ] Um ingrediente pode estar em vários produtos.
- [ ] Um produto pode ter vários ingredientes.
- [ ] Uma mesa não tem duas sessões ativas.
- [ ] Um dispositivo não tem dois vínculos ativos.
- [ ] Uma mesa pode ter N dispositivos conforme configuração.
- [ ] Pedido antigo não muda com alteração de catálogo.
- [ ] Promoção antiga não muda após edição.
- [ ] Produção possui tentativas e pausas históricas.
- [ ] Pagamento desconhecido não reduz saldo definitivamente.
- [ ] Estorno não apaga pagamento.
- [ ] CPF não aparece em logs ou auditoria comum.
- [ ] Relatórios não são fonte de verdade.
- [ ] Outbox/Inbox suportam idempotência.
- [ ] Status estruturais são validados.
- [ ] JSONB não substitui o modelo relacional central.
