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

## Tables

Tables are listed in `TableNames.All`. Today that is `Users` (one row per user, written on sign-in with ETag concurrency) and `AuditLog` (append-only audit entries). They are created once at startup.

| Table | Partition key | Row key | Purpose |
|---|---|---|---|
| (none yet) | | | |

At startup, `TableStorageInitializer` calls `CreateTableIfNotExists` once for each name in `TableNames.All`. Startup fails if a call fails. Tables are never created per request.

Table names must:

- be 3 to 63 letters or digits and start with a letter,
- not be `tables`, which Azure reserves,
- be unique, ignoring case.

`TableStorageTableSet` checks these rules at startup and throws for an invalid list.

## Rules for queries

- Every query includes a partition key condition. A query without one is a table scan. The test helper `StorageRequestRecorder.QueriesWithoutPartitionKey()` reports such queries, and the tests require none.
- Every update uses an ETag for optimistic concurrency.
- Store interfaces sit between the application and Table Storage. The foundation has no store interfaces yet, because the first feature that needs one adds it.

## Adding a table

1. Add a `const string` with the table name to `TableNames`, and add it to `TableNames.All`.
2. Document the table in the Tables section above: name, partition key, row key and what it stores. Do not store secret values in clear text.
3. Add the store interface and its implementation, with a unit test for each failure path and an integration test that checks the storage call count and that no query runs without a partition key (see [TESTING.md](TESTING.md)).
4. Check that `TableNames.All` and the Table Storage startup tests still pass.

## Local development

The AppHost runs Azurite as an emulator with a persistent data volume. See [LOCAL-DEV.md](LOCAL-DEV.md) for how to inspect or wipe that data.
