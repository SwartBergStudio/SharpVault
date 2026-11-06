# Table Storage

SharpVault stores data in Azure Table Storage through `Azure.Data.Tables`, registered by `SharpVault.Infrastructure` with the Aspire Azure Data Tables client. There is no SQL database and no EF Core.

## Configuration

The service is configured by `ConnectionStrings:tables`.

| Environment | Allowed value | Authentication |
|---|---|---|
| Production (and any non-Development environment) | An absolute `https` service URI with no query string and no user information, for example `https://<account>.table.core.windows.net/` | `DefaultAzureCredential` (managed identity in Azure) |
| Development | A service URI, or a connection string such as the Azurite connection string that the AppHost provides | `DefaultAzureCredential` for a URI. The connection string carries its own credentials |

Outside Development the following are rejected at startup: connection strings, account keys and SAS tokens. A service URI with a query string (a SAS token) or user information is also rejected.

If `ConnectionStrings:tables` is missing, startup fails in every environment.

Startup failures are thrown by `TableStorageSettingsGuard`, which runs while the Aspire client is registered, so the app does not start. The messages are constants and never contain the configured value. The messages are listed in `TableStorageMessages`.

Only the guard reads the environment for storage rules. Store code does not check `IHostEnvironment`.

## Role

The API identity needs the **Storage Table Data Contributor** role on the storage account. Production uses managed identity, so no account keys or SAS tokens are stored in configuration.

Role assignments belong to the deployment work, which is not in this repository yet.

Start-up lists the tables before it creates the missing ones, so the identity must also be allowed to list tables. Whether `Storage Table Data Contributor` allows that listing on a real account is not verified. If start-up fails with an authorization error, check whether the identity needs an account-level role. Tests run on Azurite only.

## Tables

Tables are listed in `TableNames.All`: `Users`, `AuditLog`, `Vaults`, `SecretAuditLog`, `Maintenance`, `ShareInbox`, `TransferJobs` and `SessionRevocations`. At startup the API lists the existing tables once, then creates only the missing ones. A restart therefore makes one list call, not one failing create per table.

