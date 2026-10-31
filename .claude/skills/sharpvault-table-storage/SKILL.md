---
name: sharpvault-table-storage
description: SharpVault persistence with Azure Table Storage (Azure.Data.Tables). Use when designing tables, partition and row keys, writing store implementations in SharpVault.Infrastructure, wiring storage in Aspire, or handling concurrency and multi-entity operations. Replaces EF Core guidance in this repository.
---

# Azure Table Storage in SharpVault

SharpVault has no SQL database and no EF Core. All data is stored in Azure Table Storage.

## Layering

- `SharpVault.Domain` holds plain entities with no storage concerns. No `ITableEntity` in the domain.
- `SharpVault.Application/Common/Interfaces` declares store interfaces per aggregate: `ISecretStore`, `ISecretShareStore`, `IUserStore`, `IAuditLog`, `ITransferJobStore`.
- `SharpVault.Infrastructure/Storage` implements them with `TableClient`. Each store has a private table entity class (`SecretTableEntity : ITableEntity`) and maps to and from the domain entity with Mapster.
- Handlers only ever see store interfaces and domain entities.

## Tables and keys

Choose keys for the queries you need, because a query without the partition key scans the table.

| Table | PartitionKey | RowKey | Main queries |
|---|---|---|---|
| `Secrets` | owner user id (Entra `oid`) | secret id | list a user's secrets, get one secret |
| `SecretShares` | recipient user id | secret id | secrets shared with me |
| `SecretSharesBySecret` | secret id | recipient user id | who a secret is shared with |
| `Users` | `user` | user id | admin user list |
| `AuditLog` | `yyyyMM` | inverted ticks + `_` + id | newest events first per month |
| `TransferJobs` | `transfer` | job id | job status and resume |

- Inverted ticks: `(DateTime.MaxValue.Ticks - timestamp.Ticks).ToString("D19")` so the newest rows sort first.
- Keep table and key names in a `TableNames` constants class.
- When data needs two access paths, write both rows (for example `SecretShares` and `SecretSharesBySecret`) and keep them in step.
- Document every table in `docs/STORAGE.md`.

## Concurrency and consistency

- Every update uses the entity's `ETag` with `TableUpdateMode.Replace` and `ifMatch: etag`. On `412 Precondition Failed` return a conflict result. Never update with `ETag.All` except in data migrations.
- Transactions (`SubmitTransactionAsync`) only work within one partition and up to 100 operations. Use them for multi-row changes in one partition.
- Changes across partitions (such as moving a secret to a new owner) are not atomic. Make them idempotent and resumable: record intent first (for example a `TransferJob`), then copy, verify, delete the source and mark progress, so a retry can finish safely.

## Values and limits

- An entity is at most 1 MB and 252 custom properties; a string property is at most 64 KB. Encrypted values are stored as `byte[]` or Base64 strings within those limits.
- Store `DateTimeOffset` in UTC. Store enums as strings.
- Never store a decrypted secret value. Store the ciphertext, nonce, wrapped data key and key version.

## Paging

- Query with `QueryAsync<T>(filter, maxPerPage)` and page with `AsPages(continuationToken)`. Return the continuation token to the caller instead of skip and take.
- Build filters with `TableClient.CreateQueryFilter($"PartitionKey eq {userId}")` so values are escaped. Never concatenate filter strings.

## Authentication and wiring

- Production uses `TableServiceClient` with the account endpoint and `DefaultAzureCredential`. Shared key access is disabled on the storage account, and the app identity has the "Storage Table Data Contributor" role.
- Locally, Aspire provides the Azurite connection: in `SharpVault.AppHost`

  ```csharp
  var tables = builder.AddAzureStorage("storage")
      .RunAsEmulator(emulator => emulator
          .WithDataVolume()
          .WithLifetime(ContainerLifetime.Persistent))
      .AddTables("tables");
  ```

  and in the API `builder.AddAzureTableServiceClient("tables")`. The same code then works against Azurite locally and the real account in Azure, with no environment checks in the stores.
- Create tables on start with `CreateIfNotExistsAsync` in a hosted initializer, not on every request.
