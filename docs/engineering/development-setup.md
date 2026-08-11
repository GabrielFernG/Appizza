# Ambiente de Development

## Pré-requisitos

- .NET SDK definido em `global.json`, com workloads Android e MAUI Windows;
- Node.js 22 e npm;
- Docker Desktop ou outro daemon compatível com Docker Compose.

## Segredos locais

Copie `.env.example` para `.env` e substitua todos os valores de exemplo. O arquivo `.env` não é versionado. A API e o Worker recebem a connection string e as credenciais S3 por variáveis de ambiente ou .NET User Secrets; nenhum segredo deve ser gravado em `appsettings*.json`.

Chaves esperadas: `ConnectionStrings__Appizza`, `ObjectStorage__Endpoint`, `ObjectStorage__Bucket`, `ObjectStorage__AccessKey`, `ObjectStorage__SecretKey` e `ObjectStorage__UsePathStyle`. `OpenTelemetry__OtlpEndpoint` é opcional; sem ele, a instrumentação continua ativa sem exportador externo.

## Infraestrutura local

`docker compose --env-file .env up -d` inicia PostgreSQL 18.4 e SeaweedFS. O SeaweedFS expõe sua API S3 em `http://localhost:8333`; ele é somente o provider local de Development e não faz parte dos contratos de Application ou Domain.

## Banco e migrations

O histórico central fica no projeto `Appizza.Persistence`, usando `AppizzaDbContext` e a tabela `integration.__ef_migrations_history`.

```powershell
dotnet tool restore
dotnet ef database update --project src/BuildingBlocks/Appizza.Persistence --startup-project src/Backend/Appizza.Api
```

## Validação

```powershell
dotnet build Appizza.slnx
dotnet test Appizza.slnx --no-build
npm --prefix src/Web/Appizza.Operations run lint
npm --prefix src/Web/Appizza.Operations run test
npm --prefix src/Web/Appizza.Operations run build
```

Os testes com PostgreSQL em container são habilitados com `APPIZZA_RUN_CONTAINER_TESTS=true`; eles exigem um daemon Docker acessível. O round-trip real do object storage é habilitado com `APPIZZA_RUN_OBJECT_STORAGE_TESTS=true` e recebe endpoint, bucket e credenciais pelas variáveis `APPIZZA_OBJECT_STORAGE_ENDPOINT`, `APPIZZA_OBJECT_STORAGE_BUCKET`, `APPIZZA_OBJECT_STORAGE_ACCESS_KEY` e `APPIZZA_OBJECT_STORAGE_SECRET_KEY`.

## Seed da Fase 1

O seed de exemplo é exclusivo de Development e opt-in. Defina `DevelopmentSeed__Enabled=true` e
`DevelopmentSeed__AdminPassword` via User Secrets/ambiente depois de aplicar as migrations. Produção
não cria estabelecimento ou superadmin automaticamente; onboarding futuro será bootstrap controlado.

As chaves `Phase1Security__SigningKey`, `Phase1Security__CpfEncryptionKey` e
`Phase1Security__CpfHmacKey` são secrets obrigatórios. As duas últimas são Base64; a chave AES deve
ter exatamente 32 bytes e a chave HMAC ao menos 32 bytes.