| Table | Partition key | Row key | Purpose |
|---|---|---|---|
| `Users` | `user` | user id (`D`) | One row per user, written on sign-in with ETag concurrency. Holds the directory data and the lock status. |
| `AuditLog` | month, `yyyyMM` (UTC) | inverted ticks (19 digits) and entry id (`N`) | Append-only audit entries: event type, actor, target, time, IP address, reason, and for vault events the owner id and secret id. Never secret values. |
| `Vaults` | owner object id (`D`) | see [Vaults table](#vaults-table) | Secrets, folders, history versions, recycle bin rows, share grants, transfer staging rows and the vault key ring. |
| `SecretAuditLog` | owner object id (`D`) | `{secretId:N}_{invertedTicks:D19}_{entryId:N}` | Per-secret index of the same entries, newest first. Insert only. |
| `Maintenance` | `lease` | lease name, for example `recycle-bin-purge` or `transfer-jobs` | Leases that let one instance run a background job at a time. The recycle bin purge also keeps its resume checkpoint (`RunId`, `AfterUserId`). |
| `ShareInbox` | recipient object id (`D`) | `{ownerId:N}_{kind}_{itemId:N}`, where kind is `s` (secret) or `f` (folder) | One row per share grant a user holds, with the folder the recipient filed it under (`FiledFolderKey`). Written with the grant. |
| `TransferJobs` | `transfer` for jobs and source markers, `notice_{userId:D}` for notices | `job_{id}` for a job, `src_{sourceUserId:N}` for the open-transfer marker, and the transfer id for a notice | Transfer jobs with their state, one marker per source user with an open transfer, and the in-app notices. The job and notice ids are stored with their hex digits inverted (`TransferKeys.Complement`). |
| `SessionRevocations` | UTC day the session expires (`yyyyMMdd`) | `{userId:N}_{sessionId}` | One row per web session that was signed out or ended by the server idle limit. A lock ends the session in the Web only, through its own per-process store, and writes no row here. It holds only the user id, a random session id and the day. No token, name or secret. Checked by point read. Rows are deleted by a purge that runs on the recycle bin purge schedule and sweeps the 30 days before today. |

Each `AuditLog` write adds one row, and a vault event with an owner and a secret id also adds one `SecretAuditLog` row. The two inserts are separate requests with no transaction. If the second insert fails, the exception propagates.

Table names must:

- be 3 to 63 letters or digits and start with a letter,
- not be `tables`, which Azure reserves,
- be unique, ignoring case.

`TableStorageTableSet` checks these rules at startup and throws for an invalid list.

### Vaults table

All keys come from `VaultKeys`. The partition key is the owner's object id in `D` format, so one owner's rows never share a partition with another owner's rows.

| Row key | Row | What it holds |
|---|---|---|
| `vault` | Key ring | `DataKeyId`, `DataKeyAlgorithm`, `WrappedDataKey` (the metadata key), `MetadataKeyVersion`, `CreatedAt`. One per owner. |
| `s_{secretId:N}` | Active secret | The columns below |
| `bs_{secretId:N}` | Recycled secret | The same columns, plus `DeletedAt` and `DeletionBatchId` |
| `f_{folderId:N}` | Active folder | `ParentKey` (`root` or the parent folder id), `NameEnvelope`, `MetadataKeyVersion`, `CreatedAt`, `ModifiedAt` |
| `bf_{folderId:N}` | Recycled folder | The same columns, plus `DeletedAt` and `DeletionBatchId` |
| `h_{secretId:N}_{versionId:N}` | History version | `SecretId`, `ValueEnvelope`, `Revision`, `ReplacedAt` |
| `gi_{s or f}_{itemId:N}_{recipientId:N}` | Share grant, by item | The grant. Used for the owner's views, and to revoke or change it |
| `gr_{recipientId:N}_{s or f}_{itemId:N}` | Share grant, by recipient | The same grant, keyed by recipient. Used for the recipient's lists and folder ranges. Both grant rows are written in one transaction |
| `x_{transferId:N}_f_{folderId:N}` | Transfer staging, folder | A folder copy re-encrypted under the target's keys. It is in the target's partition, outside every vault read range |
| `x_{transferId:N}_s_{secretId:N}` | Transfer staging, secret | A secret copy, with the same rule. The publish replaces it with the real row in the same transaction |
| `x_{transferId:N}_k_{secretId:N}` | Transfer staging, skipped secret | A marker for a secret that stays with the source. Removed when the transfer completes |
| `x_{transferId:N}_h_{secretId:N}_{versionId:N}` | Transfer staging, version | A staged history version of a staged secret |
| `x_{transferId:N}-fence` | Transfer staging fence | One row per transfer, in the target's partition. A stage pass takes it with an unconditional replace. Every stage transaction starts with an ETag-checked update of it, and completion, abort and cleanup delete it. It is outside every read range, is not in the staging index and holds no content |

Secret columns: `Type` (the enum name), `FolderKey` (`root` or the folder id), `NameEnvelope`, `UsernameEnvelope`, `UrlEnvelope`, `TagsEnvelope`, `ValueEnvelope`, `NotesEnvelope`, `DataKeyId`, `DataKeyAlgorithm`, `WrappedDataKey` (the per-secret data key), `MetadataKeyVersion`, `Revision`, `CreatedAt`, `ModifiedAt`, `DeletedAt` and `DeletionBatchId` (recycled rows only).

What is stored in clear: the ids in row keys and reference columns (`FolderKey`, `ParentKey`, `SecretId`), `Type`, the dates, `Revision`, `MetadataKeyVersion`, `DeletionBatchId`, and the key metadata (`DataKeyId`, `DataKeyAlgorithm`). The wrapped keys are bytes that only the Key Vault key or the development key can unwrap. Names, usernames, URLs, tags, folder names, values and notes are envelopes (binary, see [ENCRYPTION.md](ENCRYPTION.md)). Nothing in the table holds a secret value in clear.

Bin rows use their own prefix, so active listings never read deleted rows. The envelope binds the purpose, the item id and the owner, never the row key, so moving a row between `s_` and `bs_` needs no re-encryption.

## Rules for queries

- Every query includes a partition key condition. A query without one is a table scan. The test helper `StorageRequestRecorder.QueriesWithoutPartitionKey()` reports such queries, and the tests require none. Point reads and every vault range query carry `PartitionKey eq {owner}`.
- Vault range queries are: the folder range (`f_`), secret range pages (optionally with a `FolderKey` filter), the version range of one secret (`h_{secretId}_`), bin range pages (`b` to `c`, optionally with a `DeletionBatchId` or `DeletedAt` filter). Grant ranges (`gi_` by item, `gr_` by recipient, and the recipient's folder range) and transfer staging ranges (`x_{transferId}_`) are range queries too, and each keeps its partition key condition.
- Every update uses an ETag for optimistic concurrency.
- Store interfaces sit between the application and Table Storage: `IVaultStore`, `IUserStore`, `IAuditLog` and `IMaintenanceLeaseStore`. Handlers do not reference `TableClient`.

## Transactions and concurrency (vault)

The vault writes through one method, `IVaultStore.SubmitAsync(owner, changes)`. Each change becomes one table action, or a pair for a recycle or a restore (a delete plus an add). The rules:

- A transaction holds at most 100 actions and about 3 MiB of estimated payload. The estimate is 2,048 bytes per action plus the base64 size of binary values. A change's actions are never split across transactions.
- A structural folder change (create, move, delete, or restoring a folder batch) first updates the key ring row with `UpdateMerge` and the ETag read at the start of the handler. This works as one structure lock per vault, so two structural changes cannot both commit.
- Putting a secret into a folder updates that folder row with `UpdateMerge`, so a concurrent delete, move or rename of the folder conflicts.
- Secret edits and folder renames check an ETag. A stale ETag returns 409 and changes nothing.
- When a write needs more than one transaction, each transaction is atomic and the earlier ones stay applied if a later one fails. The response is 409, and repeating the request finishes the job, because each handler recomputes the current state.
- Transaction failure statuses map to: 412 to conflict, 404 to not found, 409 to already exists. Any other failure is an error.
- A purge of one secret, with its history rows, is one transaction, because a secret has at most 21 rows in its partition group.
- Transfer staging is fenced. A stage transaction holds at most 99 changes plus the fence update, which is the first action of the transaction. If the fence has been taken over by another runner, the whole transaction fails and writes nothing (see the fence row above and [TRANSFERS.md](TRANSFERS.md#crash-behaviour-and-retries)).

## Adding a table

1. Add a `const string` with the table name to `TableNames`, and add it to `TableNames.All`.
2. Document the table in the Tables section above: name, partition key, row key and what it stores. Do not store secret values in clear text.
3. Add the store interface and its implementation, with a unit test for each failure path and an integration test that checks the storage call count and that no query runs without a partition key (see [TESTING.md](TESTING.md)).
4. Check that `TableNames.All` and the Table Storage startup tests still pass.

## Local development

The AppHost runs Azurite as an emulator with a persistent data volume. See [LOCAL-DEV.md](LOCAL-DEV.md) for how to inspect or wipe that data.
