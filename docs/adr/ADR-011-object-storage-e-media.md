# ADR-011 — Object Storage e Ownership de Mídia

Status: Aceito.

O código de aplicação acessa arquivos por uma abstração `IObjectStorage` compatível com S3.
SeaweedFS é o provider exclusivo do ambiente local de Development e é acessado pela API S3.
O provider de produção não está definido.

Tipos específicos do SeaweedFS não podem aparecer em Domain ou Application.

Mídia pertence ao módulo neutro `Media`. Seus metadados ficam em `media.asset` e os binários no
object storage. Establishments, Identity, Catalog e Communications apenas referenciam assets.

A migration `Foundation` não cria tabelas funcionais de mídia.
