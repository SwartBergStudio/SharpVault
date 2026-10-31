# Plan: vault and folders

- **Branch**: `feature/vault-and-folders` (already created from `main`, which holds the foundation and the encryption core)
- **PROGRESS.md item**: Next, item 4
- **Prerequisite (T0)**: `feature/entra-sign-in` must be merged into `main` first, and `main` merged into this branch, before any implementation task starts. This plan uses its types: `ICurrentUser`, `Policies`, `IUserStore`, `IAuditLog`, `AuditEntry`, `AuditEventType`, `Result<T>`, `Versioned<T>`, `StoreWriteResult`, the access gate, `TestUsers`, `TestTokens`, `CallerKind`, `CallerClients`, `ApiTestFactory`, `ApiSecurityTestBase`, `ThrowingAuditLog` and `StorageRequestRecorder`.
- **Planner verification**: items marked "verified" were run on .NET SDK 10.0.114 (Linux, Docker running) in throwaway projects in the planner scratchpad. The implementer can rely on them. Foundation conventions still apply: the AGPL header on every `.cs` file; no comments, `#region`, TODOs or commented-out code; classes `sealed`, abstract or static; CA1848 needs `[LoggerMessage]`; no type name ends in `Collection` (CA1711); xUnit1051 needs `TestContext.Current.CancellationToken`; enums whose default could grant access put the fail-closed value at 0 or leave 0 undefined.

## 1. Goal

The first real feature: a user's own vault.

- **Secrets** (SEC-1): name, type, username, value, URL, notes, tags, folder, created and modified dates. Every text field is encrypted (SEC-2). Values and notes are never in list or detail responses. They come only from an explicit reveal or copy, and every reveal and copy is audited before the value is returned (SEC-3).
- **Folders** (FLD-1 to FLD-3): nested folders up to 10 levels deep, with no cycles, even under concurrent moves. Deleting a folder requires a choice: move its contents to the parent, or delete them.
- **History** (SEC-5): the last 20 previous values of each secret. The owner can see when they changed, reveal them (audited) and restore one.
- **Recycle bin** (FLD-3): deleted secrets and folders stay restorable for 30 days. A background service purges them after that. It is idempotent, resumable and lease-protected.
- **Password generator** (SEC-4): random passwords and passphrases (EFF large word list) built on `RandomNumberGenerator`, with an entropy report, in a small shared library used by the UI.
- **UI** in `SharpVault.UI.Shared` covering vault list, folder tree, secret detail with hidden values, reveal and copy with a clipboard that clears itself, editor with the generator dialog, history and recycle bin. Every interactive element has a stable `id`.
- **Audit** (AUD-2, AUD-3): create, change, move, delete, restore, purge, reveal and copy. AUD-5 groundwork is a per-owner, per-secret audit index table.
- **Hooks for later features**: a share check in every secret handler (`ISecretAccessPolicy`, for `feature/sharing`), and a data layout that `feature/offboarding-transfers` can re-encrypt for a new owner (D2).

### Out of scope

- Sharing with colleagues, folder shares and "Shared with me" (`feature/sharing`). This feature adds only the `ISecretAccessPolicy` hook, with an owner-only implementation.
- One-time links (`feature/one-time-links`).
- Transfers, and the cross-session `ReEncryptTo` member that transfers need (`feature/offboarding-transfers`, D2).
- Reading the AUD-5 history (endpoint and UI) and the admin audit search (`feature/audit-log`). This feature only writes the index rows.
- Per-user secret and share counts for admins (VIS-3).
- Admin settings for the clipboard clear timeout and idle auto-lock (ADM-4). The UI uses fixed defaults through a settings record that ADM-4 will replace.
- Idle auto-lock of the UI.
- Structured card fields (number, expiry, CVV), TOTP, attachments, import and export.
- Playwright E2E tests. They are deferred to a dedicated follow-up (D21). UI behaviour is covered by bUnit component tests.
- The MAUI clipboard implementation (`feature/maui-app`).

## 2. Requirements

| Id | Delivered by this work |
|---|---|
| SEC-1 | Secret fields: name, type (Login, SecureNote, ApiKey, Card, Other), username, value, URL, notes, tags, folder, created and modified. |
| SEC-2 | Value, notes, username, name, URL, tags and folder names are all encrypted with AES-256-GCM envelopes, with purpose, item id and owner id bound as associated data (section 4.3). Only ids, type, folder id, dates, revision and key metadata are stored in clear. |
| SEC-3 | List and detail responses never carry a value or notes. `POST .../reveal` with an intent of `View` or `Copy` returns one field and writes the audit entry before it responds. If the audit write fails, the value is not returned. Reveals are rate limited per user. |
| SEC-4 | `SharpVault.PasswordGeneration` builds passwords of 8 to 128 characters from up to four character sets, with optional exclusion of ambiguous characters and a guarantee of at least one character from every chosen set. Passphrases are 3 to 20 words from the EFF large list. It uses `RandomNumberGenerator.GetString` and `GetInt32` (uniform by rejection sampling) and reports exact entropy. |
| SEC-5 | Every value change keeps the previous value as a history version (up to 20 per secret), re-encrypted under its own version id. The owner can list, reveal and restore versions. |
| FLD-1 | Nested folders, at most 10 levels and 1,000 folders per vault. Cycles are rejected, and concurrent structural changes are serialised by a vault-level ETag. |
| FLD-2 | A secret has at most one folder of its owner, and its folder id is checked inside the owner's partition. Moves change only `FolderId`, so later shares, which are keyed by secret id, are not affected. |
| FLD-3 | Folder deletion requires `MoveToParent` or `DeleteContents`, with no default. Deleted items go to the recycle bin for 30 days and can be restored (with fallback to the root when the original parent is gone). They are then purged by a background service. |
| VIS-1 | Every store call takes the owner id as the partition key, and the handlers take that owner id from `ICurrentUser` (through the share hook). A user only ever reads their own partition. |
| VIS-2 | `ICurrentUser.IsAdmin` is never consulted by vault code (a source scan test checks this). An admin who is not the owner gets 404 on every item endpoint, and an admin's own vault behaves like any user's. |
| VIS-4 | The only path to another owner's data is `ISecretAccessPolicy`, and today it grants owner access only. |
| LCK-1, TEN-4 | Every new endpoint sits behind `Policies.User` and the existing access gate. Locked users get 403. |
| AUD-1 | Audit rows stay insert-only. `IAuditLog` keeps exactly one `AppendAsync` method, and the new index table is written with `AddEntityAsync` only. |
| AUD-2 | New audit events for create, change, move, delete, restore, purge, reveal and copy of secrets and history versions, and for folder create, rename, move, delete, restore and purge. |
| AUD-3 | Vault audit entries hold event type, actor, owner, target id, secret id, time and IP. There is never a name, value, note, username, URL, tag or folder name. Tests scan for planted markers. |
| AUD-5 (groundwork) | `SecretAuditLog` table (PK owner oid, RK secret id, inverted ticks and entry id), written for every secret event. `feature/audit-log` adds the query endpoint. |
| ADM-4 (groundwork) | The clipboard clear delay and the reveal auto-hide delay come from a `VaultUiSettings` record (30 s each). ADM-4 replaces where it comes from. |
| CLAUDE.md Encryption | Each secret value gets its own random data key. Values, data keys and decrypted content are never logged. Every reveal is audited. |

Pull requests and tests reference these ids.

## 3. Verified facts

| # | Fact | How |
|---|---|---|
| V1 | Azurite 3.35.0 with `Azure.Data.Tables` 12.13.0: in an entity group transaction, an `UpdateMerge` of an entity that has only PartitionKey and RowKey, with `ifMatch` set to the current ETag, keeps every stored property and changes the ETag. A second transaction that uses the old ETag fails with `TableTransactionFailedException`, `Status == 412` and `FailedTransactionActionIndex` pointing at the stale action. This "touch" is how folders and the vault key ring are locked (sections 4.5 and 4.6). | Prototype against Azurite |
| V2 | `SubmitTransactionAsync` returns `Response<IReadOnlyList<Response>>`, with one response per action **in action order**. Each has `Headers.ETag`. | Prototype |
| V3 | A transaction `Delete` of a missing entity fails with `Status == 404`. `Delete` of the active row plus `Add` of the recycle bin row in one transaction works. | Prototype |
| V4 | The filter `PartitionKey eq {pk} and RowKey ge 's_' and RowKey lt 's`' and FolderKey eq 'root'`, built with `TableClient.CreateQueryFilter`, returns exactly the matching rows. `` ` `` (0x60) is the character after `_` (0x5F). | Prototype |
| V5 | An entity with two 32,797-byte binary properties (two maximum-size envelopes) is stored and read back. | Prototype |
| V6 | A transaction with 101 actions throws `InvalidOperationException` on the client, before any request. | Prototype |
| V7 | Minimal API JSON body binding with `ConfigureHttpJsonOptions` and `new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)`, and `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` on the body record, returns **400** with a generic ProblemDetails for an unknown member (`ownerId`), an integer enum (`"type":0`), an unknown enum name, and a lone surrogate escape (`"\ud800"`). Enum names are matched case-insensitively (`"login"` binds). | Prototype with TestServer |
| V8 | `bunit` 2.13.3 (targets net10.0, Components 10.0.13) works with `xunit.v3.mtp-off` 4.0.2 and Components.Web 10.0.14. `dotnet list package --vulnerable --include-transitive` reports nothing. A `.razor` file needs `@using Microsoft.AspNetCore.Components.Web` for `@onclick` to compile (UI.Shared `_Imports.razor` already has it). | Prototype |
| V9 | `Microsoft.Extensions.Http.Resilience` 10.11.0 has `RemoveAllResilienceHandlers()` and `Retry.DisableForUnsafeHttpMethods()`. | Package XML docs |
| V10 | `RandomNumberGenerator.GetString(ReadOnlySpan<char>, int)` exists on .NET 10. | Prototype |
| V11 | CA1819 (array properties) does **not** fire under this repository's `AnalysisLevel` for `public byte[] X { get; set; }` on a sealed class. | Build with the repository props |
| V12 | The EFF large word list mirrored in the `ulif/diceware` project (`diceware/wordlists/wordlist_en_eff.txt`) is 108,800 bytes, has 7,776 lines of the form `{5 dice digits}\t{word}\n`, ends with a newline, has 7,776 unique words of 3 to 9 characters made of `a`-`z` and `-` (4 contain a hyphen: `drop-down`, `felt-tip`, `t-shirt`, `yo-yo`), and has SHA-256 `addd35536511597a02fa0a9ff1e5284677b8883b83e986e43f15a3db996b903e`. `www.eff.org` itself was blocked from this session. | Download in the scratchpad |
| V13 | Chi-square critical values at p = 1e-6: df 9 is 44.8, df 25 is 73.9, df 93 is 172.7 and df 7,775 is 8,382.2. Golden entropies: 20 lowercase characters give 94.0088 bits; 8 characters from lowercase and digits with every set required give 41.2484 bits (41.3594 without the requirement); 20 characters from all four sets (26, 26, 10, 32) with every set required give 130.9259 bits; 8 characters from all four sets required give 51.3183 bits; 6 EFF words give 77.5489 bits; 6 words with a number give 83.4558 bits. | Python in the scratchpad |
| V14 | `SignedInIdentityReader` and `EntraTokenChecks` reject an empty `oid`, so `Guid.Empty` can never be a real user. It is used as the system actor in audit entries (D17). | Source read |

## 4. Design

### 4.1 Where things live

| Layer | Path | Types |
|---|---|---|
| PasswordGeneration (new project) | `src/SharpVault.PasswordGeneration/` | `CharacterSets`, `PassphraseSeparator`, `PasswordOptions`, `PassphraseOptions`, `GeneratedSecret`, `PasswordStrength`, `GeneratorLimits`, `GeneratorMessages`, `CharacterAlphabets`, `EntropyCalculator`, `EffWordList` (internal), `PasswordGenerator`; resource `Wordlists/eff_large_wordlist.txt` |
| Domain | `Vaults/` | `Secret`, `SecretType`, `Folder`, `SecretVersion`, `VaultKeyRing` |
| Domain | `Maintenance/` | `MaintenanceLease` |
| Domain | `Audit/` | `AuditEventType` (new values), `AuditEntry` (`OwnerId`, `SecretId`), `AuditActors` |
| Application | `Common/Interfaces/` | `IVaultStore`, `IMaintenanceLeaseStore`, `IRequestMetadata`, `ISecretAccessPolicy` |
| Application | `Common/Models/` | `ResultFailureKind`, `ResultMessages`, `Done`, `Result<T>` (extended), `VaultChange`, `VaultChangeKind`, `VaultWriteResult`, `SecretFolderFilter`, `SecretFolderScope`, `SecretPage`, `RecycleBinEntry`, `RecycleBinPage`, `RecycledBatch`, `SecretAccessGrant`, `SecretPermission` |
| Application | `Features/Vault/Common/` | `VaultPurposes`, `VaultLimits`, `VaultMessages`, `VaultTextRules`, `ETagRules`, `VaultKeyRingService`, `OpenedVault`, `SecretMetadata`, `SecretMetadataCipher`, `SecretDtoFactory`, `FolderDtoFactory`, `VaultAuditWriter`, `VaultAuditItem`, `OwnerOnlySecretAccessPolicy`, `FolderTree`, `VaultIds` |
| Application | `Features/Vault/Secrets/{Commands,Queries,Dtos}/` | `CreateSecret.cs`, `UpdateSecret.cs`, `MoveSecret.cs`, `DeleteSecret.cs`, `RevealSecretField.cs`, `GetSecret.cs`, `ListSecrets.cs`; `RevealField`, `RevealIntent`; `SecretDto`, `SecretPageDto`, `RevealedValueDto` |
| Application | `Features/Vault/History/{Commands,Queries,Dtos}/` | `RevealSecretVersion.cs`, `RestoreSecretVersion.cs`, `ListSecretVersions.cs`; `SecretVersionDto`, `SecretVersionListDto` |
| Application | `Features/Vault/Folders/{Commands,Queries,Dtos}/` | `CreateFolder.cs`, `RenameFolder.cs`, `MoveFolder.cs`, `DeleteFolder.cs`, `ListFolders.cs`; `FolderDeleteMode`; `FolderDto`, `FolderListDto` |
| Application | `Features/Vault/RecycleBin/` | `Commands/RestoreRecycledSecret.cs`, `RestoreRecycledFolder.cs`, `PurgeRecycledSecret.cs`, `PurgeRecycledFolder.cs`; `Queries/ListRecycleBin.cs`; `Dtos/RecycleBinItemDto`, `RecycleBinPageDto`, `RecycleBinItemKind`; `IRecycleBinPurger`, `RecycleBinPurger`, `RecycleBinPurgeResult`, `RecycledItemEraser` |
| Infrastructure | `Storage/` | `TableNames` (add `Vaults`, `SecretAuditLog`, `Maintenance`) |
| Infrastructure | `Vaults/` | `VaultKeys`, `VaultStorageLimits`, `SecretTableEntity`, `FolderTableEntity`, `SecretVersionTableEntity`, `VaultKeyRingTableEntity` (all internal), `VaultStorageMappingRegister`, `VaultTransactionPlanner` (internal), `PlannedTransaction` (internal), `TableVaultStore` |
| Infrastructure | `Audit/` | `SecretAuditKeys`, `SecretAuditLogTableEntity` (internal), `AuditStorageMappingRegister` (extended), `TableAuditLog` (extended) |
| Infrastructure | `Maintenance/` | `MaintenanceKeys`, `MaintenanceLeaseTableEntity` (internal), `TableMaintenanceLeaseStore`, `RecycleBinPurgeOptions`, `RecycleBinPurgeOptionsValidator`, `RecycleBinPurgeService` |
| Api | `Endpoints/Vault/` | `VaultSecretEndpoints`, `VaultHistoryEndpoints`, `VaultFolderEndpoints`, `RecycleBinEndpoints`, `VaultHeaders` |
| Api | `Endpoints/Vault/Requests/` | `CreateSecretRequest`, `UpdateSecretRequest`, `MoveSecretRequest`, `RevealSecretRequest`, `RevealVersionRequest`, `CreateFolderRequest`, `RenameFolderRequest`, `MoveFolderRequest` |
| Api | `Errors/` | `ResultHttpExtensions` (extended), `SecretDecryptionExceptionHandler` |
| Api | `RateLimiting/` | `RateLimitPolicies`, `VaultRateLimitOptions`, `VaultRateLimitOptionsValidator`, `VaultRateLimitingExtensions` |
| Api | `Json/` | `ApiJsonExtensions` |
| Api | `Security/` | `HttpContextRequestMetadata` |
| UI.Shared | `Models/Vault/` | `SecretKind`, `SecretFieldName`, `RevealPurpose`, `FolderDeleteChoice`, `RecycleBinKind`, `VaultSecret`, `VaultSecretPage`, `SecretDraft`, `SecretChanges`, `RevealedValue`, `VaultFolder`, `SecretVersionInfo`, `RecycleBinEntryModel`, `RecycleBinPageModel` |
| UI.Shared | `Services/` | `ApiCallStatus` (extended), `ApiResponse<T>` (extended), `IVaultApiClient`, `IClipboard`, `SecretClipboard`, `VaultUiSettings`, `SharpVaultUiServiceCollectionExtensions` |
| UI.Shared | `Navigation/` | `VaultRoutes` |
| UI.Shared | `Pages/Vault/` | `VaultPage.razor`, `SecretDetailPage.razor`, `SecretEditorPage.razor`, `SecretHistoryPage.razor`, `RecycleBinPage.razor` |
| UI.Shared | `Components/Vault/` | `FolderTree.razor`, `SecretList.razor`, `MaskedField.razor`, `FolderEditorDialog.razor`, `FolderDeleteDialog.razor`, `MoveSecretDialog.razor`, `PasswordGeneratorDialog.razor` |
| UI.Shared | `Components/Common/` | `ConfirmDialog.razor` |
| Web | `Api/` | `SharpVaultApiTransport`, `ApiJson`, `VaultApiClient`, `SharpVaultApiClient` (refactored), `SharpVaultApiRegistration` (extended) |
| Web | `Clipboard/` | `BrowserClipboard` |
| Web | `wwwroot/js/` | `clipboard.js` |
| Web | `Components/` | `App.razor` (render mode) |

### 4.2 Key decisions in short (details in section 9)

- **D1 Data keys per field** (resolves encryption-core D5): names, usernames, URLs, tags and folder names are encrypted under one **per-user metadata data key**, stored once per vault in the key ring row. Values, notes and history values are encrypted under a **per-secret data key**. A list page costs exactly **1 Key Vault unwrap**, whatever its size.
- **D2 Transfers and sharing with D1** (resolves encryption-core D5a): `feature/offboarding-transfers` adds a cross-session member, `IDataKeySession.ReEncryptTo(EncryptionContext source, IDataKeySession target, EncryptionContext targetContext, byte[] envelope)`, to move metadata fields between the two users' metadata keys without creating a string. Per-secret fields use the existing `ReEncrypt`. Sharing opens the owner's metadata key on the server and decrypts only shared rows. That costs 1 unwrap per distinct owner on a "Shared with me" page.
- **D3 One table**: secrets, folders, history versions, recycle bin rows and the key ring all live in **one table `Vaults`**, partitioned by owner oid and told apart by RowKey prefix. Entity group transactions then make "update value plus write history", "move to bin", and "folder change plus structure lock" atomic.
- **D4 Concurrency**: secret edits use the client's `If-Match` ETag. Structural folder changes touch the key ring row (one structure lock per vault). Putting a secret into a folder touches that folder row, so a concurrent folder delete or move conflicts instead of leaving an orphan.

### 4.3 Encryption

| Field | Stored property | Data key | Purpose (persisted, never change) | ItemId | OwnerId | Plaintext limit |
|---|---|---|---|---|---|---|
| Secret name | `NameEnvelope` (binary, required) | vault metadata key | `secret.name` | secret id | owner oid | 1 to 200 chars |
| Username | `UsernameEnvelope` (binary, optional) | vault metadata key | `secret.username` | secret id | owner oid | up to 256 chars |
| URL | `UrlEnvelope` (binary, optional) | vault metadata key | `secret.url` | secret id | owner oid | up to 2,048 chars, absolute `http` or `https` |
| Tags | `TagsEnvelope` (binary, optional) | vault metadata key | `secret.tags` | secret id | owner oid | JSON array of up to 20 tags, 1 to 50 chars each |
| Value | `ValueEnvelope` (binary, optional) | per-secret key | `secret.value` | secret id | owner oid | 1 to 8,192 chars |
| Notes | `NotesEnvelope` (binary, optional) | per-secret key | `secret.notes` | secret id | owner oid | 1 to 8,192 chars |
| History value | `ValueEnvelope` of the version row | the secret's per-secret key | `secret.value.history` | **version id** (ENC-D24) | owner oid | same as value |
| Folder name | `NameEnvelope` of the folder row | vault metadata key | `folder.name` | folder id | owner oid | 1 to 100 chars |

- The URL is encrypted (D6). It shows which internal systems a user has access to, and SEC-2 keeps the user's data private from admins.
- 8,192 characters is at most 32,768 UTF-8 bytes, so every field stays within `EnvelopeFormat.MaxPlaintextBytes`. A full secret row (two maximum envelopes plus small fields) stays under 70 KB, well inside 1 MB per entity (V5).
- Tags are serialised with `System.Text.Json` (`JsonSerializer.Serialize(IReadOnlyList<string>)`) and then encrypted.
- Names, usernames, URLs, tags and folder names are trimmed before encryption. Values and notes are stored byte-exact, with no trimming or normalisation.
- The vault key ring row stores `MetadataKeyVersion` (always 1 today), and every secret and folder row stores the `MetadataKeyVersion` it was encrypted under. A mismatch reads as damaged (D19). This leaves room for a future metadata key change.
- Unwrap counts: list page 1; list search 1; get 1; reveal 1; history reveal 1; create 1 (0 on the very first create, which creates the metadata key); update 1, or 2 when the value or notes change; folder list, create and rename 1; every other endpoint 0.
- Encryption sessions are always used in a `using` block, inside one handler. Plaintext lives only in local variables and DTOs. Records that carry plaintext override `ToString()` to return the type name only (TH32).

### 4.4 Table design

All keys come from `VaultKeys` (Infrastructure). Filters are always built with `TableClient.CreateQueryFilter`.

**Table `Vaults`**: PartitionKey is the owner oid `ToString("D")`.

| RowKey | Row | Main queries |
|---|---|---|
| `vault` | Key ring: `DataKeyId`, `DataKeyAlgorithm`, `WrappedDataKey` (the metadata key), `MetadataKeyVersion` (int), `CreatedAt` | point read |
| `s_{secretId:N}` | Active secret (columns below) | point read; range `ge 's_' and lt 's`'`, optionally `and FolderKey eq {key}` |
| `bs_{secretId:N}` | Recycled secret: same columns plus `DeletedAt`, `DeletionBatchId` | point read; range `ge 'b' and lt 'c'` |
| `f_{folderId:N}` | Active folder: `ParentKey`, `NameEnvelope`, `MetadataKeyVersion`, `CreatedAt`, `ModifiedAt` | range `ge 'f_' and lt 'f`'` (whole tree in one page) |
| `bf_{folderId:N}` | Recycled folder: same columns plus `DeletedAt`, `DeletionBatchId` | as for `bs_` |
| `h_{secretId:N}_{versionId:N}` | History version: `SecretId`, `ValueEnvelope`, `Revision` (long), `ReplacedAt` | range `ge 'h_{id}_' and lt 'h_{id}`'`; point read |

Secret columns: `Type` (string enum), `FolderKey` (`root` or the folder id as `N`), `NameEnvelope`, `UsernameEnvelope`, `UrlEnvelope`, `TagsEnvelope`, `ValueEnvelope`, `NotesEnvelope` (binary, null when absent), `DataKeyId`, `DataKeyAlgorithm`, `WrappedDataKey` (the per-secret key, ENC section 4.5), `MetadataKeyVersion` (int), `Revision` (long, +1 on every change), `CreatedAt`, `ModifiedAt` (UTC `DateTimeOffset`), and `DeletedAt` and `DeletionBatchId` (bin rows only).

- **Ids** are `Guid.CreateVersion7(timeProvider.GetUtcNow())` for secrets, folders and versions (`VaultIds`). The hex `N` form of a version 7 GUID sorts by creation time, so storage order is creation order (D8). Ids are not secrets: authorisation always comes from the partition.
- **Bin rows** use a different prefix, so active listings never scan deleted rows. Associated data binds ciphertext to the purpose, the item id and the owner, never to the RowKey, so moving a row between `s_` and `bs_` needs no re-encryption.
- Every query has a `PartitionKey eq` condition (TH39). The only queries are: the folder range; secret range pages (optionally with a `FolderKey` filter); the version range of one secret; bin range pages (optionally with `DeletionBatchId eq` or `DeletedAt lt`).

**Table `SecretAuditLog`** (AUD-5 groundwork): PartitionKey is the owner oid `D`. RowKey is `{secretId:N}_{invertedTicks:D19}_{entryId:N}`, newest first per secret. Its columns match `AuditLog` (`EventType`, `ActorId`, `TargetId`, `OccurredAt`, `IpAddress`, `Reason`, `OwnerId`, `SecretId`). It is insert-only.

**Table `Maintenance`**: PartitionKey `lease`, RowKey the lease name (`recycle-bin-purge`). Columns: `Holder` (string), `ExpiresAt`, `RunId` (string, nullable), `AfterUserId` (string, nullable), `RunStartedAt` (nullable) and `LastCompletedAt` (nullable).

`TableNames.All` becomes `[Users, AuditLog, Vaults, SecretAuditLog, Maintenance]`.

### 4.5 Writes, transactions and concurrency

The store's write path is one method, `IVaultStore.SubmitAsync(ownerId, IReadOnlyList<VaultChange> changes, ct)`. Each `VaultChange` maps to one or two transaction actions:

| `VaultChangeKind` | Actions (all in the owner's partition) |
|---|---|
| `AddSecret` | Add `s_` |
| `ReplaceSecret` | UpdateReplace `s_` ifMatch |
| `RecycleSecret` | Delete `s_` ifMatch, then Add `bs_` (`DeletedAt` and `DeletionBatchId` must be set) |
| `RestoreSecret` | Delete `bs_` ifMatch, then Add `s_` (`DeletedAt` and `DeletionBatchId` must be null) |
| `PurgeSecret` | Delete `bs_` ifMatch |
| `AddVersion` | Add `h_` |
| `DeleteVersion` | Delete `h_` ifMatch |
| `AddFolder` | Add `f_` |
| `ReplaceFolder` | UpdateReplace `f_` ifMatch |
| `RecycleFolder` | Delete `f_` ifMatch, then Add `bf_` |
| `RestoreFolder` | Delete `bf_` ifMatch, then Add `f_` |
| `PurgeFolder` | Delete `bf_` ifMatch |
| `TouchFolder` | UpdateMerge `f_` ifMatch, with only PartitionKey and RowKey (V1) |
| `TouchKeyRing` | UpdateMerge `vault` ifMatch, with only PartitionKey and RowKey (V1) |

Chunking (`VaultTransactionPlanner`):

1. If there are no changes, return `Written` without a request.
2. `TouchKeyRing` may appear at most once, and only at index 0. Otherwise throw `ArgumentException`.
3. Changes are packed in order into chunks of at most 100 actions, and at most `VaultStorageLimits.MaxTransactionPayloadBytes` (3 MiB) of estimated payload. The estimate is 2,048 bytes per action plus `ceil(binaryBytes * 4 / 3)`, because binary values travel as base64. A change's actions are never split across chunks.
4. With `TouchKeyRing` at index 0, **every** chunk starts with the key ring touch, using the current key ring ETag (so 99 actions remain for changes). After a chunk commits, the next chunk uses the ETag from that chunk's first response (V2).
5. On `TableTransactionFailedException`, return 412 as `Conflict`, 404 as `NotFound` and 409 as `AlreadyExists`, with `CompletedChanges` set to the number of changes in the committed chunks. Any other exception propagates.
6. `VaultWriteResult.ETags` is aligned with the input changes. It holds the ETag of each change's primary action (the Add or Replace, the Add half of a recycle or restore, or the touch), or null for deletes and uncommitted changes.

Concurrency rules:

- **Secret edits** (update, move, delete, history restore) require the client's `If-Match` ETag. It must match `^W/"datetime'[0-9A-Za-z%.:\-]+'"$` and be at most 100 characters (`ETagRules`). `*` and anything else is rejected with 400 (TH18). A stale ETag returns 409 and changes nothing.
- **Folder structure** (create, move or delete a folder, restore a folder batch) puts `TouchKeyRing(keyRingETag)` first, with the ETag read at the start of the handler. Two concurrent structural changes cannot both commit, so the cycle check and the folder count limit hold (TH16, TH37).
- **Secret into folder** (create in a folder, move into a folder, restore into the original folder) includes `TouchFolder(folderId, folderETag)`. A concurrent delete, move or rename of that folder conflicts, so no secret ever points at a deleted folder (TH20).
- **Folder rename** uses the ETag the handler just read (last writer wins between users' tabs). Folder rows change often because of touches, so a client ETag would cause spurious conflicts (D11).
- Multi-chunk operations (large folder delete, move to parent, restore or purge) are not atomic. Each chunk is atomic, and a failure returns 409 with the earlier chunks applied. Repeating the request finishes the job, because every handler recomputes the current state (D12).

### 4.6 Folders

- `FolderTree` (Application, pure, unit tested) is built from a list of folders:
  - `Contains(id)` and `Count`
  - `ChildrenOf(Guid? parentId)`
  - `DepthOf(id)`: a root-level folder is depth 1. The walk is bounded by `Count`, and a cycle in the stored data throws `InvalidOperationException`.
  - `HeightOf(id)`: a folder with no children has height 1.
  - `IsSameOrDescendant(candidateId, ancestorId)`
  - `SubtreeOf(id)`: the id first, then descendants breadth-first.

  A folder whose parent is not in the list counts as root-level.
- Limits: `MaxFolderDepth = 10`, `MaxFoldersPerVault = 1000` (one storage page lists the whole tree), `MaxItemsPerFolderOperation = 1000` (secrets plus folders touched by one delete, restore or purge).
- **Create**: the parent must be active (or null for the root). The new folder's depth is `DepthOf(parent) + 1` and must be at most 10. The active folder count must stay at or below 1,000.
- **Move**: the target parent must be active or null, and must not be the folder itself or one of its descendants (`IsSameOrDescendant`). The depth `(parent is null ? 0 : DepthOf(parent)) + HeightOf(folder)` must be at most 10.
- **Rename** changes only the name envelope and `ModifiedAt`.
- **Delete `MoveToParent`**: direct secrets of F move to F's parent P (or the root), direct child folders are re-parented to P, and then F goes to the bin. Depth can only shrink.
- **Delete `DeleteContents`**: F, every descendant folder and every secret in any of them go to the bin with one `DeletionBatchId` and one `DeletedAt`. Secrets come first, then folders deepest first, then F.
- `FolderDeleteMode` has no 0 value (`MoveToParent = 1`, `DeleteContents = 2`), and the query parameter is required. A missing or unknown mode is 400, so contents are never deleted by default.
- More than 1,000 affected items returns 400 `FolderOperationTooLarge` and changes nothing (TH35).

### 4.7 Recycle bin and purge

- **Restore a secret**: Delete `bs_` plus Add `s_`. The original `FolderId` is kept when that folder is active (with `TouchFolder`). Otherwise the secret goes to the root.
- **Restore a folder**: load the folder's batch (`DeletionBatchId`) and restore the folder's subtree within the batch, together with the batch secrets whose folder is in that subtree. The subtree root returns to its original parent if that parent is active and the depth fits, otherwise to the root. The active folder count must stay at or below 1,000. A structural `TouchKeyRing` is included.
- **Purge (delete forever)** of a secret: query its `h_` range (at most 21 rows), then one transaction deletes `bs_` and every version row. Of a folder: the folder's subtree within its batch, with each secret purged as above and then the folders in one or more chunks.
- **Retention**: an item is purged when `DeletedAt < now - 30 days` (`VaultLimits.RecycleBinRetention`). DTOs show `PurgeAfter = DeletedAt + 30 days`.
- **Crypto-shredding**: purging a secret deletes the only copy of its wrapped per-secret key, so any leftover version envelope is unreadable.
- **Background purge** (`RecycleBinPurgeService : BackgroundService`, Infrastructure):
  1. It waits `InitialDelay` (1 minute), then runs every `Interval` (6 hours) using a `PeriodicTimer` built with `TimeProvider`. It is disabled when `RecycleBinPurge:Enabled` is false.
  2. Each run calls `IRecycleBinPurger.PurgeExpiredAsync(ct)`.
  3. The purger acquires the `recycle-bin-purge` lease with `IMaintenanceLeaseStore.TryAcquireAsync(name, holderId, LeaseDuration)`. `holderId` is a GUID per process. If the lease is not acquired, it returns `RecycleBinPurgeResult.Skipped`.
  4. If the lease has no `RunId`, it starts a run (`RunId` new, `AfterUserId` null, `RunStartedAt` now). Otherwise it **resumes** after `AfterUserId` (the checkpoint).
  5. It loops over `IUserStore.ListAsync(100, after)`. For each user it calls `IVaultStore.ListRecycledBeforeAsync(owner, cutoff)` and purges each item with `RecycledItemEraser`, catching and logging per-item conflicts. After each user page it renews the lease with the new `AfterUserId`. A lost lease stops the run.
  6. At the end it sets `RunId` and `AfterUserId` to null and `LastCompletedAt` to now.
  7. Every purge writes `SecretPurged` or `FolderPurged` with `ActorId = AuditActors.System` (`Guid.Empty`, V14).
  8. A purge whose ifMatch fails (the item was restored or changed) is skipped. Running twice purges nothing new (idempotent).
- Re-deleting a restored item gives it a new `DeletedAt` and `DeletionBatchId`, and purge always reads the current row, so a restore-then-delete never shortens the 30 days (TH19).

### 4.8 History

- An update with `ReplaceValue = true` on a secret that already has a value runs this transaction:
  - `ReEncrypt` the current envelope from (`secret.value`, secretId) to (`secret.value.history`, newVersionId), under the same per-secret key. No string is created.
  - `AddVersion` with `Revision` = the secret's revision before the change and `ReplacedAt` = now.
  - Encrypt the new value.
  - `ReplaceSecret` with `Revision + 1`.
  - `DeleteVersion` for every version beyond the newest 20.
- **Restore version V**: the current value, if present, becomes a new version as above. V's envelope is re-encrypted from (`secret.value.history`, V) to (`secret.value`, secretId). V itself is kept. Pruning to 20 applies.
- Versions are visible only while the secret is active. A binned secret's history returns 404 (TH8).
- Version ids are version 7 GUIDs. The list is sorted by `ReplacedAt` descending, then by version id descending.

### 4.9 Listing, search and paging

- **List** `GET /api/vault/secrets`:
  - Parameters: `folderId` (guid, optional), `unfiled` (bool, optional; both set is 400), `search` (1 to 100 characters after trimming, optional), `pageSize` (1 to 200, default 50) and `continuationToken` (a canonical `D` GUID: the last secret id of the previous page).
  - Without search: read the key ring and one storage page in parallel (`maxPerPage = pageSize`, filter by `FolderKey` when given), then decrypt the metadata of the page with one session.
  - The store keeps fetching storage pages until it has at least one item or reaches the end, with at most 20 storage requests per call. It returns `NextAfterSecretId` = the last item id when more may follow, otherwise null. An empty page with a token can only happen after 20 empty server pages. The UI stops on an empty page.
- **Search** (D9): names are encrypted, so the server scans. It reads storage pages of 1,000 (`VaultLimits.SearchScanBatchSize`), decrypts name, username, URL and tags, and keeps rows where any of them contains the term (`StringComparison.OrdinalIgnoreCase`). It stops at `pageSize` matches or after 5,000 scanned rows per request (`SearchMaxScannedRows`), and returns the last **examined** row id as the token. One unwrap per request. The search term never reaches storage, which rules out OData injection (TH40).
- **Order** is storage order, which is creation order (D8). The server cannot sort by encrypted name without decrypting the whole vault, so the UI sorts the loaded items by name.
- **Folders**: `GET /api/vault/folders` returns every active folder (at most 1,000, one storage page) sorted by decrypted name (`StringComparer.OrdinalIgnoreCase`), with `parentId`. The UI builds the tree.
- **Recycle bin**: `GET /api/vault/recycle-bin?pageSize=&continuationToken=` reads the range from `b` to `c`, folders (`bf_`) first and then secrets (`bs_`). The token is the RowKey without the leading `b` (`f_{N}` or `s_{N}`) and is validated with `^[fs]_[0-9a-f]{32}$`.
- A damaged row (decryption failure, unknown key version or malformed tags) appears with `isDamaged = true` and null text fields. It never fails the whole page (D19).

### 4.10 API contract

Every route is under `/api/vault`, with `RequireAuthorization(Policies.User)`, `RequireRateLimiting(RateLimitPolicies.Vault)` and tag `Vault`. Reveal routes override the limit with `RateLimitPolicies.Reveal`. Ids in routes use `:guid` constraints. Request bodies are Api records with `[JsonUnmappedMemberHandling(Disallow)]` (V7). Enums travel as strings. `If-Match` is the `ETag` value from a previous response.

| # | Method and route | Body or query | Success |
|---|---|---|---|
| E1 | `GET /secrets` | query `folderId`, `unfiled`, `search`, `pageSize`, `continuationToken` | 200 `SecretPageDto { items: SecretDto[], continuationToken }` |
| E2 | `GET /secrets/{secretId}` | | 200 `SecretDto` |
| E3 | `POST /secrets` | `{ type, name, username?, url?, value?, notes?, tags?, folderId? }` | 201 `SecretDto`, `Location: /api/vault/secrets/{id}` |
| E4 | `PUT /secrets/{secretId}` | `If-Match`; `{ type, name, username?, url?, tags?, replaceValue, value?, replaceNotes, notes? }` | 200 `SecretDto` |
| E5 | `POST /secrets/{secretId}/move` | `If-Match`; `{ folderId? }` | 200 `SecretDto` |
| E6 | `DELETE /secrets/{secretId}` | `If-Match` | 204 |
| E7 | `POST /secrets/{secretId}/reveal` | `{ field: Value or Notes, intent: View or Copy }` | 200 `RevealedValueDto { value }` |
| E8 | `GET /secrets/{secretId}/history` | | 200 `SecretVersionListDto { items: [{ versionId, replacedAt, revision }] }` |
| E9 | `POST /secrets/{secretId}/history/{versionId}/reveal` | `{ intent }` | 200 `RevealedValueDto` |
| E10 | `POST /secrets/{secretId}/history/{versionId}/restore` | `If-Match` (the secret's ETag) | 200 `SecretDto` |
| E11 | `GET /folders` | | 200 `FolderListDto { items: FolderDto[] }` |
| E12 | `POST /folders` | `{ name, parentId? }` | 201 `FolderDto`, `Location: /api/vault/folders/{id}` |
| E13 | `PUT /folders/{folderId}` | `{ name }` | 200 `FolderDto` |
| E14 | `POST /folders/{folderId}/move` | `{ parentId? }` | 200 `FolderDto` |
| E15 | `DELETE /folders/{folderId}` | query `mode` (required) | 204 |
| E16 | `GET /recycle-bin` | query `pageSize`, `continuationToken` | 200 `RecycleBinPageDto { items, continuationToken }` |
| E17 | `POST /recycle-bin/secrets/{secretId}/restore` | | 200 `SecretDto` |
| E18 | `POST /recycle-bin/folders/{folderId}/restore` | | 200 `FolderDto` (the restored subtree root) |
| E19 | `DELETE /recycle-bin/secrets/{secretId}` | | 204 |
| E20 | `DELETE /recycle-bin/folders/{folderId}` | | 204 |

DTOs (Application, records):

- `SecretDto(Guid Id, SecretType Type, string? Name, string? Username, string? Url, IReadOnlyList<string> Tags, Guid? FolderId, bool HasValue, bool HasNotes, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, string ETag, bool IsDamaged)`. It has **no** value or notes members (TH21).
- `FolderDto(Guid Id, Guid? ParentId, string? Name, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, bool IsDamaged)`
- `RecycleBinItemDto(RecycleBinItemKind Kind, Guid Id, string? Name, SecretType? Type, Guid DeletionBatchId, DateTimeOffset DeletedAt, DateTimeOffset PurgeAfter, bool IsDamaged)`
- `RevealedValueDto(string Value)`, which overrides `ToString`.

Errors:

| Case | Status | Body |
|---|---|---|
| FluentValidation failure, missing `If-Match` | 400 | `ValidationProblemDetails`. Messages are constants and never echo the submitted value (TH25) |
| JSON binding failure (unknown member, integer or unknown enum, lone surrogate) | 400 | generic ProblemDetails (V7) |
| `Result.Rejected(message)` (depth, cycle, limits, too large) | 400 | ProblemDetails, `title` "The request could not be completed.", `detail` the constant message |
| `Result.NotFound()` | 404 | ProblemDetails "The item was not found.", identical whether the id does not exist or belongs to someone else (TH27) |
| `Result.Conflict()` | 409 | ProblemDetails "The item was changed by another request. Reload and try again." |
| Body over 1 MiB | 413 | foundation limit |
| Rate limit | 429 | ProblemDetails from status code pages, plus `Retry-After` when known |
| `SecretDecryptionException` (reveal, history reveal or restore of a damaged value) | 500 | ProblemDetails "The item could not be read." with no reason (TH25) |

All API responses already get `Cache-Control: no-store` from the foundation middleware (TH26).

### 4.11 Authorisation and the share hook

- `ISecretAccessPolicy.ResolveAsync(Guid secretId, SecretPermission required, CancellationToken) : ValueTask<SecretAccessGrant?>` returns `SecretAccessGrant(Guid OwnerId, SecretPermission Granted)`.
- `OwnerOnlySecretAccessPolicy` always returns `new SecretAccessGrant(currentUser.UserId, SecretPermission.Owner)` and makes no storage call. `feature/sharing` replaces it: it will look up a share row and return the owner and the permission, or null.
- `SecretPermission`: `None = 0`, `View = 1`, `Edit = 2`, `Manage = 3`, `Owner = 4`. Required permissions:
  - View: E2, E7, E8, E9
  - Edit: E4, E5, E10
  - Owner: E6 (SHR-2 "only the owner can delete")
- Handlers on E2 and E4 to E10 call the policy first. A null grant, or `grant.Granted < required`, returns `Result.NotFound()` (never 403, TH27). `grant.OwnerId` is then the partition **and** the `OwnerId` in every `EncryptionContext`.
- Lists (E1, E11, E16), folder endpoints (E12 to E15) and recycle bin endpoints (E17 to E20) always use `currentUser.UserId`. Sharing will add its own views.

### 4.12 Audit

- `AuditEntry` gets `Guid? OwnerId` (the vault owner of the item) and `Guid? SecretId`. `AuditLogTableEntity` gets `OwnerId` and `SecretId` (string, `D`, null when absent).
- `TableAuditLog.AppendAsync` writes the `AuditLog` row and, when both `OwnerId` and `SecretId` are set, a `SecretAuditLog` row. Both use `AddEntityAsync` only, with no transactions (the existing source scan stays green). If the second insert fails, the exception propagates.
- New `AuditEventType` values (stored as strings): `SecretCreated = 10`, `SecretUpdated = 11`, `SecretMoved = 12`, `SecretDeleted = 13`, `SecretRestored = 14`, `SecretPurged = 15`, `SecretValueRevealed = 16`, `SecretValueCopied = 17`, `SecretNotesRevealed = 18`, `SecretNotesCopied = 19`, `SecretVersionRevealed = 20`, `SecretVersionCopied = 21`, `SecretVersionRestored = 22`, `FolderCreated = 30`, `FolderRenamed = 31`, `FolderMoved = 32`, `FolderDeleted = 33`, `FolderRestored = 34`, `FolderPurged = 35`.
- `VaultAuditWriter` (scoped) builds entries:
  - `Id = Guid.NewGuid()`
  - `ActorId = currentUser.UserId`
  - `OwnerId` = the vault owner
  - `TargetId` = the item id `D`
  - `SecretId` = the secret id for secret events, null for folder events
  - `OccurredAt = timeProvider.GetUtcNow()`
  - `IpAddress = requestMetadata.IpAddress`
  - `Reason = null`, always

  `WriteManyAsync` uses `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = VaultLimits.AuditParallelism` (8).
- **Order**: reveals and copies audit **after** a successful decrypt and **before** the response. An audit exception propagates (500) and the value is never returned (TH22). Mutations audit **after** the storage write succeeds. An audit failure then returns 500, and the change stays without its entry. This is a recorded residual risk (D13). It is logged as Error `Audit write failed for {EventType} on {ItemId}`.

### 4.13 Rate limiting

- `AddRateLimiter` (ASP.NET Core shared framework, no package) with two fixed-window policies, partitioned by the `oid` claim (`EntraClaimTypes.ObjectId`; the constant partition `unknown` if it is missing):
  - `RateLimitPolicies.Vault` = `"vault"`: `VaultRateLimits:RequestsPerMinute`, default 600, on the whole group.
  - `RateLimitPolicies.Reveal` = `"vault-reveal"`: `VaultRateLimits:RevealsPerMinute`, default 60, on E7 and E9. An endpoint policy overrides the group policy.
- Window 1 minute, `QueueLimit = 0`, `RejectionStatusCode = 429`. `OnRejected` sets `Retry-After` (seconds, rounded up) when the lease has `MetadataName.RetryAfter`.
- `app.UseRateLimiter()` goes right after `app.UseAuthorization()`, so anonymous, locked and role-less callers are rejected before they reach a limiter.
- `VaultRateLimitOptionsValidator`: both values between 1 and 100,000, with `ValidateOnStart`.
- The limits are per instance (D14).

### 4.14 Password generator (`SharpVault.PasswordGeneration`)

- A new class library with no package and no project references. It is referenced by `SharpVault.UI.Shared`, `SharpVault.UnitTests` and `SharpVault.Benchmarks`. There is no API endpoint (D15).
- `CharacterSets` (`[Flags]`): `None = 0`, `Lowercase = 1`, `Uppercase = 2`, `Digits = 4`, `Symbols = 8`, `All = 15`.
- `CharacterAlphabets`:
  - `Lowercase = "abcdefghijklmnopqrstuvwxyz"`
  - `Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"`
  - `Digits = "0123456789"`
  - `Symbols = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"` (the 32 printable ASCII punctuation characters)
  - `Ambiguous = "Il1O0|"`
  - `For(CharacterSets single, bool excludeAmbiguous)` returns the alphabet without the ambiguous characters when asked: 25, 24, 8 and 31 characters.
- `PasswordOptions(int Length, CharacterSets Sets, bool ExcludeAmbiguous, bool RequireEverySet)`, `Default = (20, All, false, true)`.
- `PassphraseSeparator`: `Hyphen = 1` (`-`), `Space = 2`, `Period = 3`, `Underscore = 4`; 0 is undefined.
- `PassphraseOptions(int WordCount, PassphraseSeparator Separator, bool Capitalize, bool IncludeNumber)`, `Default = (6, Hyphen, false, false)`.
- `GeneratorLimits`: `MinLength = 8`, `MaxLength = 128`, `MinWords = 3`, `MaxWords = 20`, `MaxRequireEverySetAttempts = 10_000`.
- `PasswordGenerator.Validate(PasswordOptions)` and `Validate(PassphraseOptions)` return `IReadOnlyList<string>` of constant messages from `GeneratorMessages`:
  - length out of range
  - no set selected
  - undefined flags
  - fewer characters than selected sets when `RequireEverySet`
  - words out of range
  - undefined separator
- `GeneratePassword(PasswordOptions)`:
  1. Throw `ArgumentException(first message)` when invalid.
  2. Concatenate the selected alphabets in the fixed order lowercase, uppercase, digits, symbols.
  3. `RandomNumberGenerator.GetString(alphabet, length)`.
  4. When `RequireEverySet`, repeat the whole draw until every set appears (rejection keeps the result uniform over valid passwords), up to the attempt limit, then throw `InvalidOperationException`.
  5. Entropy from `EntropyCalculator.PasswordBits(setSizes, length, requireEverySet)`.
- `GeneratePassphrase(PassphraseOptions)`:
  1. Pick words with `RandomNumberGenerator.GetInt32(EffWordList.Count)`.
  2. With `Capitalize`, upper-case the first letter of each word (ASCII).
  3. With `IncludeNumber`, append `GetInt32(10)` to the word at `GetInt32(wordCount)`.
  4. Join with the separator.
  5. Entropy: `wordCount * log2(7776)`, plus `log2(10 * wordCount)` with a number.
- `EntropyCalculator.PasswordBits`: with `requireEverySet`, it is the exact log2 of the inclusion-exclusion count `sum over subsets S of (-1)^|S| * (N - size(S))^L`, using `BigInteger` and `BigInteger.Log(x, 2)`. Otherwise `L * log2(N)`. Golden values are in V13.
- `PasswordStrength` from bits: `VeryWeak = 0` (under 40), `Weak` (under 60), `Reasonable` (under 80), `Strong` (under 100), `VeryStrong`. Method `PasswordGenerator.StrengthOf(double bits)`.
- `GeneratedSecret(string Value, double EntropyBits)` with `ToString()` returning `nameof(GeneratedSecret)`.
- `EffWordList`:
  - Loads the embedded resource `SharpVault.PasswordGeneration.Wordlists.eff_large_wordlist.txt` once (`Lazy<T>`, thread-safe).
  - Checks that its SHA-256 equals the constant from V12 and throws `InvalidOperationException(GeneratorMessages.WordListCorrupt)` otherwise.
  - Parses `digits\tword` lines and checks 7,776 unique words.
  - Exposes `Count` and `this[int]`.
- No `System.Random` anywhere (a source scan test checks this).
- **Licensing** (D16): the EFF list is CC BY 3.0 US. It is bundled unmodified as a data file with attribution in `THIRD-PARTY-NOTICES.md` at the repository root and in `src/SharpVault.PasswordGeneration/Wordlists/README.md`. `.gitattributes` gets `src/SharpVault.PasswordGeneration/Wordlists/eff_large_wordlist.txt -text`, so the bytes (and the hash) never change.

### 4.15 UI

- **Render mode** (D20):
  - `SharpVault.Web/Components/App.razor` sets `<HeadOutlet @rendermode="PageRenderMode" />` and `<Routes @rendermode="PageRenderMode" />`.
  - `PageRenderMode` is `HttpContext?.AcceptsInteractiveRouting() == true ? new InteractiveServerRenderMode(prerender: false) : null`, with a static readonly field for the mode and `[CascadingParameter] private HttpContext? HttpContext { get; set; }`.
  - `@attribute [ExcludeFromInteractiveRouting]` goes on `Home`, `Account`, `AccessDenied`, `SignedOut`, `SignInError` and `NotFound`, so the account pages stay static exactly as `feature/entra-sign-in` built them.
  - Vault pages are interactive and **not prerendered**, so no secret data is ever in the first HTML response (TH29) and no API call is made twice.
- **Pages and routes** (`VaultRoutes` constants), all `@attribute [Authorize]`:
  - `/vault` (all items), `/vault/unfiled`, `/vault/folders/{FolderId:guid}`, each with an optional query `q` for search (`[SupplyParameterFromQuery]`)
  - `/vault/secrets/new` (optional query `folderId`)
  - `/vault/secrets/{SecretId:guid}`, `/vault/secrets/{SecretId:guid}/edit`, `/vault/secrets/{SecretId:guid}/history`
  - `/vault/recycle-bin`
- **API client contract**: `IVaultApiClient` (UI.Shared) has one method per endpoint E1 to E20, each returning `Task<ApiResponse<T>>` and taking a `CancellationToken` last. `ApiCallStatus` gains `NotFound`, `Conflict`, `Invalid`, `RateLimited`. `ApiResponse<T>` gains `string? Detail` (the ProblemDetails `detail` of a 400, which is always a server constant) and factories `NotFound()`, `Conflict()`, `Invalid(string? detail)`, `RateLimited()`. The UI shows `Detail` only from a fixed map of known constants, falling back to a generic message.
- **Clipboard** (D18):
  - `IClipboard` (UI.Shared): `Task WriteTextAsync(string text, CancellationToken)` and `Task ClearAsync(CancellationToken)`.
  - Web `BrowserClipboard` imports `./js/clipboard.js` once (`Lazy<Task<IJSObjectReference>>`) and calls `writeText` and `clear`.
  - `SecretClipboard` (UI.Shared, scoped, `IAsyncDisposable`) offers `CopyAsync(string text, CancellationToken)`. It writes, cancels any earlier pending clear, and schedules `ClearAsync` after `VaultUiSettings.ClipboardClearDelay` using `Task.Delay(delay, timeProvider, token)`. `DisposeAsync` clears now and swallows `JSDisconnectedException` and `TaskCanceledException`.
  - The JS module, exactly:

    ```js
    export async function writeText(text) {
        await navigator.clipboard.writeText(text);
    }

    export async function clear() {
        await navigator.clipboard.writeText("");
    }
    ```

- **Revealed values**: `MaskedField` shows `••••••••` until the page has a value. A reveal keeps the value in a private field for `VaultUiSettings.RevealAutoHideDelay` (30 s), then sets it to null and re-renders. Values are also cleared on Hide, on navigation away and in `Dispose`. Copy calls E7 or E9 with intent `Copy` and passes the value straight to `SecretClipboard`. It is never rendered.
- **Interactive elements and their ids** (Playwright-ready, and checked by a bUnit test):

| Component | Ids |
|---|---|
| `MainLayout` nav | `nav-vault`, `nav-recycle-bin` |
| `Home` (signed in) | `home-open-vault` |
| `VaultPage` | `vault-heading`, `vault-search-input`, `vault-search-submit`, `vault-search-clear`, `vault-new-secret`, `vault-new-folder`, `vault-load-more`, `vault-empty`, `vault-error`, `vault-status` |
| `SecretList` | `vault-secret-list`, `vault-secret-row-{id}`, `vault-secret-link-{id}`, `vault-secret-copy-{id}`, `vault-secret-damaged-{id}` |
| `FolderTree` | `folder-tree`, `folder-tree-all`, `folder-tree-unfiled`, `folder-node-{id}`, `folder-link-{id}`, `folder-toggle-{id}`, `folder-new-child-{id}`, `folder-rename-{id}`, `folder-move-{id}`, `folder-delete-{id}` |
| `FolderEditorDialog` | `folder-dialog`, `folder-name-input`, `folder-parent-select`, `folder-save`, `folder-cancel`, `folder-dialog-error` |
| `FolderDeleteDialog` | `folder-delete-dialog`, `folder-delete-mode-move`, `folder-delete-mode-delete`, `folder-delete-confirm` (disabled until a mode is chosen), `folder-delete-cancel`, `folder-delete-error` |
| `SecretDetailPage` | `secret-heading`, `secret-type`, `secret-username`, `secret-copy-username`, `secret-url`, `secret-tags`, `secret-folder`, `secret-created`, `secret-modified`, `secret-value`, `secret-reveal-value`, `secret-hide-value`, `secret-copy-value`, `secret-notes`, `secret-reveal-notes`, `secret-hide-notes`, `secret-copy-notes`, `secret-edit`, `secret-move`, `secret-delete`, `secret-history-link`, `secret-status`, `secret-error`, `secret-damaged` |
| `MoveSecretDialog` | `move-dialog`, `move-folder-select`, `move-confirm`, `move-cancel` |
| `SecretEditorPage` | `secret-form`, `secret-name-input`, `secret-type-select`, `secret-username-input`, `secret-change-value`, `secret-value-input` (`type="password"`, `autocomplete="new-password"`), `secret-value-toggle`, `secret-generate`, `secret-url-input`, `secret-change-notes`, `secret-notes-input`, `secret-tags-input` (comma separated), `secret-folder-select`, `secret-save`, `secret-cancel`, `secret-form-error`, `secret-conflict` |
| `PasswordGeneratorDialog` | `generator-dialog`, `generator-mode-password`, `generator-mode-passphrase`, `generator-length`, `generator-lowercase`, `generator-uppercase`, `generator-digits`, `generator-symbols`, `generator-exclude-ambiguous`, `generator-require-every-set`, `generator-word-count`, `generator-separator`, `generator-capitalize`, `generator-include-number`, `generator-output`, `generator-entropy`, `generator-strength`, `generator-regenerate`, `generator-copy`, `generator-use`, `generator-cancel`, `generator-error` |
| `SecretHistoryPage` | `history-heading`, `history-list`, `history-row-{versionId}`, `history-value-{versionId}`, `history-reveal-{versionId}`, `history-hide-{versionId}`, `history-copy-{versionId}`, `history-restore-{versionId}`, `history-empty`, `history-error`, `history-conflict` |
| `RecycleBinPage` | `bin-heading`, `bin-list`, `bin-row-{kind}-{id}`, `bin-expires-{kind}-{id}`, `bin-restore-{kind}-{id}`, `bin-purge-{kind}-{id}`, `bin-load-more`, `bin-empty`, `bin-error` (`{kind}` is `secret` or `folder`) |
| `ConfirmDialog` | `{IdPrefix}-dialog`, `{IdPrefix}-confirm`, `{IdPrefix}-cancel` (parameter `IdPrefix`, for example `secret-delete`, `history-restore`, `bin-purge`) |

- **Rendering rules**:
  - Names and all text go through normal Razor encoding, never `MarkupString` (TH30).
  - `secret-url` is an `<a href>` with `rel="noopener noreferrer"` and `target="_blank"` only when `Uri.TryCreate` gives an absolute `http` or `https` URI. Otherwise it is plain text.
  - No `style=` attributes and no inline scripts (the CSP forbids them, and `WebTestSupport.HasInlineScriptHandlerOrStyle` checks it). Styles go in `.razor.css` files using the existing CSS variables in `app.css`.
- **Status handling**:
  - `SignInRequired`: `NavigateTo(SignIn?returnUrl=current, forceLoad: true)`.
  - `AccessDenied`: `NavigateTo(AccessDenied, forceLoad: true)`.
  - `NotFound`: show "This item no longer exists."
  - `Conflict`: show `secret-conflict` or `history-conflict` with a Reload button.
  - `RateLimited`: "Too many reveals. Try again in a minute."
  - `Invalid`: the mapped detail.
  - `Failed`: a generic error.
- **Folder views** load pages of 200 until the token is null or 1,000 items are shown, then offer `vault-load-more`. Search loads one page of 50 per `vault-load-more`.

### 4.16 Configuration

| Key | Host | Default | Rule |
|---|---|---|---|
| `VaultRateLimits:RequestsPerMinute` | Api | 600 | 1 to 100,000 |
| `VaultRateLimits:RevealsPerMinute` | Api | 60 | 1 to 100,000 |
| `RecycleBinPurge:Enabled` | Api | `true` | |
| `RecycleBinPurge:InitialDelay` | Api | `00:01:00` | 0 to 1 hour |
| `RecycleBinPurge:Interval` | Api | `06:00:00` | 1 minute to 24 hours |
| `RecycleBinPurge:LeaseDuration` | Api | `00:05:00` | 1 minute to 1 hour |

Retention (30 days), the history limit (20), field limits, folder limits and scan limits are constants in `VaultLimits`, because they are product rules (FLD-3) and not tuning values. The UI settings are a `VaultUiSettings` record with defaults (ADM-4 groundwork).

## 5. Tasks

Ownership:

- `implementer`: T1 to T15. After each task: `dotnet build SharpVault.slnx` reports `0 Warning(s)` and `0 Error(s)`, and `dotnet test SharpVault.slnx --filter "Category!=Performance"` stays green, except for the existing tests listed in T19, which T19 updates. The implementer of the task that first breaks one of those (T1, T3, T4, T5) notes it in their hand-off.
- `test-writer`: T16 to T21.
- `docs-writer`: T22.

```
T0 (orchestrator: merge main with entra-sign-in into this branch)
T1 (alone, first)
 ├─ T2 (PasswordGeneration)                               ∥ T3
 ├─ T3 (Domain + Application contracts) ─┬─ T4 (Vault store) ──────┐
 │                                       ├─ T5 (Audit + maintenance)│
 │                                       └─ T6 (Vault common)  ─────┼─ T7 ∥ T8 ∥ T9 ∥ T10 (handlers)
 │                                                                  │        └─ T11 (Api) ── T12 (purge service)
 ├─ T13 (UI.Shared services + models, after T1)  ∥ T3..T12
 │      ├─ T14a (vault list, tree, folder dialogs)  ∥ T14b (detail, editor, generator, history, bin)
 │      └─ T15 (Web: API client, clipboard, render mode; after T13, parallel with T11)
 ├─ T16 test infrastructure (after T11)
 ├─ T17 unit tests (after T2, T6..T10)       T18 integration and security tests (after T12, T16)
 ├─ T19 existing test updates (after T1, T3, T4, T5)   T20 UI tests (after T14a, T14b, T15)
 ├─ T21 performance tests and benchmarks (after T16)
 └─ T22 docs (after all)
```

T4, T5 and T6 can run in parallel. T7, T8, T9 and T10 can run in parallel (separate folders, no shared files). T14a and T14b can run in parallel. T15 runs in parallel with T11 because the HTTP contract is fixed in section 4.10.

---

### T0. Merge prerequisite (orchestrator, not an implementer)

Merge `main` (with `feature/entra-sign-in`) into `feature/vault-and-folders`. Expect conflicts in `src/SharpVault.Infrastructure/DependencyInjection.cs` (keep `builder.AddEncryption()` **and** the user, audit and cache registrations), `tests/SharpVault.IntegrationTests/Factories/ApiTestFactory.cs` (keep `DevelopmentKeyMaterial` **and** the token setup), `Directory.Packages.props`, `docs/PROGRESS.md`, `docs/STORAGE.md`, `docs/TESTING.md` and `tests/SharpVault.UnitTests/Architecture/LayeringTests.cs`.

**Acceptance**: build clean and all non-performance tests pass before T1 starts.

### T1. Projects, packages and licensing (sequential, first)

1. Create `src/SharpVault.PasswordGeneration/SharpVault.PasswordGeneration.csproj`: `Microsoft.NET.Sdk`, no package and no project references, and `<EmbeddedResource Include="Wordlists/eff_large_wordlist.txt" LogicalName="SharpVault.PasswordGeneration.Wordlists.eff_large_wordlist.txt" />`. Add it to `SharpVault.slnx` under `/src/`.
2. Download the EFF large list to `src/SharpVault.PasswordGeneration/Wordlists/eff_large_wordlist.txt`. Use `https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt` if reachable, otherwise `https://raw.githubusercontent.com/ulif/diceware/master/diceware/wordlists/wordlist_en_eff.txt`. Check `sha256sum` equals `addd35536511597a02fa0a9ff1e5284677b8883b83e986e43f15a3db996b903e` and the size is 108,800 bytes. **If either differs, stop and report to the planner.**
3. `src/SharpVault.PasswordGeneration/Wordlists/README.md`: source URL, "EFF Large Wordlist for Passphrases, Electronic Frontier Foundation, licensed under CC BY 3.0 US (https://creativecommons.org/licenses/by/3.0/us/)", "Unmodified", and the SHA-256.
4. `THIRD-PARTY-NOTICES.md` (repository root): one section for the EFF list with the same attribution and the file path.
5. `.gitattributes`: add `src/SharpVault.PasswordGeneration/Wordlists/eff_large_wordlist.txt -text`.
6. `Directory.Packages.props`: add `<PackageVersion Include="bunit" Version="2.13.3" />` in alphabetical order.
7. `src/SharpVault.UI.Shared/SharpVault.UI.Shared.csproj`: add a project reference to `../SharpVault.PasswordGeneration/SharpVault.PasswordGeneration.csproj`.
8. `tests/SharpVault.UnitTests/SharpVault.UnitTests.csproj`: add `<PackageReference Include="bunit" />` and a project reference to PasswordGeneration.
9. `tests/SharpVault.Benchmarks/SharpVault.Benchmarks.csproj`: add a project reference to PasswordGeneration.
10. `.github/workflows/ci.yml`: append `SharpVault.PasswordGeneration=95` to the `CoverageGate.cs` arguments.
11. Add an empty `public static class GeneratorLimits` in namespace `SharpVault.PasswordGeneration` with the five constants from 4.14, so the project builds.

**Acceptance**: the build is clean and `dotnet list SharpVault.slnx package --vulnerable --include-transitive` reports nothing. `LayeringTests.ProjectFiles_ProjectReferences_MatchTheAllowedLayering` for UI.Shared fails until T19. This is expected.

### T2. Password generator (after T1, parallel with T3)

Implement every type in section 4.14 under `src/SharpVault.PasswordGeneration/`. Notes:

- `PasswordGenerator` is a `public static class`. `EffWordList` is `internal static`, with `private static readonly Lazy<string[]> Words`. Add `[assembly: InternalsVisibleTo("SharpVault.UnitTests")]` through a csproj `<InternalsVisibleTo Include="SharpVault.UnitTests" />` item, so tests can read the count.
- The resource stream is read fully into a `byte[]`. The hash uses `SHA256.HashData`, compared with `Convert.ToHexStringLower(hash) == ExpectedSha256` (the list is not secret, so plain comparison is correct).
- Messages in `GeneratorMessages` are constants (for example `LengthOutOfRange = "Length must be between 8 and 128."`).
- XML doc comments on the public types (this is a shared library).

**Acceptance**: build clean; `GeneratePassword(PasswordOptions.Default)` returns 20 characters; `GeneratePassphrase(PassphraseOptions.Default)` returns 6 hyphen-joined words; entropy matches V13 within 1e-3.

### T3. Domain and Application contracts (after T1, parallel with T2)

**Domain** (lean POCOs, no methods):

- `Vaults/SecretType.cs`: `Login = 1, SecureNote = 2, ApiKey = 3, Card = 4, Other = 5` (0 undefined).
- `Vaults/Secret.cs`: `Guid Id`, `Guid OwnerId`, `SecretType Type`, `Guid? FolderId`, `byte[] NameEnvelope = []`, `byte[]? UsernameEnvelope`, `byte[]? UrlEnvelope`, `byte[]? TagsEnvelope`, `byte[]? ValueEnvelope`, `byte[]? NotesEnvelope`, `public required WrappedDataKey DataKey { get; set; }`, `int MetadataKeyVersion`, `long Revision`, `DateTimeOffset CreatedAt`, `DateTimeOffset ModifiedAt`, `DateTimeOffset? DeletedAt`, `Guid? DeletionBatchId`.
- `Vaults/Folder.cs`: `Guid Id`, `Guid OwnerId`, `Guid? ParentId`, `byte[] NameEnvelope = []`, `int MetadataKeyVersion`, `DateTimeOffset CreatedAt`, `DateTimeOffset ModifiedAt`, `DateTimeOffset? DeletedAt`, `Guid? DeletionBatchId`.
- `Vaults/SecretVersion.cs`: `Guid Id`, `Guid SecretId`, `Guid OwnerId`, `byte[] ValueEnvelope = []`, `long Revision`, `DateTimeOffset ReplacedAt`.
- `Vaults/VaultKeyRing.cs`: `Guid OwnerId`, `required WrappedDataKey MetadataKey`, `int MetadataKeyVersion`, `DateTimeOffset CreatedAt`.
- `Maintenance/MaintenanceLease.cs`: `string Name`, `string Holder`, `DateTimeOffset ExpiresAt`, `Guid? RunId`, `Guid? AfterUserId`, `DateTimeOffset? RunStartedAt`, `DateTimeOffset? LastCompletedAt`.
- `Audit/AuditEventType.cs`: append the values in 4.12.
- `Audit/AuditEntry.cs`: add `Guid? OwnerId` and `Guid? SecretId`.
- `Audit/AuditActors.cs`: `public static class AuditActors { public static readonly Guid System = Guid.Empty; }`.

**Application** `Common/Models/`:

- `ResultFailureKind.cs`: `Invalid = 0, NotFound = 1, Conflict = 2, Rejected = 3`.
- `ResultMessages.cs`: `NotFound = "The item was not found."`, `Conflict = "The item was changed by another request. Reload and try again."`.
- `Result.cs`: add `public ResultFailureKind FailureKind { get; }` (Invalid for the existing `Failure` overloads, unchanged behaviour), and factories `NotFound()`, `Conflict()`, `Rejected(string message)`. Add a private constructor taking `(IReadOnlyList<string> errors, ResultFailureKind kind)`.
- `Done.cs`: `public sealed record Done { public static Done Value { get; } = new(); }`.
- `SecretPermission.cs` (4.11) and `SecretAccessGrant.cs`: `public sealed record SecretAccessGrant(Guid OwnerId, SecretPermission Granted)`.
- `SecretFolderScope.cs`: `All = 1, Unfiled = 2, Folder = 3`. `SecretFolderFilter.cs`: `public sealed record SecretFolderFilter(SecretFolderScope Scope, Guid? FolderId)` with static `All`, `Unfiled` and `InFolder(Guid id)`.
- `SecretPage.cs`: `(IReadOnlyList<Versioned<Secret>> Items, Guid? NextAfterSecretId)`.
- `RecycleBinEntry.cs`: `(Versioned<Secret>? Secret, Versioned<Folder>? Folder)`. `RecycleBinPage.cs`: `(IReadOnlyList<RecycleBinEntry> Items, string? NextAfterKey)`. `RecycledBatch.cs`: `(IReadOnlyList<Versioned<Secret>> Secrets, IReadOnlyList<Versioned<Folder>> Folders)`.
- `VaultChangeKind.cs`: the 14 kinds in 4.5, numbered from 1.
- `VaultChange.cs`: a sealed record with a private constructor and `Kind`, `Secret?`, `Folder?`, `Version?`, `Guid ItemId`, `string? ETag`, plus one static factory per kind:
  - `AddSecret(Secret)`, `ReplaceSecret(Secret, string etag)`, `RecycleSecret(Secret, string activeETag)`, `RestoreSecret(Secret, string recycledETag)`, `PurgeSecret(Secret, string recycledETag)`
  - `AddVersion(SecretVersion)`, `DeleteVersion(SecretVersion, string etag)`
  - `AddFolder(Folder)`, `ReplaceFolder(Folder, string etag)`, `RecycleFolder(Folder, string activeETag)`, `RestoreFolder(Folder, string recycledETag)`, `PurgeFolder(Folder, string recycledETag)`
  - `TouchFolder(Guid folderId, string etag)`, `TouchKeyRing(string etag)`

  Factories throw `ArgumentNullException` and `ArgumentException.ThrowIfNullOrWhiteSpace` for missing data.
- `VaultWriteResult.cs`: `(StoreWriteResult Result, int CompletedChanges, IReadOnlyList<string?> ETags)`.

**Application** `Common/Interfaces/`:

- `IVaultStore.cs`, exactly:

```csharp
public interface IVaultStore
{
    Task<Versioned<VaultKeyRing>?> GetKeyRingAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Versioned<VaultKeyRing>?> AddKeyRingAsync(VaultKeyRing keyRing, CancellationToken cancellationToken);
    Task<Versioned<Secret>?> GetSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken);
    Task<Versioned<Secret>?> GetRecycledSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken);
    Task<SecretPage> ListSecretsAsync(Guid ownerId, SecretFolderFilter filter, int pageSize, Guid? afterSecretId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Versioned<Secret>>> ListSecretsInFolderAsync(Guid ownerId, Guid? folderId, int limit, CancellationToken cancellationToken);
    Task<Versioned<Folder>?> GetFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);
    Task<Versioned<Folder>?> GetRecycledFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Versioned<Folder>>> ListFoldersAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Versioned<SecretVersion>?> GetVersionAsync(Guid ownerId, Guid secretId, Guid versionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Versioned<SecretVersion>>> ListVersionsAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken);
    Task<RecycleBinPage> ListRecycleBinAsync(Guid ownerId, int pageSize, string? afterKey, CancellationToken cancellationToken);
    Task<RecycledBatch> ListRecycledBatchAsync(Guid ownerId, Guid deletionBatchId, CancellationToken cancellationToken);
    Task<RecycledBatch> ListRecycledBeforeAsync(Guid ownerId, DateTimeOffset deletedBefore, CancellationToken cancellationToken);
    Task<VaultWriteResult> SubmitAsync(Guid ownerId, IReadOnlyList<VaultChange> changes, CancellationToken cancellationToken);
}
```

  `AddKeyRingAsync` returns null when a key ring already exists. `ListSecretsInFolderAsync` with a null `folderId` lists the root, and returns at most `limit` items.
- `IMaintenanceLeaseStore.cs`: `TryAcquireAsync(string name, string holder, TimeSpan duration, CancellationToken)` returning `Task<Versioned<MaintenanceLease>?>`; `RenewAsync(MaintenanceLease lease, string etag, TimeSpan duration, CancellationToken)` returning `Task<Versioned<MaintenanceLease>?>` (null when lost); `ReleaseAsync(MaintenanceLease lease, string etag, CancellationToken)`, which writes the lease with `ExpiresAt = now` so another instance can take it.
- `IRequestMetadata.cs`: `string? IpAddress { get; }`.
- `ISecretAccessPolicy.cs`: as in 4.11.

**Acceptance**: build clean. `AuditStorageMappingTests.AuditEntry_Properties_HaveNoNameEmailOrSecretFields` fails until T19. This is expected.

### T4. Vault table store (after T3, parallel with T5 and T6)

1. `Storage/TableNames.cs`: add `Vaults`, `SecretAuditLog` and `Maintenance` constants, and set `All` to `[Users, AuditLog, Vaults, SecretAuditLog, Maintenance]`.
2. `Vaults/VaultKeys.cs` (public static), with:
   - `PartitionKey(Guid ownerId) => ownerId.ToString("D")`
   - `KeyRingRowKey = "vault"`
   - `SecretPrefix = "s_"`, `RecycledSecretPrefix = "bs_"`, `FolderPrefix = "f_"`, `RecycledFolderPrefix = "bf_"`, `VersionPrefix = "h_"`
   - `RecycleBinLowerBound = "b"`, `RecycleBinUpperBound = "c"`, `RootFolderKey = "root"`
   - `SecretRowKey(Guid id, bool recycled)`, `FolderRowKey(Guid id, bool recycled)`, `VersionRowKey(Guid secretId, Guid versionId) => "h_" + secretId.ToString("N") + "_" + versionId.ToString("N")`, `VersionRangeStart(Guid secretId) => "h_" + N + "_"`
   - `UpperBound(string prefix)`: the prefix with its last character incremented by one
   - `FolderKey(Guid? folderId) => folderId?.ToString("N") ?? RootFolderKey`, and `ParseFolderKey(string key)` returning `Guid?`, which returns null for `root` and for anything that is not a 32-character `N` GUID.
3. `Vaults/VaultStorageLimits.cs`: `MaxActionsPerTransaction = 100`, `MaxTransactionPayloadBytes = 3 * 1024 * 1024`, `ActionOverheadBytes = 2048`, `MaxStorageRequestsPerListCall = 20`, `FolderPageSize = 1000`.
4. Table entities (internal sealed, `ITableEntity`), with columns exactly as in 4.4:
   - `SecretTableEntity`: `Type` and `FolderKey` strings; `DeletionBatchId` a string `D` or null.
   - `FolderTableEntity`: `ParentKey` string.
   - `SecretVersionTableEntity`
   - `VaultKeyRingTableEntity`

   Binary columns are `byte[]?`.
5. `Vaults/VaultStorageMappingRegister.cs` (`IRegister`), domain to entity and back:
   - The PartitionKey comes from `OwnerId`. The RowKey comes from the id and whether `DeletedAt` is set (`bs_`, `bf_`).
   - `DataKey` maps to and from `DataKeyId`, `DataKeyAlgorithm` and `WrappedDataKey`.
   - Enums map to and from strings. An unknown type string maps to `SecretType.Other`.
   - `OwnerId` is parsed from the PartitionKey and `Id` from the RowKey suffix.
   - ETag and Timestamp are ignored.
6. `Vaults/VaultTransactionPlanner.cs` (internal static) and `Vaults/PlannedTransaction.cs` (internal sealed record holding `IReadOnlyList<TableTransactionAction> Actions`, `IReadOnlyList<int> ChangeIndexes` and `IReadOnlyList<int?> PrimaryActionIndexes`). `Plan(Guid ownerId, IReadOnlyList<VaultChange> changes)` implements the rules in 4.5, steps 2 to 4 (the key ring ETag of later chunks is set by the store).
7. `Vaults/TableVaultStore.cs` (`public sealed partial class TableVaultStore(TableServiceClient tableServiceClient, ILogger<TableVaultStore> logger) : IVaultStore`):
   - Point reads use `GetEntityIfExistsAsync`.
   - Range queries use `QueryAsync<T>(filter, maxPerPage, cancellationToken: ct).AsPages()` and pass `cancellationToken`.
   - `ListSecretsAsync` follows 4.9: it loops storage pages until it has at least one item, reaches the end or hits `MaxStorageRequestsPerListCall`. `NextAfterSecretId` is the last item's id when the last page had a continuation token, otherwise null. With an `afterSecretId`, the lower bound is `RowKey gt 's_{N}'`.
   - `ListFoldersAsync` and `ListVersionsAsync` read every page.
   - `ListRecycleBinAsync` uses `RowKey gt 'b{afterKey}'` or `ge 'b'`, and `lt 'c'`, as `TableEntity`, and maps by prefix.
   - `ListRecycledBatchAsync` adds `and DeletionBatchId eq {id:D}`. `ListRecycledBeforeAsync` adds `and DeletedAt lt {deletedBefore}` (a `DateTimeOffset` in `CreateQueryFilter`).
   - `SubmitAsync` runs the planner, then submits the chunks sequentially (chaining the key ring ETag, V2) and maps failures (4.5, step 5). It logs Warning `Vault write for owner {OwnerId} stopped after {CompletedChanges} of {TotalChanges} changes with {Result}` when a later chunk fails.
   - `AddKeyRingAsync` catches 409 and returns null. On success it returns `Versioned` with `response.Headers.ETag`.
8. `DependencyInjection.cs`: `builder.Services.AddSingleton<IVaultStore, TableVaultStore>();`.

**Acceptance**: build clean. No code in `Vaults/` contains `ETag.All`, `UpsertEntity` or a filter built by string concatenation (T18 checks this with a source scan). `TableStorageStartupTests` and `HealthPerformanceTests` table-list assertions fail until T19. This is expected.

### T5. Audit index and maintenance lease (after T3, parallel with T4 and T6)

1. `Audit/AuditLogTableEntity.cs`: add `string? OwnerId` and `string? SecretId`. `AuditStorageMappingRegister`: map them as `?.ToString("D")`, and add `AuditEntry` to `SecretAuditLogTableEntity` with PartitionKey `SecretAuditKeys.PartitionKey(OwnerId!.Value)` and RowKey `SecretAuditKeys.RowKey(SecretId!.Value, OccurredAt, Id)`.
2. `Audit/SecretAuditKeys.cs`: `PartitionKey(Guid ownerId) => ownerId.ToString("D")` and `RowKey(Guid secretId, DateTimeOffset occurredAt, Guid id) => secretId.ToString("N") + "_" + inverted ticks D19 + "_" + id.ToString("N")`, using the same inverted ticks as `AuditKeys`.
3. `Audit/SecretAuditLogTableEntity.cs`: the same columns as `AuditLogTableEntity`.
4. `Audit/TableAuditLog.cs`: get a second `TableClient` for `TableNames.SecretAuditLog`. In `AppendAsync`, after the existing `AddEntityAsync`, when `entry.OwnerId is { } owner && entry.SecretId is { } secret`, call `AddEntityAsync` on the index table. Use no other storage method in the folder.
5. `Maintenance/MaintenanceKeys.cs`: `PartitionKey = "lease"` and `RecycleBinPurgeLease = "recycle-bin-purge"`. `Maintenance/MaintenanceLeaseTableEntity.cs`.
6. `Maintenance/TableMaintenanceLeaseStore.cs` (`IMaintenanceLeaseStore`, with `TimeProvider`):
   - `TryAcquireAsync` reads the lease row.
     - If it is missing, `AddEntityAsync` with `Holder` and `ExpiresAt = now + duration`. A 409 returns null.
     - If it is held by another holder and not expired, return null.
     - Otherwise `UpdateEntityAsync(Replace, ifMatch: etag)` with the new `Holder` and `ExpiresAt`, keeping `RunId`, `AfterUserId` and `RunStartedAt`. A 412 returns null.
   - `RenewAsync` and `ReleaseAsync` do a Replace with ifMatch. A 412 or 404 returns null.
7. `Maintenance/RecycleBinPurgeOptions.cs` (`SectionName = "RecycleBinPurge"`, defaults in 4.16) and `RecycleBinPurgeOptionsValidator.cs` (ranges in 4.16, constant messages).
8. `DependencyInjection.cs`: register `IMaintenanceLeaseStore` (singleton), the options with `Bind(...).ValidateOnStart()` and the validator.

**Acceptance**: build clean; `AuditLogContractTests` stay green. `AuditStorageMappingTests.AuditLogTableEntity_Properties_HaveNoNameEmailOrSecretFields` fails until T19. This is expected.

### T6. Vault common services (after T3, parallel with T4 and T5)

All in `Application/Features/Vault/Common/` unless noted.

1. `VaultPurposes.cs`: the 8 purpose constants from 4.3.
2. `VaultLimits.cs`:
   - `NameMaxLength = 200`, `UsernameMaxLength = 256`, `UrlMaxLength = 2048`, `ValueMaxLength = 8192`, `NotesMaxLength = 8192`
   - `TagMaxLength = 50`, `MaxTags = 20`, `FolderNameMaxLength = 100`
   - `MaxFolderDepth = 10`, `MaxFoldersPerVault = 1000`, `MaxItemsPerFolderOperation = 1000`, `HistoryVersionLimit = 20`
   - `RecycleBinRetention = TimeSpan.FromDays(30)`
   - `DefaultPageSize = 50`, `MaxPageSize = 200`, `SearchMaxLength = 100`, `SearchScanBatchSize = 1000`, `SearchMaxScannedRows = 5000`
   - `AuditParallelism = 8`, `FolderQueryParallelism = 4`, `ETagMaxLength = 100`, `CurrentMetadataKeyVersion = 1`
3. `VaultMessages.cs`: constant messages, among them `FolderDepthExceeded`, `FolderCycle`, `FolderLimitReached`, `FolderOperationTooLarge`, `FolderTreeInvalid`, `FolderAndUnfiledConflict`, `InvalidContinuationToken`, `InvalidETag`, `FieldTooLong`, `FieldRequired`, `InvalidCharacters`, `InvalidUrl`, `TooManyTags`, `DuplicateTag` and `NotWellFormedText`. None of them contains a placeholder.
4. `VaultTextRules.cs`: FluentValidation extension methods.
   - `SingleLineText(maxLength)`: not whitespace only; length at most max after trim; well-formed UTF-16 (no lone surrogates: loop with `char.IsSurrogatePair`); no `UnicodeCategory.Control`; none of U+202A to U+202E or U+2066 to U+2069.
   - `MultiLineSecretText(maxLength)`: well-formed UTF-16 and length 1 to max. Any other character is allowed.
   - `HttpUrl()`: no whitespace or control characters; `Uri.TryCreate(value, UriKind.Absolute, out var uri)` with scheme `http` or `https`.
   - `TagList()`: at most 20 tags; each `SingleLineText(50)`; distinct after trim, case-insensitive ordinal.

   Each rule calls `.WithMessage(constant)`.
5. `ETagRules.cs`: `ValidIfMatch()` with the regex and length from 4.5 (`[GeneratedRegex]`).
6. `VaultIds.cs`: `NewId(TimeProvider time) => Guid.CreateVersion7(time.GetUtcNow())`.
7. `OpenedVault.cs`: `public sealed class OpenedVault(IDataKeySession session, int metadataKeyVersion, string keyRingETag) : IDisposable`, exposing `Session`, `MetadataKeyVersion` and `KeyRingETag`. Dispose disposes the session.
8. `VaultKeyRingService.cs` (sealed, singleton; `IVaultStore`, `IEnvelopeEncryptor`, `TimeProvider`):
   - `Task<OpenedVault?> OpenAsync(Guid ownerId, CancellationToken)`
   - `Task<OpenedVault> OpenOrCreateAsync(Guid ownerId, CancellationToken)`, following the algorithm in section 9 D1: get, else create a data key and add the ring, and on `null` from the add, dispose and re-read.
   - `Task<string?> GetKeyRingETagAsync(Guid ownerId, CancellationToken)`, which reads without unwrapping.
9. `SecretMetadata.cs`: `public sealed record SecretMetadata(string Name, string? Username, string? Url, IReadOnlyList<string> Tags)` with `ToString() => nameof(SecretMetadata)`.
10. `SecretMetadataCipher.cs` (static):
    - `Encrypt(IDataKeySession metadataSession, Secret secret, SecretMetadata metadata)` sets the four envelopes, with null for an absent username, URL or empty tags. Tags are JSON.
    - `TryDecrypt(IDataKeySession, Secret, int expectedVersion, out SecretMetadata? metadata)` returns false on `SecretDecryptionException`, `JsonException` or a version mismatch.
    - `EncryptFolderName` and `TryDecryptFolderName`.
11. `SecretDtoFactory.cs` (static): `Create(Versioned<Secret> secret, OpenedVault? vault)`. It decrypts when `vault` is not null, otherwise sets `IsDamaged = true`. Tags are `[]` when damaged. `FolderDtoFactory.cs` works the same way.
12. `VaultAuditItem.cs`: `public sealed record VaultAuditItem(AuditEventType EventType, Guid OwnerId, Guid TargetId, Guid? SecretId)`. `VaultAuditWriter.cs` (sealed, scoped) is described in 4.12.
13. `OwnerOnlySecretAccessPolicy.cs` (sealed, scoped) is described in 4.11.
14. `FolderTree.cs` is described in 4.6.
15. `Api` is out of scope for this task. `Application/DependencyInjection.cs` registers `VaultKeyRingService` (singleton), `VaultAuditWriter` (scoped) and `ISecretAccessPolicy` to `OwnerOnlySecretAccessPolicy` (scoped).
16. Dtos: `Features/Vault/Secrets/Dtos/SecretDto.cs`, `SecretPageDto.cs` and `RevealedValueDto.cs`; `Folders/Dtos/FolderDto.cs` and `FolderListDto.cs`; `History/Dtos/SecretVersionDto.cs` and `SecretVersionListDto.cs`; `RecycleBin/Dtos/RecycleBinItemDto.cs`, `RecycleBinPageDto.cs` and `RecycleBinItemKind.cs` (`Secret = 1, Folder = 2`). Enums: `Secrets/RevealField.cs` (`Value = 1, Notes = 2`), `Secrets/RevealIntent.cs` (`View = 1, Copy = 2`), `Folders/FolderDeleteMode.cs` (`MoveToParent = 1, DeleteContents = 2`).

**Acceptance**: build clean; no file under `Features/Vault` contains `IsAdmin`.

### T7. Secret commands and queries (after T4 and T6, parallel with T8, T9 and T10)

One file each in `Features/Vault/Secrets/Commands` and `Queries`. Each holds the request record (with `ToString` overridden when it carries plaintext), the validator and the handler (`sealed partial` with `[LoggerMessage]` where it logs). Handlers depend on `IVaultStore`, `VaultKeyRingService`, `IEnvelopeEncryptor`, `ISecretAccessPolicy`, `VaultAuditWriter`, `ICurrentUser` and `TimeProvider` as needed.

1. **`CreateSecret.cs`**: `CreateSecretCommand(SecretType Type, string Name, string? Username, string? Url, string? Value, string? Notes, IReadOnlyList<string>? Tags, Guid? FolderId) : IRequest<Result<SecretDto>>`.
   - Validator: `IsInEnum` on type (which rejects 0); `SingleLineText(200)` on Name (required); `SingleLineText(256)` on Username when not null; `HttpUrl` and length 2,048 on Url when not null; `MultiLineSecretText(8192)` on Value and Notes when not null; `TagList` when not null; `NotEqual(Guid.Empty)` on FolderId when not null.
   - Handler:
     1. owner = currentUser.UserId.
     2. If there is a FolderId, `GetFolderAsync`; missing returns NotFound.
     3. `using var vault = await keyRings.OpenOrCreateAsync(owner)`.
     4. `using var secretKey = await encryptor.CreateDataKeyAsync()`.
     5. Build `Secret` (id `VaultIds.NewId`, Revision 1, `MetadataKeyVersion = vault.MetadataKeyVersion`, dates now) and encrypt the metadata, value and notes.
     6. Changes: `[TouchFolder(folder) if any, AddSecret]`, then `SubmitAsync`. Not Written returns Conflict (an AlreadyExists here means a racing folder change).
     7. Audit `SecretCreated`.
     8. Return `SecretDtoFactory.Create` with the new ETag.
2. **`GetSecret.cs`**: `GetSecretQuery(Guid SecretId)`. Validator: not empty. Handler: grant (View); in parallel `GetSecretAsync(grant.OwnerId)` and `keyRings.OpenAsync(grant.OwnerId)`; return the DTO. Dispose the vault in `finally`, even when the secret is missing.
3. **`ListSecrets.cs`**: `ListSecretsQuery(Guid? FolderId, bool Unfiled, string? Search, int PageSize, string? ContinuationToken)` with `DefaultPageSize` and `MaxPageSize` constants.
   - Validator: page size 1 to 200; not both FolderId and Unfiled; Search trimmed 1 to 100 when not null; continuation token is a canonical `D` GUID (same as `ListUsersQueryValidator`).
   - Handler: open the vault (null returns an empty page without a secret query). Without Search, one `ListSecretsAsync`. With Search, the loop in 4.9. Map with `SecretDtoFactory`.
4. **`UpdateSecret.cs`**: `UpdateSecretCommand(Guid SecretId, string ETag, SecretType Type, string Name, string? Username, string? Url, IReadOnlyList<string>? Tags, bool ReplaceValue, string? Value, bool ReplaceNotes, string? Notes)`.
   - Validator: as create, plus `ValidIfMatch` on the ETag. Value must be null when `ReplaceValue` is false, and Notes null when `ReplaceNotes` is false.
   - Handler:
     1. grant (Edit).
     2. Read the secret and open the vault in parallel. A missing secret returns NotFound. `secret.ETag != command.ETag` returns Conflict, without a write.
     3. Re-encrypt the metadata.
     4. If `ReplaceValue` or `ReplaceNotes`: `using var key = await encryptor.OpenDataKeyAsync(secret.DataKey)`. On `ReplaceValue`, when the old value exists, `ListVersionsAsync`, add a version and prune (4.8). Set or clear the value. On `ReplaceNotes`, set or clear the notes.
     5. Revision + 1, ModifiedAt now.
     6. `SubmitAsync([ReplaceSecret(etag), AddVersion?, DeleteVersion...])`.
     7. Audit `SecretUpdated`.
5. **`MoveSecret.cs`**: `MoveSecretCommand(Guid SecretId, string ETag, Guid? FolderId)`.
   - Handler: grant (Edit); read the secret, and the target folder if non-null, in parallel; a missing one returns NotFound; when the target equals the current folder, return the DTO without a write. Otherwise `[TouchFolder if non-null, ReplaceSecret]`, then audit `SecretMoved`. The returned DTO needs metadata, so open the vault (1 unwrap).

     Budget note: the move response returns `SecretDto` with metadata, so the move costs 1 unwrap. The budget table in section 8 lists 1 for E5.
6. **`DeleteSecret.cs`**: `DeleteSecretCommand(Guid SecretId, string ETag) : IRequest<Result<Done>>`. Handler: grant (Owner); read; check the ETag; set `DeletedAt = now` and `DeletionBatchId = new Guid`; `[RecycleSecret]`; audit `SecretDeleted`.
7. **`RevealSecretField.cs`**: `RevealSecretFieldCommand(Guid SecretId, RevealField Field, RevealIntent Intent) : IRequest<Result<RevealedValueDto>>`. Validator: enums defined. Handler: the steps in section 9 D7. Event: `(Value, View)` is SecretValueRevealed, `(Value, Copy)` SecretValueCopied, `(Notes, View)` SecretNotesRevealed, `(Notes, Copy)` SecretNotesCopied.

Shared rule for every handler: `StoreWriteResult.Conflict` and `AlreadyExists` return `Result.Conflict()`, `NotFound` returns `Result.NotFound()`. Logs carry ids only: `LogSecretWriteConflict(SecretId)` at Debug and `LogAuditFailed(EventType, ItemId, Exception)` at Error before rethrowing.

**Acceptance**: build clean. Every handler compiles against the interfaces only (no Infrastructure using).

### T8. History (after T4 and T6, parallel with T7, T9 and T10)

`Features/Vault/History/`:

1. **`Queries/ListSecretVersions.cs`**: `ListSecretVersionsQuery(Guid SecretId)`. Handler: grant (View); `GetSecretAsync` (missing, including binned, returns NotFound); `ListVersionsAsync`; sort as in 4.8; map to `SecretVersionDto(Guid VersionId, DateTimeOffset ReplacedAt, long Revision)`.
2. **`Commands/RevealSecretVersion.cs`**: `RevealSecretVersionCommand(Guid SecretId, Guid VersionId, RevealIntent Intent)`. Handler: grant (View); read the secret and the version in parallel (either missing returns NotFound); open the per-secret key; decrypt with (`secret.value.history`, VersionId, grant.OwnerId); audit `SecretVersionRevealed` or `SecretVersionCopied`, with `SecretId` set; return.
3. **`Commands/RestoreSecretVersion.cs`**: `RestoreSecretVersionCommand(Guid SecretId, Guid VersionId, string ETag)`. Handler: grant (Edit); read the secret, the version and the vault in parallel; check the ETag; open the per-secret key; follow 4.8 "Restore version V"; submit; audit `SecretVersionRestored`; return `SecretDto`.

**Acceptance**: build clean.

### T9. Folders (after T4 and T6, parallel with T7, T8 and T10)

`Features/Vault/Folders/`:

1. **`Queries/ListFolders.cs`**: `ListFoldersQuery()`. Handler: open the vault (null returns an empty list without a folder query); `ListFoldersAsync`; decrypt the names; sort; return.
2. **`Commands/CreateFolder.cs`**: `CreateFolderCommand(string Name, Guid? ParentId)`. Validator: `SingleLineText(100)`; ParentId not empty when set. Handler: `OpenOrCreateAsync` and `ListFoldersAsync` in parallel; `FolderTree`; parent check, depth and count checks (Rejected with the constant message); `[TouchKeyRing(vault.KeyRingETag), AddFolder]`; audit `FolderCreated` (SecretId null).
3. **`Commands/RenameFolder.cs`**: `RenameFolderCommand(Guid FolderId, string Name)`. Handler: `OpenAsync` and `GetFolderAsync` in parallel (missing returns NotFound); `[ReplaceFolder(folder, folderETag)]`; audit `FolderRenamed`.
4. **`Commands/MoveFolder.cs`**: `MoveFolderCommand(Guid FolderId, Guid? ParentId)`. Handler: `GetKeyRingETagAsync` and `ListFoldersAsync` in parallel; the folder must be in the tree (else NotFound); the target parent, cycle and depth checks from 4.6; a parent equal to the current one returns the DTO without a write; `[TouchKeyRing, ReplaceFolder]`; audit `FolderMoved`. The DTO needs the name, so open the vault once at the end.

   Budget note: the move response returns `FolderDto` with the name, so E14 costs 1 unwrap (section 8).
5. **`Commands/DeleteFolder.cs`**: `DeleteFolderCommand(Guid FolderId, FolderDeleteMode Mode) : IRequest<Result<Done>>`. Validator: `IsInEnum` (0 rejected). Handler: `GetKeyRingETagAsync` and `ListFoldersAsync`; the folder must be in the tree (else NotFound); then the steps for the mode in 4.6.
   - Secret listing uses `ListSecretsInFolderAsync(owner, id, VaultLimits.MaxItemsPerFolderOperation + 1)`, with `Parallel.ForEachAsync` at `FolderQueryParallelism` for subtrees. Count folders plus secrets; over 1,000 returns Rejected `FolderOperationTooLarge` before any write.
   - One `batchId` and one `deletedAt`. Submit.
   - `WriteManyAsync` audits: `SecretDeleted` or `SecretMoved` per secret, `FolderMoved` per re-parented child, and `FolderDeleted` per binned folder.

**Acceptance**: build clean.

### T10. Recycle bin (after T4 and T6, parallel with T7, T8 and T9)

`Features/Vault/RecycleBin/`:

1. **`Queries/ListRecycleBin.cs`**: `ListRecycleBinQuery(int PageSize, string? ContinuationToken)`. Validator: page size 1 to 200; token matches `^[fs]_[0-9a-f]{32}$`. Handler: `OpenAsync` (null returns empty) and `ListRecycleBinAsync`; map to `RecycleBinItemDto` with `PurgeAfter = DeletedAt + RecycleBinRetention`.
2. **`Commands/RestoreRecycledSecret.cs`**:
   1. Read the bin row (else NotFound).
   2. If `FolderId` is set, `GetFolderAsync`: an active folder adds `TouchFolder` and keeps the folder, otherwise the secret goes to the root.
   3. Clear the deletion fields and set ModifiedAt now.
   4. `[TouchFolder?, RestoreSecret(binETag)]`.
   5. Audit `SecretRestored`.
   6. Return `SecretDto` (open the vault: 1 unwrap).
3. **`Commands/RestoreRecycledFolder.cs`**: implements 4.7 "Restore a folder". Restoring subfolder S of a batch restores S's subtree only. Audit `FolderRestored` and `SecretRestored` per item. Return the root `FolderDto` (1 unwrap).
4. **`RecycledItemEraser.cs`** (sealed, singleton, `IVaultStore`), used by the purge commands and the purger:
   - `Task<StoreWriteResult> EraseSecretAsync(Guid ownerId, Versioned<Secret> recycled, CancellationToken)`: `ListVersionsAsync`, then `SubmitAsync([PurgeSecret, DeleteVersion...])`.
   - `Task<StoreWriteResult> EraseFoldersAsync(Guid ownerId, IReadOnlyList<Versioned<Folder>> folders, CancellationToken)`: `[PurgeFolder...]`.
5. **`Commands/PurgeRecycledSecret.cs`**: read the bin row (else NotFound); erase; audit `SecretPurged`.
6. **`Commands/PurgeRecycledFolder.cs`**: read the bin row; `ListRecycledBatchAsync`; take the subtree within the batch and its secrets (over 1,000 returns Rejected); erase the secrets (parallel 4), then the folders; audit `SecretPurged` and `FolderPurged`.
7. **`IRecycleBinPurger.cs`** (`Task<RecycleBinPurgeResult> PurgeExpiredAsync(CancellationToken)`), **`RecycleBinPurgeResult.cs`** (record: `bool Skipped`, `int UsersScanned`, `int SecretsPurged`, `int FoldersPurged`, `int Conflicts`), and **`RecycleBinPurger.cs`** (sealed partial, singleton; `IUserStore`, `IVaultStore`, `IMaintenanceLeaseStore`, `RecycledItemEraser`, `IAuditLog`, `TimeProvider`, `ILogger`) implementing 4.7 steps 3 to 8.
   - Audit entries are built directly (`ActorId = AuditActors.System`, no IP) and appended one at a time.
   - Logs: Information `Recycle bin purge finished: {UsersScanned} users, {SecretsPurged} secrets, {FoldersPurged} folders, {Conflicts} conflicts`; Information `Recycle bin purge skipped because another instance holds the lease`.
8. Register `RecycledItemEraser` and `IRecycleBinPurger` as singletons in `Application/DependencyInjection.cs`.

**Acceptance**: build clean.

### T11. API (after T7 to T10, parallel with T15)

1. **`Errors/ResultHttpExtensions.cs`**:
   - `ToHttpResult<T>` maps `FailureKind`: Invalid to 400 (unchanged, title only); Rejected to 400 with `detail` = `result.Error`; NotFound to 404 with title `ResultMessages.NotFound`; Conflict to 409 with title `ResultMessages.Conflict`.
   - Add `ToCreatedHttpResult<T>(this Result<T> result, Func<T, string> location)` (201 `TypedResults.Created(location(value), value)`, failures as above) and `ToNoContentHttpResult(this Result<Done> result)` (204).
2. **`Errors/SecretDecryptionExceptionHandler.cs`** (`IExceptionHandler`): `SecretDecryptionException` becomes 500 ProblemDetails with title "The item could not be read." Register it after `ValidationExceptionHandler`.
3. **`Json/ApiJsonExtensions.cs`**: `AddApiJson(this IServiceCollection)` calls `ConfigureHttpJsonOptions` with `JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)` (V7).
4. **`Security/HttpContextRequestMetadata.cs`** (`IRequestMetadata`, scoped): `UserProfileText.NormalizeIpAddress(accessor.HttpContext?.Connection.RemoteIpAddress?.ToString())`. Register it in `AddApiAuthentication` next to `ICurrentUser`.
5. **`RateLimiting/`**: `RateLimitPolicies` (constants), `VaultRateLimitOptions` (`SectionName = "VaultRateLimits"`), the validator, and `VaultRateLimitingExtensions.AddVaultRateLimiting(this IHostApplicationBuilder)` implementing 4.13.
6. **`Endpoints/Vault/VaultHeaders.cs`**: `IfMatch = "If-Match"`.
7. **Request records** (`Endpoints/Vault/Requests/`), each `public sealed record` with `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` and `ToString` overridden when it has text:
   - `CreateSecretRequest(SecretType Type, string Name, string? Username, string? Url, string? Value, string? Notes, IReadOnlyList<string>? Tags, Guid? FolderId)`
   - `UpdateSecretRequest(SecretType Type, string Name, string? Username, string? Url, IReadOnlyList<string>? Tags, bool ReplaceValue, string? Value, bool ReplaceNotes, string? Notes)`
   - `MoveSecretRequest(Guid? FolderId)`
   - `RevealSecretRequest(RevealField Field, RevealIntent Intent)`
   - `RevealVersionRequest(RevealIntent Intent)`
   - `CreateFolderRequest(string Name, Guid? ParentId)`
   - `RenameFolderRequest(string Name)`
   - `MoveFolderRequest(Guid? ParentId)`
8. **Endpoint classes** (static, `Map...Endpoints(this IEndpointRouteBuilder)`):
   - `VaultSecretEndpoints` (E1 to E7), `VaultHistoryEndpoints` (E8 to E10), `VaultFolderEndpoints` (E11 to E15), `RecycleBinEndpoints` (E16 to E20).
   - Each class calls `app.MapGroup("/api/vault").RequireAuthorization(Policies.User).RequireRateLimiting(RateLimitPolicies.Vault).WithTags("Vault")` and maps its routes on it. E7 and E9 add `.RequireRateLimiting(RateLimitPolicies.Reveal)`.
   - Every route has `WithName("Vault_{Operation}")`, `WithSummary`, `WithDescription` and `Produces...` for the statuses in 4.10.
   - `If-Match` binds with `[FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch` and is passed as `ifMatch ?? string.Empty`, so the validator returns 400.
   - Handler delegates are private static methods that build the command and call `mediator.Send`.
9. **`Program.cs`**: add `builder.Services.AddApiJson();`, `builder.Services.AddExceptionHandler<SecretDecryptionExceptionHandler>();` (after the validation handler), `builder.AddVaultRateLimiting();`, `app.UseRateLimiter();` right after `app.UseAuthorization();`, and the four `Map...Endpoints()` calls.

**Acceptance**: build clean. `/openapi/v1.json` in Development lists the 20 operations. Locked and anonymous callers never reach a rate limiter (the middleware order is as specified).

### T12. Recycle bin purge service (after T10 and T11)

1. `Infrastructure/Maintenance/RecycleBinPurgeService.cs`: `public sealed partial class RecycleBinPurgeService(IRecycleBinPurger purger, IOptions<RecycleBinPurgeOptions> options, TimeProvider timeProvider, ILogger<RecycleBinPurgeService> logger) : BackgroundService`.
   - `ExecuteAsync`: return when disabled. Otherwise `await Task.Delay(InitialDelay, timeProvider, ct)`, run once, then use `new PeriodicTimer(Interval, timeProvider)` and run on every tick.
   - Each run is wrapped in `try`/`catch (Exception ex) when (ex is not OperationCanceledException)`, which logs Error `Recycle bin purge failed` with the exception type only, and continues.
2. Register it with `builder.Services.AddHostedService<RecycleBinPurgeService>();` in `AddInfrastructure`.

**Acceptance**: build clean. With `RecycleBinPurge:Enabled=false` the API starts and makes no `Maintenance` table call.

### T13. UI.Shared services and models (after T1, parallel with T3 to T12)

1. `Models/Vault/`: the UI records and enums in 4.1, mirroring the DTOs in 4.10 (`SecretKind` has the same names as `SecretType`). `SecretDraft` (create) and `SecretChanges` (update) carry the text fields and override `ToString`. `RevealedValue(string Value)` overrides `ToString`.
2. `Services/ApiCallStatus.cs`: append `NotFound`, `Conflict`, `Invalid`, `RateLimited`. `Services/ApiResponse.cs`: add `string? Detail = null` as the last positional parameter and the new factories. Existing factories are unchanged.
3. `Services/IVaultApiClient.cs`: 20 methods, for example `Task<ApiResponse<VaultSecretPage>> ListSecretsAsync(Guid? folderId, bool unfiled, string? search, int pageSize, string? continuationToken, CancellationToken cancellationToken)`, `Task<ApiResponse<RevealedValue>> RevealSecretAsync(Guid secretId, SecretFieldName field, RevealPurpose purpose, CancellationToken cancellationToken)` and `Task<ApiResponse<bool>> DeleteSecretAsync(Guid secretId, string eTag, CancellationToken cancellationToken)`. Name the rest after E1 to E20.
4. `Services/IClipboard.cs`, `Services/SecretClipboard.cs` (4.15), `Services/VaultUiSettings.cs` (`public sealed record VaultUiSettings(TimeSpan ClipboardClearDelay, TimeSpan RevealAutoHideDelay) { public static VaultUiSettings Default { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)); }`).
5. `Services/SharpVaultUiServiceCollectionExtensions.cs`: `AddSharpVaultUi(this IServiceCollection services)` calls `TryAddSingleton(TimeProvider.System)`, `TryAddSingleton(VaultUiSettings.Default)` and `AddScoped<SecretClipboard>()`.
6. `Navigation/VaultRoutes.cs`: route constants and helpers (`Folder(Guid)`, `Secret(Guid)`, `EditSecret(Guid)`, `History(Guid)`).
7. `_Imports.razor`: add `@using SharpVault.UI.Shared.Models.Vault`, `@using SharpVault.UI.Shared.Components.Vault`, `@using SharpVault.UI.Shared.Components.Common` and `@using SharpVault.PasswordGeneration`.
8. Add `@attribute [ExcludeFromInteractiveRouting]` to `Home`, `Account`, `AccessDenied`, `SignedOut`, `SignInError` and `NotFound` (4.15).

**Acceptance**: build clean; the existing UI and Web tests pass.

### T14a. Vault list, folder tree and folder dialogs (after T13, parallel with T14b)

`Pages/Vault/VaultPage.razor`, `Components/Vault/FolderTree.razor`, `SecretList.razor`, `FolderEditorDialog.razor`, `FolderDeleteDialog.razor`, `Components/Common/ConfirmDialog.razor`, each with a `.razor.css`. `MainLayout` nav links go inside `AuthorizeView`, and `Home` gets `home-open-vault`.

- Behaviour and ids follow 4.15.
- The folder tree is built from `ListFoldersAsync` on load, with child folders collapsed by default.
- `FolderDeleteDialog` has two radio inputs and no initial selection, and confirm stays disabled until one is chosen.
- Copy from the list calls `RevealSecretAsync(id, Value, Copy)` and `SecretClipboard.CopyAsync`, then shows "Copied. The clipboard clears in 30 seconds." in `vault-status`.

**Acceptance**: build clean; no `style=` or inline handler attributes in the markup; every `button`, `a`, `input`, `select` and `textarea` has an `id`.

### T14b. Secret detail, editor, generator, history and recycle bin (after T13, parallel with T14a)

`Pages/Vault/SecretDetailPage.razor`, `SecretEditorPage.razor`, `SecretHistoryPage.razor`, `RecycleBinPage.razor`, `Components/Vault/MaskedField.razor`, `MoveSecretDialog.razor`, `PasswordGeneratorDialog.razor`, with `.razor.css`. Behaviour and ids follow 4.15. The rules:

- The detail page never calls a reveal on load. Reveal and copy are separate calls with the right intent.
- The editor for an existing secret starts with `secret-change-value` and `secret-change-notes` unchecked and the value and notes inputs disabled and empty. Checking `secret-change-notes` first calls `RevealSecretAsync(Notes, View)` to fill the field (this is audited).
- Save sends `UpdateSecretAsync` with `ReplaceValue` and `ReplaceNotes` from the checkboxes and the ETag from the loaded secret. 409 shows `secret-conflict`.
- The generator dialog regenerates on every option change. It shows the entropy rounded to one decimal place and the strength from `PasswordGenerator.StrengthOf`. "Use" passes the value back to the editor through an `EventCallback<string>`. "Copy" uses `SecretClipboard` (no audit: generated values are not stored secrets). Invalid options show the first `Validate` message in `generator-error`.
- History restore uses `ConfirmDialog` (`IdPrefix="history-restore"`) and sends the secret's current ETag.
- The recycle bin shows `Expires {PurgeAfter:d}`. Purge uses `ConfirmDialog` (`IdPrefix="bin-purge"`).

**Acceptance**: build clean; same markup rules as T14a.

### T15. Web host (after T13, parallel with T11)

1. `Api/ApiJson.cs`: `internal static class ApiJson { public static JsonSerializerOptions Options { get; } }`, with Web defaults and `JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)`.
2. `Api/SharpVaultApiTransport.cs` (sealed): moves the token acquisition, the bearer header and the status mapping out of `SharpVaultApiClient`.
   - `Task<ApiResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body, string? ifMatch, CancellationToken)`. 204 maps to `Success(default)` for `bool` (true).
   - Mapping: 400 is `Invalid(detail)` (the ProblemDetails `detail` read with `ReadFromJsonAsync<ProblemDetailsBody>`, where `ProblemDetailsBody` is an internal record with `string? Detail`), 401 SignInRequired, 403 AccessDenied, 404 NotFound, 409 Conflict, 429 RateLimited, and anything else Failed.
   - The existing log messages stay as they are. No log ever includes a body.
   - `SharpVaultApiClient` keeps its exact public behaviour by delegating to the transport, so its tests stay green.
3. `Api/VaultApiClient.cs` (`IVaultApiClient`): one method per endpoint. Paths are built with `Uri.EscapeDataString` for query values. GUIDs are formatted `D`.
4. `Api/SharpVaultApiRegistration.cs`: register the transport as transient, and `AddHttpClient<IVaultApiClient, VaultApiClient>(...)` with the same base address, followed by `.RemoveAllResilienceHandlers().AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods())` (V9, D22). Also add `builder.Services.AddSharpVaultUi()` and `builder.Services.AddScoped<IClipboard, BrowserClipboard>()`.
5. `Clipboard/BrowserClipboard.cs` (sealed, `IClipboard`, `IAsyncDisposable`), using module path `./js/clipboard.js`, and `wwwroot/js/clipboard.js` exactly as in 4.15.
6. `Components/App.razor`: the render mode in 4.15.

**Acceptance**: build clean. The existing Web tests (`AccountPageTests`, `WebAuthenticationTests`, `AccountPageApiTests`) still pass, because the account pages stay static.

### T16. Test infrastructure (test-writer, after T11)

1. `Authentication/CallerKind.cs`: append `OtherUser`. `CallerClients.UserFor`: `CallerKind.OtherUser => TestUsers.OtherUser`.
2. `Encryption/CountingKeyWrapProvider.cs`: wraps an `IKeyWrapProvider`, counts `Wraps` and `Unwraps` with `Interlocked`, and has `Clear()`.
3. `Factories/ApiTestFactory.cs`:
   - Settings: `RecycleBinPurge:Enabled=false`, `VaultRateLimits:RequestsPerMinute=100000`, `VaultRateLimits:RevealsPerMinute=100000`.
   - In `ConfigureTestServices`, replace `IKeyWrapProvider` with `CountingKeyWrapProvider` over a `DevelopmentKeyWrapProvider` from DI (register `DevelopmentKeyWrapProvider` as a singleton too).
   - Expose `public CountingKeyWrapProvider KeyWraps`.
4. `Vault/VaultTestApi.cs`: typed helpers over `HttpClient` for E1 to E20, returning `(HttpResponseMessage Response, T? Body)`. They use the same JSON options as the API (string enums).
5. `Vault/VaultRows.cs`: raw `TableClient` helpers on `TableNames.Vaults`:
   - `GetAsync(owner, rowKey)` and `ListPartitionAsync(owner)`
   - `CopyPropertyAsync(owner, fromRowKey, toRowKey, property)` and `SwapPropertiesAsync(owner, rowKey, a, b)`
   - `CopyRowToPartitionAsync(fromOwner, rowKey, toOwner)`
   - `SetDeletedAtAsync(owner, rowKey, DateTimeOffset)`
6. `Vault/VaultSeeder.cs`: seeds N secrets and M folders for an owner through `factory.Services` (`VaultKeyRingService`, `IEnvelopeEncryptor`, `IVaultStore.SubmitAsync` in chunks of 50). It returns the ids and plaintexts.
7. `Vault/PlaintextMarkers.cs`: per-test unique markers `SV-NAME-{guid}`, `SV-USER-{guid}`, `SV-URL` (as `https://sv-url-{guid}.test/`), `SV-TAG-{guid}`, `SV-VALUE-{guid}`, `SV-NOTES-{guid}` and `SV-FOLDER-{guid}`, plus `All` and `AssertAbsentFrom(string text)`.
8. `Vault/VaultStorageRequests.cs`: filters for the recorder.
   - `VaultRequests()` matches `^/[^/]+/(Vaults|SecretAuditLog)(\(|$)` or `^/[^/]+/\$batch$` (transactions go to `$batch`).
   - `AuditWrites()` matches POSTs to `AuditLog` or `SecretAuditLog`.
   - `VaultReads()` and `VaultWrites()`.
9. `ApiTests/Vault/VaultTestBase.cs` (`: ApiSecurityTestBase`):
   - `ClientFor(TestUser)`.
   - `WarmUpAsync(TestUser)`: calls `/api/me`, so the access gate's provisioning writes happen before measurement. With CacheTtl 0, every later request still makes one `Users` GET, which vault counts exclude.
   - `VaultAuditRowsAsync(params AuditEventType[])` (AuditLog rows filtered by `EventType`, ignoring `SignIn`).
   - `SecretAuditRowsAsync(owner, secretId)`.
   - `ResetCounters()`: clears the storage recorder and `KeyWraps`.
10. Unit test support: `Support/Vault/StubCurrentUser.cs`, `StubRequestMetadata.cs`, `RecordingAuditLog.cs` (an in-memory `IAuditLog` that can be told to throw), `VaultFixtures.cs` (AutoFixture customisations for `Secret`, `Folder` and `SecretVersion` with valid envelopes from the existing `EncryptionHarness`), and `FakeClipboard.cs`.

**Acceptance**: `TestInfrastructureTests` gets `CountingKeyWrapProvider_CountsUnwrapsThroughTheApi` and `VaultStorageRequests_BatchPath_IsCountedAsVaultWrite`, and both pass.

### T17. Unit tests (test-writer, after T2 and T6 to T10)

Every test listed in sections 7.2 to 7.5.

### T18. API integration and security tests (test-writer, after T12 and T16)

Every test listed in sections 7.1 and 7.6 to 7.9.

### T19. Updates to existing tests (test-writer, after T1, T3, T4 and T5)

| File | Change |
|---|---|
| `UnitTests/Architecture/LayeringTests.cs` | Add `PasswordGeneration` to `ProductionAssemblyNames`. `ProjectFiles_ProjectReferences_MatchTheAllowedLayering` rows: UiShared `[PasswordGeneration]`, PasswordGeneration `[]`. Add `PasswordGeneration_ReferencedAssemblies_ContainNoSharpVaultOrFrameworkProject` (no `SharpVault.*`, `Azure.*` or `Microsoft.AspNetCore.*`) and `PasswordGenerationProjectFile_PackageReferences_AreEmpty`. |
| `UnitTests/Infrastructure/Audit/AuditStorageMappingTests.cs` | Add `OwnerId` and `SecretId` to both property lists (still no name, email or secret fields). Add `Adapt_VaultAuditEntry_MapsOwnerAndSecretIds` and `Adapt_SecretAuditEntry_UsesOwnerPartitionAndSecretPrefixedRowKey`. |
| `IntegrationTests/ApiTests/TableStorageStartupTests.cs` | Expect the five tables in `TableNames.All` and their creation. |
| `IntegrationTests/ApiTests/HealthPerformanceTests.cs` | Precondition list becomes the five tables. Storage calls equal `TableNames.All.Count`. |
| `IntegrationTests/ApiTests/UserProvisioningTests.cs` | Add `OwnerId` and `SecretId` to `AllowedAuditProperties` only if the typed entity writes null columns (check first. Azure.Data.Tables omits null properties, so normally no change). |
| `UnitTests/Api/Errors/ResultHttpExtensionsTests.cs` | Keep the existing tests. Add the cases in 7.5. |

### T20. UI and Web tests (test-writer, after T14a, T14b and T15)

Every test in sections 7.10 and 7.11.

### T21. Performance tests and benchmarks (test-writer, after T16)

Section 7.12 and section 8.

### T22. Documentation (docs-writer, after all)

- `docs/STORAGE.md`: the `Vaults` (every RowKey prefix and column, nothing in clear except ids, type, folder key, dates, revision and key metadata), `SecretAuditLog` and `Maintenance` tables; the transaction and touch rules (4.5); the partition-key rule.
- `docs/ENCRYPTION.md`: replace "Open decision: data key per field" with the D1 and D2 decision, the field table in 4.3 (purposes are persisted format), unwrap counts, history item ids (ENC-D24 done), crypto-shredding, and the `ReEncryptTo` contract for transfers.
- New `docs/VAULT.md`: user-facing behaviour (fields, limits, folders and depth, delete modes, recycle bin and purge, history, search and order, reveal, copy and rate limits, clipboard clearing, the share hook), plus the endpoint list from 4.10.
- `docs/TESTING.md`: bUnit, `CountingKeyWrapProvider`, `VaultStorageRequests` (`$batch`), `PlaintextMarkers`, purge tests with `FakeTimeProvider`, and the new filters.
- `docs/PROGRESS.md`: move vault-and-folders to Done with requirement ids. Add these follow-ups: Playwright E2E with the system Chrome channel (D21); idle auto-lock and admin-configurable clipboard timeout (ADM-4); AUD-5 query endpoint and UI; `ReEncryptTo` in `feature/offboarding-transfers` (D2); VIS-3 counts; MAUI `IClipboard`; distributed rate limiting (D14); structured card fields; Safari clipboard behaviour (D18); and audit-after-write residual risk review (D13).
- `README.md`: a feature list entry for the vault, the folders, the generator and the recycle bin.

## 6. Threats

Every threat needs a passing test before the work is done. Tests are `[Trait("Category", "Security")]` unless they are marked Performance.

### 6.1 Access (VIS-1, VIS-2, LCK-1)

| Id | Threat | Mitigation | Tests |
|---|---|---|---|
| TH1 | IDOR: another user's secret id on E2 and E4 to E10 | Owner partition from the grant; missing returns 404 | `SecretEndpoints_OtherUsersSecretId_Return404AndChangeNothing` (theory over E2, E4, E5, E6, E7, E8, E9, E10), `SecretEndpoints_OtherUsersSecretId_WriteNoAuditEntry` |
| TH2 | IDOR: another user's folder id in a route (E13, E14, E15) | Folder lookups use the caller's partition | `FolderEndpoints_OtherUsersFolderId_Return404AndChangeNothing` (theory over E13, E14, E15 with both modes) |
| TH3 | Moving or creating into another user's folder (body or query ids) | The same | `CreateSecret_OtherUsersFolderId_Returns404AndCreatesNothing`, `MoveSecret_IntoOtherUsersFolder_Returns404AndSecretUnchanged`, `CreateFolder_UnderOtherUsersFolder_Returns404`, `MoveFolder_UnderOtherUsersFolder_Returns404AndTreeUnchanged`, `ListSecrets_OtherUsersFolderId_ReturnsEmptyPage`, `RestoreSecret_OriginalFolderNowOtherUsersId_RestoresToRoot` (row tampered) |
| TH4 | History restore or reveal of another user's version, or a version of another of the caller's own secrets | Version RowKey includes the secret id and lives in the owner's partition | `HistoryReveal_OtherUsersVersionId_Returns404`, `HistoryRestore_OtherUsersVersionId_Returns404AndChangesNothing`, `HistoryRestore_VersionOfAnotherOwnSecret_Returns404AndChangesNothing`, `HistoryReveal_VersionOfAnotherOwnSecret_Returns404` |
| TH5 | Recycle bin restore or purge across owners | Bin lookups use the caller's partition | `RecycleBinRestoreSecret_OtherUsersItem_Returns404AndItemStaysInTheirBin`, `RecycleBinRestoreFolder_OtherUsersItem_Returns404`, `RecycleBinPurgeSecret_OtherUsersItem_Returns404AndItemKept`, `RecycleBinPurgeFolder_OtherUsersItem_Returns404AndItemKept`, `RecycleBinList_ReturnsOnlyCallersItems` |
| TH6 | Admin reads users' secrets (VIS-2) | No `IsAdmin` in vault code; owner partition | `AdminNonOwner_EveryItemEndpoint_Returns404` (theory E2, E4 to E10, E13 to E15, E17 to E20), `AdminLists_ReturnOnlyAdminsOwnVault`, `VaultSources_DoNotReferenceIsAdmin` (unit source scan of `Features/Vault`), `AdminOwnVault_FullLifecycle_WorksLikeUser` |
| TH7 | A locked user, or a locked owner with a valid token | Existing gate on every route | Authorisation matrix rows, `LockedOwner_ValidToken_Returns403OnEveryVaultEndpoint`, `LockedOwner_AfterUnlock_SeesVaultAgain` |
| TH8 | Binned items reachable through active endpoints | Different RowKey prefix | `GetSecret_InRecycleBin_Returns404`, `RevealSecret_InRecycleBin_Returns404AndWritesNoAudit`, `History_SecretInRecycleBin_Returns404`, `UpdateSecret_InRecycleBin_Returns404`, `ListSecrets_ExcludesRecycledSecrets`, `ListFolders_ExcludesRecycledFolders` |
| TH9 | A continuation token that points into another partition, or a malformed one | Tokens are GUIDs or validated keys, used only as RowKey bounds inside the caller's partition | `ListSecrets_ContinuationTokenFromOtherUsersVault_ReturnsOnlyCallersSecrets`, `ListSecrets_MalformedContinuationToken_Returns400`, `ListRecycleBin_MalformedContinuationToken_Returns400` |

### 6.2 Escalation and mass assignment

| Id | Threat | Mitigation | Tests |
|---|---|---|---|
| TH10 | Owner swap or mass assignment (`ownerId`, `id`, `createdAt`, `revision`, `dataKeyId`, `deletedAt` in a body) | Unmapped members rejected; ids come from the route or the server | `CreateSecret_BodyWithOwnerId_Returns400AndCreatesNothing`, `CreateSecret_BodyWithId_Returns400`, `UpdateSecret_BodyWithUnknownMembers_Returns400AndChangesNothing` (theory `ownerId`, `id`, `revision`, `createdAt`, `dataKey`, `deletedAt`), `CreateFolder_BodyWithOwnerId_Returns400`, `RevealSecret_BodyWithOwnerId_Returns400` |
| TH11 | A future share with less than Owner deletes (SHR-2) | Delete requires `SecretPermission.Owner` | Unit: `DeleteSecret_GrantIsEdit_ReturnsNotFoundAndWritesNothing`, `UpdateSecret_GrantIsView_ReturnsNotFound`, `RevealSecretField_NullGrant_ReturnsNotFound` |

### 6.3 Tampering

| Id | Threat | Mitigation | Tests |
|---|---|---|---|
| TH12 | Ciphertext swapped between fields of one secret | A purpose per field in the associated data | `GetSecret_NameAndUsernameEnvelopesSwapped_ReturnsDamagedWithoutPlaintext`, `RevealValue_NotesEnvelopeCopiedIntoValue_Returns500GenericBody`, `RevealNotes_ValueEnvelopeCopiedIntoNotes_Returns500` |
| TH13 | Ciphertext swapped between secrets of one owner | The item id in the associated data, plus per-secret keys | `GetSecret_NameEnvelopeFromAnotherSecret_ReturnsDamaged`, `RevealValue_EnvelopeAndKeyFromAnotherSecret_Returns500`, `ListFolders_NameEnvelopeFromAnotherFolder_ReturnsDamaged` |
| TH14 | A row copied to another owner's partition | The owner id in the associated data, and a different metadata key | `RowCopiedToOtherOwnersPartition_ListShowsDamagedAndRevealReturns500`, `FolderRowCopiedToOtherOwnersPartition_ReadsAsDamaged` |
| TH15 | History envelope swapped into the current value, or a version row copied between secrets (ENC-D24) | Version id as item id; secret key per secret | `RevealValue_HistoryEnvelopeCopiedIntoValue_Returns500`, `HistoryReveal_RowCopiedFromAnotherSecret_Returns500` |
| TH16 | Folder cycles, including concurrent opposite moves | Tree check plus the key ring touch | `MoveFolder_IntoItself_Returns400`, `MoveFolder_IntoOwnDescendant_Returns400AndTreeUnchanged`, `ParallelOppositeFolderMoves_NeverCreateACycle` (20 iterations: move A under B and B under A in parallel; after each, `FolderTree` over the stored rows throws no cycle and at most one move committed) |
| TH17 | Depth beyond the limit | `DepthOf` and `HeightOf` checks | `CreateFolder_AtDepthTen_Returns400`, `CreateFolder_AtDepthNine_Succeeds`, `MoveFolder_SubtreeWouldExceedMaxDepth_Returns400`, `RestoreFolder_OriginalParentNowTooDeep_RestoresToRoot` |
| TH18 | ETag tampering (`*`, garbage, missing) to bypass concurrency | Strict `If-Match` regex; the stored ETag is compared before the write | `UpdateSecret_IfMatchWildcard_Returns400AndChangesNothing`, `UpdateSecret_IfMatchGarbage_Returns400`, `UpdateSecret_MissingIfMatch_Returns400`, `DeleteSecret_IfMatchWildcard_Returns400AndSecretStays`, `UpdateSecret_StaleETag_Returns409AndChangesNothing`, `MoveSecret_StaleETag_Returns409`, `HistoryRestore_StaleETag_Returns409AndHistoryUnchanged` |
| TH19 | Purge shortens retention through restore and re-delete, or races a restore | Purge reads the current row with ifMatch; re-delete sets a new `DeletedAt` | `Purge_RestoredThenDeletedAgain_UsesNewDeletionTime`, `RestoreAndPurgeRace_ExactlyOneWinsAndNothingIsHalfApplied`, `Purge_ItemRestoredDuringRun_IsKept` |
| TH20 | An orphaned secret in a deleted folder through a race | `TouchFolder` on secret-into-folder; ifMatch on folder delete | `ParallelSecretMoveAndFolderDelete_LeaveNoActiveSecretInADeletedFolder` (20 iterations), `ParallelCreateInFolderAndFolderDelete_NoOrphan` |

### 6.4 Leakage (SEC-3, AUD-3)

| Id | Threat | Mitigation | Tests |
|---|---|---|---|
| TH21 | Values or notes in list or detail responses | DTO has no such members | `SecretDto_Properties_HaveNoValueOrNotes` (unit reflection), `ListAndGetAndMutations_Responses_NeverContainValueOrNotesMarkers` (create, list, get, update, move, restore and history list; the body never contains the value or notes markers) |
| TH22 | Reveal or copy without audit, or audit after the value is gone | Audit after decrypt, before the response; failure is 500 without the value | `RevealValue_Success_WritesOneAuditEntryAndOneSecretAuditRow`, `RevealValue_AuditWriteFails_Returns500WithoutValue` (`ThrowingAuditLog`), `RevealNotes_CopyIntent_WritesSecretNotesCopied`, `TwentyParallelReveals_WriteTwentyAuditEntries`, `HistoryReveal_Success_WritesSecretVersionRevealed`, `HistoryReveal_AuditWriteFails_Returns500WithoutValue`, unit `RevealSecretField_AuditThrows_PropagatesAndReturnsNoValue` |
| TH23 | Audit entries containing values or names | Entries built only from ids | `VaultAuditEntries_AfterFullLifecycle_ContainNoPlaintextMarkers` (create, update with value, reveal, copy, history reveal and restore, move, folder create, rename and delete, restore, purge; scan `AuditLog` and `SecretAuditLog` rows), `VaultAuditEntries_HaveOnlyAllowedColumns`, `VaultAuditEntries_ReasonIsAlwaysNull` |
| TH24 | Log leaks (values, names, keys, envelopes) | Ids-only `[LoggerMessage]`; `ToString` redaction | `VaultLifecycle_CapturedLogsAtDebug_ContainNoPlaintextMarkersOrKeyMaterial` (includes `factory.DevelopmentKeyMaterial` and base64 of the wrapped keys), `DecryptionFailure_Log_HasItemIdPurposeAndReasonOnly`, `ValidationFailure_Logs_DoNotContainSubmittedValue` |
| TH25 | Errors echo inputs or reveal decryption reasons | Constant messages; generic 500 | `ValidationErrors_OversizedValue_DoNotEchoSubmittedValue`, `ValidationErrors_InvalidUrl_DoNotEchoUrl`, `DecryptionFailure_Response_IsGenericProblemWithoutReason`, `RejectedFolderMove_Response_HasConstantDetailOnly` |
| TH26 | Browser or proxy caching of revealed values | `no-store` from the middleware; POST for reveal | `RevealValue_Response_HasNoStoreCacheControl`, `HistoryReveal_Response_HasNoStoreCacheControl`, `RevealEndpoint_Get_Returns405` |
| TH27 | Enumeration through 404 versus 403, or through timing | One NotFound path; identical storage pattern | `GetSecret_OtherUsersIdAndRandomId_ReturnIdenticalStatusBodyAndStorageCallPattern`, the same for `RevealSecret_...`, `HistoryReveal_...` and `RecycleBinRestore_...` (bodies equal after removing `traceId`; same list of method and table pairs from the recorder) |
| TH28 | Plaintext stored in Table Storage | Only envelopes are stored | `VaultRows_AfterLifecycle_ContainNoPlaintextMarkers` (every property of every `Vaults` row, as UTF-8 bytes and as text) |
| TH29 | Secret data in prerendered HTML | Vault pages are interactive without prerender | Web: `VaultPages_InitialHtml_ContainNoSecretDataAndNoApiCall` |
| TH30 | XSS through names, tags or URLs | Razor encoding; http and https URLs only (server and UI) | `CreateSecret_JavascriptUrl_Returns400`, `CreateSecret_DataUrl_Returns400`, bUnit `SecretDetail_NameWithMarkup_RendersEncoded`, `SecretDetail_NonHttpUrlFromApi_RendersAsTextNotLink`, `SecretList_TagWithMarkup_RendersEncoded` |
| TH31 | Clipboard and memory residue in the UI | Auto-clear and auto-hide timers; cleared on dispose | bUnit/unit `SecretClipboard_AfterDelay_ClearsClipboard`, `SecretClipboard_SecondCopy_RestartsTimer`, `SecretClipboard_Dispose_ClearsImmediately`, `SecretDetail_AfterAutoHideDelay_HidesValue`, `SecretDetail_Dispose_DropsRevealedValue`, `ClipboardModule_Source_UsesOnlyWriteText` (repository scan: no `readText`, `eval`, `innerHTML`) |
| TH32 | `ToString()` of records leaking plaintext | Overrides | `SecretBearingRecords_ToString_ReturnTypeNameOnly` (reflection over `CreateSecretCommand`, `UpdateSecretCommand`, `RevealedValueDto`, `SecretMetadata`, `CreateSecretRequest`, `UpdateSecretRequest`, `CreateFolderCommand`, `RenameFolderCommand`, `CreateFolderRequest`, `RenameFolderRequest`, `GeneratedSecret`, `SecretDraft`, `SecretChanges`, `RevealedValue`) |

### 6.5 Denial of service and input

| Id | Threat | Mitigation | Tests |
|---|---|---|---|
| TH33 | Oversized fields or bodies | Validators; 1 MiB body limit | `CreateSecret_FieldOverLimit_Returns400` (theory: name 201, username 257, URL 2,049, value 8,193, notes 8,193, tag 51, 21 tags, folder name 101), `CreateSecret_FieldsAtLimit_Succeed`, `CreateSecret_BodyOver1MiB_Returns413` |
| TH34 | Unbounded lists or scans | Page size cap; search scan cap | `ListSecrets_PageSizeZeroOrOverMax_Returns400`, `Search_VaultOf6000_ScansAtMost5000RowsPerRequestAndReturnsToken` (counts entities through the recorder: at most 5 query pages) |
| TH35 | Huge folder operations | 1,000-item cap before any write | `DeleteFolder_MoreThan1000Items_Returns400AndWritesNothing`, `PurgeFolder_MoreThan1000Items_Returns400` |
| TH36 | Reveal flooding | Per-user reveal limit; group limit | `RevealValue_OverRateLimit_Returns429AndWritesNoAuditEntry` (factory with `RevealsPerMinute=3`), `RevealRateLimit_IsPerUser_OtherUserUnaffected`, `VaultRequests_OverGroupLimit_Return429` (`RequestsPerMinute=5`), `LockedUser_NeverConsumesRateLimit` |
| TH37 | Folder or tag explosion | Caps of 1,000 folders and 20 tags, serialised | `CreateFolder_AtMaxFolders_Returns400`, `TwentyParallelFolderCreatesAt995_NeverExceed1000` |
| TH38 | History growth | Pruned to 20 in the same transaction | `UpdateValue_TwentyFiveTimes_KeepsTwentyNewestVersions` |
| TH39 | Table scans | Every query has the partition key | `EveryVaultEndpoint_Queries_AllHavePartitionKey` (theory over E1 to E20, `QueriesWithoutPartitionKey()` empty), `Purge_Run_QueriesAllHavePartitionKey`, `VaultStoreSources_UseCreateQueryFilterOnly` (unit source scan: no `$"PartitionKey` or string concatenation into a filter) |
| TH40 | OData injection | Search never reaches storage; typed ids | `ListSecrets_SearchWithODataSyntax_IsNeverSentToStorage` (search `' or PartitionKey ne '`; no recorded filter contains it), `ListSecrets_ContinuationTokenWithODataSyntax_Returns400` |
| TH41 | Unicode edge cases | Well-formed checks; no control or bidi characters in single-line fields; byte-exact values | `CreateSecret_LoneSurrogateEscape_Returns400`, `CreateSecret_ControlCharacterInName_Returns400`, `CreateSecret_BidiOverrideInName_Returns400`, `CreateSecret_DecomposedUnicodeValue_RoundTripsByteExact`, `CreateSecret_EmojiAndNewlinesInValue_RoundTrip`, `CreateSecret_WhitespaceOnlyName_Returns400`, `CreateSecret_EmptyValueString_Returns400` |
| TH42 | Characters illegal in Table keys | Keys come only from GUIDs | `CreateFolder_NameWithSlashHashQuestionBackslash_Succeeds` (these characters only affect keys, and names are encrypted) |

### 6.6 Generator, purge and concurrency

| Id | Threat | Mitigation | Tests |
|---|---|---|---|
| TH43 | Generator bias or weak randomness | `RandomNumberGenerator` only; whole-draw rejection | `GeneratePassword_DigitsOnly200000Chars_ChiSquareBelowCritical` (df 9, under 44.8), `GeneratePassword_AllSets_EachSymbolFrequencyChiSquareBelowCritical` (94-character alphabet without `RequireEverySet`, df 93, under 172.7), `GeneratePassphrase_WordIndices_ChiSquareBelowCritical` (100 draws per word on average, 777,600 indices, df 7,775, under 8,382.2), `GeneratePassword_RequireEverySet_PositionOfDigitIsUniform` (df 9 over the position of the single digit in 10-character passwords from 9 lowercase letters plus digits: chi-square under 44.8), `PasswordGenerationSource_ContainsNoSystemRandom` |
| TH44 | Misreported entropy | Exact calculation | `PasswordBits_GoldenValues_MatchWithin0001` (V13 values), `PassphraseBits_GoldenValues_Match` |
| TH45 | A tampered or missing word list | SHA-256 check at load | `WordList_Embedded_Has7776UniqueWordsAndExpectedHash`, `WordList_HashCheck_IsPerformedOnLoad` (source scan for the constant) |
| TH46 | Early purge (before 30 days) | Strict `DeletedAt < now - 30 days` | `Purge_ItemDeleted29Days23HoursAgo_IsKept`, `Purge_ItemDeleted30DaysAndOneSecondAgo_IsPurged` (`FakeTimeProvider`) |
| TH47 | Interrupted or duplicated purge | Lease plus checkpoint; ifMatch | `Purge_CancelledAfterFirstUserPage_ResumesFromCheckpoint`, `Purge_RunTwice_SecondRunPurgesNothing`, `Purge_LeaseHeldByOtherInstance_Skips`, `Purge_ExpiredLeaseOfCrashedInstance_IsTakenOver`, `Purge_TwoInstancesInParallel_EachItemPurgedOnceAndAuditedOnce` |
| TH48 | Purge leaves recoverable data | Version rows deleted in the same transaction; key gone | `Purge_Secret_DeletesBinRowAndEveryVersionRow`, `PurgeEndpoint_Secret_DeletesBinRowAndVersions` |
| TH49 | Lost updates and races | ETags, touches, a single key ring | `ParallelUpdatesWithSameETag_ExactlyOneSucceeds`, `TwentyParallelFirstCreates_ShareOneMetadataKey` (one key ring row; every secret decrypts), `ParallelRevealsAndUpdate_RevealReturnsOldOrNewValueNeverMixed` |

## 7. Tests

Conventions:

- Names are `Scenario_Condition_ExpectedResult`. API tests use the `StorageTestGroup` collection; performance tests use `PerformanceTestGroup`.
- Disposal resets storage.
- Each test seeds its own data with unique `PlaintextMarkers`.
- `WarmUpAsync` runs before any storage or Key Vault count.
- Audit assertions filter by `EventType` (the gate writes `SignIn` rows).
- No `Task.Delay` in tests: use `FakeTimeProvider` or polling with a timeout.
- Classes that only hold Security tests carry a class-level `[Trait("Category", "Security")]`.

### 7.1 Authorisation matrix (`ApiTests/Vault/VaultAuthorizationMatrixTests.cs`, Security)

Theory `VaultAuthorizationMatrix_EveryEndpointAndCaller_ReturnsExpectedStatus(string endpointId, CallerKind caller, HttpStatusCode expected)`.

- Arrange: `TestUsers.User` (the owner) creates a folder F, a sub-folder G, a secret S in F with a value change (so one version V exists), and a binned secret B and binned folder C.
- The request for each endpoint uses those ids. A valid body, and `If-Match` from the owner's last read, are always sent, so only authorisation decides the status.
- "Locked owner" locks `TestUsers.User` after the arrangement and calls as `CallerKind.User`.
- Forged covers `AlgNoneToken`, `UnknownKeyToken` and `OtherTenantUser`, each a separate row.

| Endpoint | Anonymous | Forged (3 rows) | Guest | No role | Locked user | Locked admin | Locked owner | Owner (User) | Other user | Admin, not owner |
|---|---|---|---|---|---|---|---|---|---|---|
| E1 `GET /secrets` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 200, own items only | 200, own items only |
| E2 `GET /secrets/{S}` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E3 `POST /secrets` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 201 | 201 (own vault) | 201 (own vault) |
| E3 with `folderId=F` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 201 | 404 | 404 |
| E4 `PUT /secrets/{S}` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E5 `POST /secrets/{S}/move` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E6 `DELETE /secrets/{S}` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 204 | 404 | 404 |
| E7 `POST /secrets/{S}/reveal` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E8 `GET /secrets/{S}/history` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E9 `POST /secrets/{S}/history/{V}/reveal` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E10 `POST /secrets/{S}/history/{V}/restore` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E11 `GET /folders` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 200, own only | 200, own only |
| E12 `POST /folders` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 201 | 201 (own) | 201 (own) |
| E12 with `parentId=F` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 201 | 404 | 404 |
| E13 `PUT /folders/{F}` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E14 `POST /folders/{G}/move` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E15 `DELETE /folders/{G}?mode=MoveToParent` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 204 | 404 | 404 |
| E15 `DELETE /folders/{G}?mode=DeleteContents` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 204 | 404 | 404 |
| E16 `GET /recycle-bin` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 200, own only | 200, own only |
| E17 `POST /recycle-bin/secrets/{B}/restore` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E18 `POST /recycle-bin/folders/{C}/restore` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 200 | 404 | 404 |
| E19 `DELETE /recycle-bin/secrets/{B}` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 204 | 404 | 404 |
| E20 `DELETE /recycle-bin/folders/{C}` | 401 | 401 | 403 | 403 | 403 | 403 | 403 | 204 | 404 | 404 |

Share recipient rows (View, Edit, Manage) are added by `feature/sharing`. There is no share today, and `ISecretAccessPolicy` is the hook.

Companion tests in the same class:

- `VaultAuthorizationMatrix_DeniedCallers_MakeNoVaultStorageOrKeyVaultCalls` (every 401 and 403 row: `VaultRequests()` empty and `KeyWraps.Unwraps == 0`)
- `VaultAuthorizationMatrix_NonOwners_LeaveOwnerDataUnchanged` (every Other user and Admin row: the owner's `Vaults` partition rows and ETags are identical before and after, and no owner audit row is added)
- `VaultAuthorizationMatrix_OtherUserLists_ContainNoneOfOwnersIds`
- `VaultAuthorizationMatrix_EveryEndpoint_NoQueryWithoutPartitionKey`

### 7.2 Unit tests: password generation (`UnitTests/PasswordGeneration/`)

- `PasswordGeneratorTests`:
  - `GeneratePassword_Default_Returns20CharactersFromAllSets`
  - `GeneratePassword_LengthBounds_8And128_Succeed`
  - `GeneratePassword_Length7Or129_ThrowsArgumentException`
  - `GeneratePassword_NoSets_ThrowsArgumentException`
  - `GeneratePassword_UndefinedFlag_ThrowsArgumentException`
  - `GeneratePassword_RequireEverySet_EverySelectedSetPresentIn10000Draws`
  - `GeneratePassword_RequireEverySetLengthBelowSetCount_IsInvalid` (only reachable through `Validate`, since the minimum length is 8; assert the message for length 3 with 4 sets)
  - `GeneratePassword_ExcludeAmbiguous_NeverContainsAmbiguousCharacters`
  - `GeneratePassword_OnlySelectedSets_NoOtherCharacters` (theory over all 15 combinations)
  - `GeneratePassword_TwoCalls_ProduceDifferentValues`
  - `GeneratedSecret_ToString_DoesNotContainValue`
  - the TH43 bias tests
  - `PasswordGenerationSource_ContainsNoSystemRandom`
- `PassphraseGeneratorTests`:
  - `GeneratePassphrase_Default_SixWordsJoinedByHyphen`
  - `GeneratePassphrase_WordCountBounds_3And20_Succeed`
  - `GeneratePassphrase_WordCount2Or21_Throws`
  - `GeneratePassphrase_EverySeparator_UsesItsCharacter`
  - `GeneratePassphrase_UndefinedSeparator_Throws`
  - `GeneratePassphrase_Capitalize_EveryWordStartsUppercase`
  - `GeneratePassphrase_IncludeNumber_ExactlyOneDigitAppendedToOneWord`
  - `GeneratePassphrase_Words_AllComeFromTheList`
  - TH43 passphrase bias
- `EntropyCalculatorTests`:
  - `PasswordBits_GoldenValues_MatchWithin0001` (theory over V13)
  - `PassphraseBits_GoldenValues_Match`
  - `PasswordBits_RequireEverySet_IsBelowUnconstrained`
  - `StrengthOf_Boundaries_MapToExpectedLevel` (39.99, 40, 59.99, 60, 79.99, 80, 99.99, 100)
- `WordListTests`:
  - `WordList_Embedded_Has7776UniqueWordsAndExpectedHash`
  - `WordList_Words_AreLowercaseLettersOrHyphen3To9Chars`
  - `WordList_FirstAndLast_AreAbacusAndZoom`
- `CharacterAlphabetsTests`:
  - `Alphabets_Sizes_Are26_26_10_32`
  - `Alphabets_ExcludeAmbiguous_Sizes_Are25_24_8_31`
  - `Alphabets_AreDisjoint`

### 7.3 Unit tests: vault common (`UnitTests/Application/Vault/`)

- `FolderTreeTests`:
  - `DepthOf_RootLevel_IsOne`
  - `DepthOf_TenLevels_IsTen`
  - `HeightOf_Leaf_IsOne`
  - `HeightOf_ChainOfThree_IsThree`
  - `IsSameOrDescendant_Self_IsTrue`
  - `IsSameOrDescendant_Grandchild_IsTrue`
  - `IsSameOrDescendant_Sibling_IsFalse`
  - `SubtreeOf_ReturnsRootFirstThenAllDescendants`
  - `ChildrenOf_Null_ReturnsRootLevelFolders`
  - `DepthOf_StoredCycle_ThrowsInvalidOperationException`
  - `FolderWithMissingParent_CountsAsRootLevel`
  - `EmptyTree_CountIsZero`
- `VaultTextRulesTests` (pass and fail per rule):
  - `SingleLineText_AtMax_Passes`
  - `SingleLineText_OverMax_FailsWithConstantMessage`
  - `SingleLineText_WhitespaceOnly_Fails`
  - `SingleLineText_ControlCharacter_Fails` (theory `\u0000`, `\u0007`, `\u001b`, `\u007f`, `\u0085`)
  - `SingleLineText_BidiControl_Fails` (theory U+202A to U+202E, U+2066 to U+2069)
  - `SingleLineText_LoneSurrogate_Fails`
  - `SingleLineText_EmojiSurrogatePair_Passes`
  - `MultiLineSecretText_NewlinesAndTabs_Pass`
  - `MultiLineSecretText_LoneSurrogate_Fails`
  - `MultiLineSecretText_Empty_Fails`
  - `HttpUrl_HttpsAndHttp_Pass`
  - `HttpUrl_JavascriptDataFileFtpRelative_Fail` (theory)
  - `HttpUrl_WithWhitespace_Fails`
  - `TagList_TwentyTags_Passes`
  - `TagList_TwentyOne_Fails`
  - `TagList_DuplicateCaseInsensitive_Fails`
  - `TagList_EmptyTag_Fails`
- `ETagRulesTests`:
  - `ValidIfMatch_AzureFormat_Passes`
  - `ValidIfMatch_Wildcard_Fails`
  - `ValidIfMatch_StrongETag_Fails`
  - `ValidIfMatch_Empty_Fails`
  - `ValidIfMatch_Over100Chars_Fails`
  - `ValidIfMatch_InjectedQuote_Fails`
- `SecretMetadataCipherTests` (with `EncryptionHarness`):
  - `Encrypt_ThenTryDecrypt_RoundTripsAllFields`
  - `Encrypt_AbsentUsernameUrlAndTags_StoresNullEnvelopes`
  - `TryDecrypt_NameEnvelopeFromOtherSecret_ReturnsFalse`
  - `TryDecrypt_SwappedNameAndUsername_ReturnsFalse`
  - `TryDecrypt_WrongOwner_ReturnsFalse`
  - `TryDecrypt_VersionMismatch_ReturnsFalse`
  - `TryDecrypt_TagsNotJson_ReturnsFalse`
  - `EncryptFolderName_RoundTrips`
  - `TryDecryptFolderName_FromOtherFolder_ReturnsFalse`
- `VaultKeyRingServiceTests` (NSubstitute `IVaultStore`, `EncryptionHarness`):
  - `OpenAsync_NoRing_ReturnsNullWithoutUnwrap`
  - `OpenAsync_Ring_UnwrapsOnce`
  - `OpenOrCreateAsync_NoRing_CreatesAndAddsOnce`
  - `OpenOrCreateAsync_AddReturnsNull_DisposesOwnKeyAndOpensWinner`
  - `OpenOrCreateAsync_Existing_DoesNotCreate`
- `VaultAuditWriterTests`:
  - `WriteAsync_SecretEvent_SetsActorOwnerTargetSecretTimeAndIp`
  - `WriteAsync_FolderEvent_LeavesSecretIdNull`
  - `WriteAsync_ReasonIsNull`
  - `WriteManyAsync_HundredItems_AppendsHundredEntries`
  - `WriteManyAsync_OneThrows_Propagates`
- `OwnerOnlySecretAccessPolicyTests`:
  - `ResolveAsync_AnySecret_ReturnsCurrentUserAsOwner`
  - `ResolveAsync_AdminCaller_StillReturnsOnlyOwnPartition`
- `VaultSourcesTests` (Security):
  - `VaultSources_DoNotReferenceIsAdmin`
  - `SecretDto_Properties_HaveNoValueOrNotes`
  - `SecretBearingRecords_ToString_ReturnTypeNameOnly`
  - `VaultStoreSources_UseCreateQueryFilterOnly`
  - `VaultStoreSources_NeverUseETagAll`

### 7.4 Unit tests: validators and handlers (`UnitTests/Application/Vault/`)

Each validator gets one passing case and one failing case per rule, named `{Validator}_{Rule}_{Passes|Fails}`. That is `CreateSecretCommandValidator`, `UpdateSecretCommandValidator` (also `ReplaceValueFalseWithValue_Fails` and `ReplaceNotesFalseWithNotes_Fails`), `MoveSecretCommandValidator`, `DeleteSecretCommandValidator`, `RevealSecretFieldCommandValidator` (`UndefinedField_Fails`, `UndefinedIntent_Fails`), `ListSecretsQueryValidator` (`FolderIdAndUnfiled_Fails`, `SearchOver100_Fails`, `SearchWhitespace_Fails`, `TokenNotCanonicalGuid_Fails`), `CreateFolderCommandValidator`, `RenameFolderCommandValidator`, `MoveFolderCommandValidator`, `DeleteFolderCommandValidator` (`ModeZero_Fails`), `ListRecycleBinQueryValidator` (`TokenPattern_Fails`, theory with `x_...`, uppercase hex, 31 hex), `RestoreSecretVersionCommandValidator`.

Handlers (NSubstitute `IVaultStore` returning `Versioned` fixtures, real `EncryptionHarness`, `RecordingAuditLog`, `FakeTimeProvider`):

- `CreateSecretHandlerTests`:
  - `Handle_NoFolder_SubmitsAddSecretOnlyAndAudits`
  - `Handle_InFolder_SubmitsTouchFolderThenAddSecret`
  - `Handle_FolderMissing_ReturnsNotFoundWithoutSubmit`
  - `Handle_SubmitConflict_ReturnsConflictWithoutAudit`
  - `Handle_EncryptsEveryTextFieldWithItsPurpose`
  - `Handle_ValueUsesNewPerSecretKeyAndNameUsesMetadataKey`
  - `Handle_AuditThrows_Propagates`
  - `Handle_IdsAreVersion7`
- `GetSecretHandlerTests`:
  - `Handle_Missing_ReturnsNotFound`
  - `Handle_NoKeyRing_ReturnsDamaged`
  - `Handle_DecryptionFails_ReturnsDamagedWithNullFields`
  - `Handle_NullGrant_ReturnsNotFoundWithoutStoreCall`
- `ListSecretsHandlerTests`:
  - `Handle_NoKeyRing_ReturnsEmptyWithoutSecretQuery`
  - `Handle_PageWithToken_ReturnsLastIdAsToken`
  - `Handle_LastPage_ReturnsNullToken`
  - `Handle_Search_MatchesNameUsernameUrlAndTagCaseInsensitive`
  - `Handle_Search_StopsAtPageSizeAndReturnsLastExaminedId`
  - `Handle_Search_StopsAt5000Scanned`
  - `Handle_DamagedRow_FlaggedNotFailed`
  - `Handle_OpensMetadataSessionOncePerRequest`
- `UpdateSecretHandlerTests`:
  - `Handle_StaleETag_ReturnsConflictWithoutSubmit`
  - `Handle_MetadataOnly_DoesNotOpenSecretKey`
  - `Handle_ReplaceValue_AddsVersionReEncryptedUnderVersionId`
  - `Handle_ReplaceValueWithoutOldValue_AddsNoVersion`
  - `Handle_ReplaceValueAt20Versions_DeletesOldest`
  - `Handle_ReplaceNotesNull_ClearsNotes`
  - `Handle_RevisionIncrements`
  - `Handle_GrantView_ReturnsNotFound`
- `MoveSecretHandlerTests`:
  - `Handle_SameFolder_NoWrite`
  - `Handle_ToRoot_NoTouch`
  - `Handle_ToFolder_TouchesTargetFolder`
  - `Handle_TargetMissing_ReturnsNotFound`
- `DeleteSecretHandlerTests`:
  - `Handle_Owner_RecyclesWithBatchIdAndDeletedAt`
  - `Handle_GrantIsEdit_ReturnsNotFoundAndWritesNothing`
  - `Handle_StaleETag_ReturnsConflict`
- `RevealSecretFieldHandlerTests`:
  - `Handle_Value_DecryptsAndAuditsRevealed`
  - `Handle_CopyIntent_AuditsCopied`
  - `Handle_Notes_AuditsNotesEvent`
  - `Handle_FieldAbsent_ReturnsNotFoundWithoutUnwrap`
  - `Handle_AuditThrows_PropagatesAndReturnsNoValue`
  - `Handle_DecryptionFails_ThrowsSecretDecryptionExceptionWithoutAudit`
  - `Handle_NullGrant_ReturnsNotFound`
- `ListSecretVersionsHandlerTests`:
  - `Handle_SecretMissing_ReturnsNotFound`
  - `Handle_SortsNewestFirst`
- `RevealSecretVersionHandlerTests`:
  - `Handle_VersionMissing_ReturnsNotFound`
  - `Handle_UsesVersionIdAsItemId`
  - `Handle_AuditsVersionRevealed`
- `RestoreSecretVersionHandlerTests`:
  - `Handle_CurrentBecomesNewVersionAndVersionBecomesCurrent`
  - `Handle_NoCurrentValue_AddsNoNewVersion`
  - `Handle_KeepsRestoredVersion`
  - `Handle_PrunesTo20`
  - `Handle_StaleETag_ReturnsConflict`
- `CreateFolderHandlerTests`:
  - `Handle_RootLevel_SubmitsTouchKeyRingThenAddFolder`
  - `Handle_ParentMissing_ReturnsNotFound`
  - `Handle_ParentAtDepth10_ReturnsRejectedDepth`
  - `Handle_1000Folders_ReturnsRejectedLimit`
  - `Handle_FirstEverFolder_CreatesKeyRing`
- `RenameFolderHandlerTests`:
  - `Handle_Missing_ReturnsNotFound`
  - `Handle_UsesServerReadETag`
  - `Handle_Conflict_ReturnsConflict`
- `MoveFolderHandlerTests`:
  - `Handle_IntoSelf_ReturnsRejectedCycle`
  - `Handle_IntoDescendant_ReturnsRejectedCycle`
  - `Handle_TooDeep_ReturnsRejectedDepth`
  - `Handle_ToRoot_Succeeds`
  - `Handle_SameParent_NoWrite`
  - `Handle_StoredCycle_ReturnsRejectedTreeInvalid`
- `DeleteFolderHandlerTests`:
  - `Handle_MoveToParent_MovesSecretsAndChildrenUpAndRecyclesFolder`
  - `Handle_MoveToParentAtRoot_MovesContentsToRoot`
  - `Handle_DeleteContents_RecyclesSubtreeWithOneBatchIdSecretsFirstDeepestFoldersNextRootLast`
  - `Handle_Over1000Items_ReturnsRejectedWithoutSubmit`
  - `Handle_FolderMissing_ReturnsNotFound`
  - `Handle_AuditsEveryItem`
  - `Handle_ChangesStartWithTouchKeyRing`
- `RestoreRecycledSecretHandlerTests`:
  - `Handle_FolderActive_RestoresIntoFolderWithTouch`
  - `Handle_FolderGone_RestoresToRoot`
  - `Handle_NotInBin_ReturnsNotFound`
- `RestoreRecycledFolderHandlerTests`:
  - `Handle_BatchRoot_RestoresWholeBatch`
  - `Handle_Subfolder_RestoresOnlyItsSubtreeToRoot`
  - `Handle_ParentActive_RestoresUnderParent`
  - `Handle_FolderLimitWouldBeExceeded_ReturnsRejected`
- `PurgeRecycledSecretHandlerTests`:
  - `Handle_DeletesBinRowAndVersions`
  - `Handle_NotInBin_ReturnsNotFound`
- `PurgeRecycledFolderHandlerTests`:
  - `Handle_PurgesSubtreeSecretsThenFolders`
  - `Handle_Over1000_ReturnsRejected`
- `RecycleBinPurgerTests` (fakes plus `FakeTimeProvider`):
  - `PurgeExpiredAsync_LeaseNotAcquired_ReturnsSkippedWithoutUserScan`
  - `PurgeExpiredAsync_NewRun_ScansAllUsersAndCompletes`
  - `PurgeExpiredAsync_ExistingCheckpoint_ResumesAfterIt`
  - `PurgeExpiredAsync_CutoffIsNowMinus30Days`
  - `PurgeExpiredAsync_ItemConflict_CountsAndContinues`
  - `PurgeExpiredAsync_LeaseLostDuringRun_Stops`
  - `PurgeExpiredAsync_AuditsWithSystemActorAndNoIp`
  - `PurgeExpiredAsync_Cancelled_KeepsCheckpoint`

### 7.5 Unit tests: infrastructure and API

- `VaultKeysTests`:
  - `SecretRowKey_ActiveAndRecycled_HaveExpectedPrefixes`
  - `VersionRowKey_Format_IsHSecretUnderscoreVersion`
  - `UpperBound_Prefix_IncrementsLastCharacter` (theory `s_` to `` s` ``, `h_{id}_` to `` h_{id}` ``)
  - `FolderKey_Null_IsRoot`
  - `ParseFolderKey_InvalidOrRoot_ReturnsNull`
  - `Version7Ids_NFormat_SortByCreationTime`
- `VaultStorageMappingTests`:
  - `Secret_RoundTripsEveryColumn`
  - `Secret_Recycled_UsesBsPrefix`
  - `Secret_UnknownTypeString_MapsToOther`
  - `Folder_RootParent_UsesRootKey`
  - `KeyRing_RoundTripsWrappedKeyParts`
  - `SecretTableEntity_Properties_ContainNoPlaintextColumns` (Security: the allowed list only)
- `VaultTransactionPlannerTests`:
  - `Plan_Empty_ReturnsNoTransactions`
  - `Plan_RecycleSecret_ProducesDeleteThenAdd`
  - `Plan_150AddSecrets_SplitsInto100And50`
  - `Plan_WithTouchKeyRing_EveryChunkStartsWithTouchAnd99Changes`
  - `Plan_TouchKeyRingNotFirst_ThrowsArgumentException`
  - `Plan_TwoTouchKeyRings_ThrowsArgumentException`
  - `Plan_LargeEnvelopes_SplitByPayloadBelow3MiB`
  - `Plan_RecycleIsNeverSplitAcrossChunks`
  - `Plan_PrimaryActionIndexes_PointAtAddOrReplace`
  - `Plan_RecycleWithoutDeletedAt_ThrowsArgumentException`
- `TableMaintenanceLeaseStoreTests` (fake `TableClient`, the same style as `FakeUsersTableClient`):
  - `TryAcquire_NoRow_Adds`
  - `TryAcquire_HeldByOtherNotExpired_ReturnsNull`
  - `TryAcquire_Expired_Replaces`
  - `TryAcquire_PreconditionFailed_ReturnsNull`
  - `Renew_Lost_ReturnsNull`
- `RecycleBinPurgeOptionsValidatorTests`: one pass and one fail per range.
- `RecycleBinPurgeServiceTests` (`FakeTimeProvider`, substitute purger):
  - `Disabled_NeverCallsPurger`
  - `Enabled_RunsAfterInitialDelay`
  - `Enabled_RunsAgainEveryInterval`
  - `PurgerThrows_LogsAndContinues`
  - `Stop_CancelsCleanly`
- `ResultHttpExtensionsTests` additions:
  - `ToHttpResult_NotFound_Returns404WithConstantTitle`
  - `ToHttpResult_Conflict_Returns409`
  - `ToHttpResult_Rejected_Returns400WithDetail`
  - `ToHttpResult_Invalid_StillReturns400WithoutDetail`
  - `ToCreatedHttpResult_Success_Returns201WithLocation`
  - `ToNoContentHttpResult_Success_Returns204`
- `SecretDecryptionExceptionHandlerTests`:
  - `TryHandle_DecryptionException_Writes500GenericProblem`
  - `TryHandle_OtherException_ReturnsFalse`
  - `TryHandle_EveryReason_ProducesIdenticalBody`
- `VaultRateLimitOptionsValidatorTests`: pass and fail at 0, 1, 100,000 and 100,001.
- `HttpContextRequestMetadataTests`:
  - `IpAddress_FromConnection_IsNormalized`
  - `IpAddress_NoContext_IsNull`

### 7.6 API integration: functional (`ApiTests/Vault/`)

- `SecretEndpointsTests`:
  - `CreateSecret_AllFields_Returns201WithLocationAndNoValue`
  - `CreateSecret_ThenGet_ReturnsSameMetadata`
  - `CreateSecret_SecureNoteWithoutValue_HasValueFalse`
  - `ListSecrets_Empty_ReturnsEmptyPageWithoutToken`
  - `ListSecrets_OneItem_ReturnsItWithoutToken`
  - `ListSecrets_Exactly50_PageSize50_ReturnsAllAndNoFurtherItems`
  - `ListSecrets_51_PageSize50_ReturnsTokenAndSecondPageHasOne`
  - `ListSecrets_DefaultOrder_IsCreationOrder`
  - `ListSecrets_FolderFilter_ReturnsOnlyThatFolder`
  - `ListSecrets_Unfiled_ReturnsOnlyRootSecrets`
  - `ListSecrets_Search_FindsByNameUsernameUrlAndTag`
  - `ListSecrets_SearchAcrossPages_ContinuesWithToken`
  - `UpdateSecret_MetadataOnly_KeepsValueAndAddsNoVersion`
  - `UpdateSecret_ReplaceValue_AddsVersionAndRevealsNewValue`
  - `UpdateSecret_ClearNotes_HasNotesFalse`
  - `MoveSecret_BetweenFolders_UpdatesFolderIdOnly`
  - `MoveSecret_ToRoot_Succeeds`
  - `DeleteSecret_MovesToRecycleBin`
  - `RevealValue_ReturnsExactValue`
  - `RevealNotes_ReturnsExactNotes`
  - `RevealValue_SecretWithoutValue_Returns404`
- `SecretHistoryTests`:
  - `History_AfterThreeValueChanges_ListsThreeNewestFirst`
  - `HistoryReveal_ReturnsPreviousValue`
  - `HistoryRestore_SwapsValuesAndKeepsBoth`
  - `UpdateValue_TwentyFiveTimes_KeepsTwentyNewestVersions`
  - `History_AfterRestoreFromBin_IsIntact`
- `FolderEndpointsTests`:
  - `CreateFolder_RootAndNested_ReturnTree`
  - `ListFolders_SortedByName`
  - `RenameFolder_UpdatesName`
  - `MoveFolder_ToOtherParent_Succeeds`
  - `MoveFolder_ToRoot_Succeeds`
  - the TH16 and TH17 tests
  - `CreateFolder_NameWithSlashHashQuestionBackslash_Succeeds`
- `FolderDeleteTests`:
  - `DeleteFolder_MoveToParent_SecretsAndChildrenMoveUp`
  - `DeleteFolder_MoveToParentAtRoot_ContentsToRoot`
  - `DeleteFolder_DeleteContents_SubtreeAndSecretsInBinWithOneBatch`
  - `DeleteFolder_MissingMode_Returns400AndNothingChanges`
  - `DeleteFolder_UnknownMode_Returns400`
  - `DeleteFolder_FiftyMaxSizeSecrets_MovesAllToBin` (payload chunking)
  - `DeleteFolder_250Secrets_SpansThreeTransactions` (recorder: at least three `$batch` requests)
  - `DeleteFolder_SecondChunkConflicts_Returns409AndRetryCompletes`
- `RecycleBinTests`:
  - `RecycleBin_ListsFoldersThenSecretsWithPurgeAfter`
  - `RecycleBin_Paging_WithToken`
  - `RestoreSecret_ToOriginalFolder`
  - `RestoreSecret_OriginalFolderDeleted_ToRoot`
  - `RestoreFolder_RestoresBatch`
  - `RestoreFolder_SubfolderOfBatch_RestoresItsSubtreeToRoot`
  - `RestoreFolder_ParentNowTooDeep_ToRoot`
  - `PurgeEndpoint_Secret_DeletesBinRowAndVersions`
  - `PurgeEndpoint_Folder_PurgesSubtree`
  - `RestoreAfterPurge_Returns404`
- `RecycleBinPurgeTests` (purger resolved from `factory.Services`, `FakeTimeProvider` through `CreateTimedFactory`):
  - TH19
  - TH46
  - TH47
  - `Purge_Secret_DeletesBinRowAndEveryVersionRow`
  - `Purge_Run_QueriesAllHavePartitionKey`
  - `Purge_WritesSecretPurgedWithSystemActor`
  - `Purge_SkipsUsersWithEmptyBins_WithOneQueryEach`
- `SecretAuditLogTests` (AUD-5 groundwork):
  - `SecretEvent_WritesIndexRowInOwnerPartitionWithSecretPrefix`
  - `FolderEvent_WritesNoIndexRow`
  - `IndexRows_SortNewestFirstPerSecret`
  - `IndexWriteFails_RevealReturns500WithoutValue`
- `VaultStoreTests` (store resolved from services, real Azurite):
  - `SubmitAsync_ChainedKeyRingTouch_AllChunksCommit`
  - `SubmitAsync_StaleTouch_ReturnsConflictAndNoChange`
  - `SubmitAsync_MissingDelete_ReturnsNotFound`
  - `SubmitAsync_ReturnsETagsAlignedWithChanges`
  - `ListSecretsAsync_FolderFilterOnlyMatchingRows`
  - `ListRecycledBeforeAsync_UsesDeletedAtFilter`
  - `AddKeyRingAsync_Twice_SecondReturnsNull`

### 7.7 API integration: security (`ApiTests/Vault/`, Security)

- `VaultIdorTests`: TH1 to TH5, TH9.
- `VaultAdminIsolationTests`: TH6.
- `VaultLockTests`: TH7.
- `VaultMassAssignmentTests`: TH10.
- `VaultTamperingTests`: TH12 to TH15, using `VaultRows`.
- `VaultConcurrencyTests`: TH16 (parallel), TH18, TH20, TH37 (parallel), TH49.
- `VaultLeakageTests`: TH21 to TH28, TH32 (integration part).
- `VaultInputTests`: TH33, TH34, TH38, TH40, TH41, TH42.
- `VaultRateLimitTests`: TH36. Each uses `Factory.WithWebHostBuilder` with low limits, so the shared factory keeps its high limits.
- `VaultStorageRulesTests`: TH39 (theory over E1 to E20), plus `BinnedItems_NeverReturnedByActiveEndpoints` (TH8).

### 7.8 Storage and Key Vault call counts (`ApiTests/Vault/VaultCallCountTests.cs`)

One test per endpoint, `{Endpoint}_Calls_MatchBudget`. Each asserts the exact counts in section 8 (vault reads, vault writes including `$batch`, audit writes, `KeyWraps.Unwraps` and `KeyWraps.Wraps`) after `WarmUpAsync` and `ResetCounters`.

The names are:

- `ListSecrets_Calls_MatchBudget`
- `ListSecretsWithSearch_Calls_MatchBudget`
- `GetSecret_Calls_MatchBudget`
- `CreateSecretFirstEver_Calls_MatchBudget`
- `CreateSecret_Calls_MatchBudget`
- `CreateSecretInFolder_Calls_MatchBudget`
- `UpdateSecretMetadata_Calls_MatchBudget`
- `UpdateSecretValue_Calls_MatchBudget`
- `MoveSecret_Calls_MatchBudget`
- `DeleteSecret_Calls_MatchBudget`
- `RevealValue_Calls_MatchBudget`
- `ListHistory_Calls_MatchBudget`
- `RevealVersion_Calls_MatchBudget`
- `RestoreVersion_Calls_MatchBudget`
- `ListFolders_Calls_MatchBudget`
- `CreateFolder_Calls_MatchBudget`
- `RenameFolder_Calls_MatchBudget`
- `MoveFolder_Calls_MatchBudget`
- `DeleteFolderMoveToParent_Calls_MatchBudget`
- `DeleteFolderDeleteContents_Calls_MatchBudget`
- `ListRecycleBin_Calls_MatchBudget`
- `RestoreSecret_Calls_MatchBudget`
- `RestoreFolder_Calls_MatchBudget`
- `PurgeSecret_Calls_MatchBudget`
- `PurgeFolder_Calls_MatchBudget`
- `ListSecretsPageOf50_UnwrapsExactlyOnce` (the D1 proof)

### 7.9 Cross-cutting

- `VaultResponsesTests`:
  - `EveryVaultResponse_HasNoStoreAndSecurityHeaders`
  - `OpenApiDocument_ListsAllTwentyVaultOperations` (Development only)

### 7.10 UI component tests (bUnit, `UnitTests/UiShared/Vault/`)

Fakes: a substitute `IVaultApiClient`, `FakeClipboard` and `FakeTimeProvider`. `BunitContext` registers `SecretClipboard`, `VaultUiSettings.Default`, a `TestAuthorization` signed-in user and `JSInterop.Mode = JSRuntimeMode.Strict` (fails on any unexpected JS call).

- `VaultPageTests`:
  - `Render_LoadsFirstPageAndShowsRowsWithIds`
  - `Render_Empty_ShowsVaultEmpty`
  - `Search_Submit_CallsListWithSearch`
  - `LoadMore_CallsWithContinuationToken`
  - `LoadMore_EmptyPage_HidesButton`
  - `CopyFromList_CallsRevealWithCopyIntentAndWritesClipboard`
  - `CopyFromList_RateLimited_ShowsMessageAndNoClipboardWrite`
  - `SignInRequired_NavigatesToSignInWithReturnUrl`
  - `DamagedRow_ShowsDamagedMarker`
  - `Rows_SortedByNameWithinLoadedItems`
- `FolderTreeTests`:
  - `Render_BuildsNestedTreeFromFlatList`
  - `Toggle_ExpandsChildren`
  - `FolderLink_NavigatesToFolderRoute`
- `FolderDeleteDialogTests`:
  - `Initially_NoModeSelectedAndConfirmDisabled`
  - `SelectMove_EnablesConfirmAndSendsMoveToParent`
  - `SelectDelete_SendsDeleteContents`
  - `Rejected_ShowsMappedDetail`
- `FolderEditorDialogTests`:
  - `Save_EmptyName_ShowsError`
  - `Save_CallsCreateWithParent`
- `SecretDetailPageTests`:
  - `Render_ShowsMaskedValueAndNeverCallsReveal`
  - `RevealValue_CallsRevealOnceWithViewAndShowsValue`
  - `HideValue_RemovesValueFromMarkup`
  - `AfterAutoHideDelay_HidesValue`
  - `CopyValue_CallsRevealWithCopyAndWritesClipboardWithoutRendering`
  - `AfterClipboardDelay_ClipboardCleared`
  - `RevealNotes_CallsRevealWithNotes`
  - `RateLimited_ShowsMessageWithoutValue`
  - `NotFound_ShowsGoneMessage`
  - `Dispose_DropsRevealedValue`
  - `NameWithMarkup_RendersEncoded`
  - `NonHttpUrlFromApi_RendersAsTextNotLink`
  - `HttpsUrl_RendersLinkWithNoopener`
  - `Delete_ConfirmSendsETag`
- `SecretEditorPageTests`:
  - `New_SaveSendsDraft`
  - `Edit_ValueInputsDisabledUntilChangeChecked`
  - `Edit_ChangeNotes_RevealsNotesWithViewIntent`
  - `Edit_SaveSendsReplaceFlagsAndETag`
  - `Edit_Conflict_ShowsConflictPanel`
  - `ValueInput_IsPasswordTypeWithNewPasswordAutocomplete`
  - `Generate_UseFillsValue`
- `PasswordGeneratorDialogTests`:
  - `Open_ShowsPasswordWithEntropyAndStrength`
  - `ChangeLength_Regenerates`
  - `UncheckAllSets_ShowsErrorAndDisablesUse`
  - `PassphraseMode_ShowsWords`
  - `Copy_WritesClipboardWithoutApiCall`
  - `Use_RaisesCallbackWithValue`
- `SecretHistoryPageTests`:
  - `Render_ListsVersionsWithoutValues`
  - `Reveal_CallsVersionReveal`
  - `Restore_ConfirmSendsSecretETag`
  - `Restore_Conflict_ShowsConflict`
- `RecycleBinPageTests`:
  - `Render_ShowsExpiry`
  - `Restore_CallsRestoreForKind`
  - `Purge_RequiresConfirm`
- `UiMarkupTests` (Security):
  - `AllVaultComponents_EveryInteractiveElement_HasId` (renders every page and dialog with data, then checks every `button`, `a`, `input`, `select` and `textarea`)
  - `AllVaultComponents_Ids_AreUniquePerRender`
  - `AllVaultComponents_NoInlineStyleOrHandlerAttributes`
- `SecretClipboardTests`: the TH31 clipboard tests, plus `Copy_ClipboardThrows_DoesNotThrowToCaller`.

### 7.11 Web tests (`IntegrationTests/WebTests/` and `UnitTests/Web/`)

- `VaultPageWebTests` (WebTestGroup, Security):
  - `VaultPages_InitialHtml_ContainNoSecretDataAndNoApiCall` (signed-in cookie; every vault route returns 200 with the Blazor shell, the fake API handler records no call and the HTML has no `vault-secret-row`)
  - `AccountPage_StillStaticRendered` (no `<!--Blazor:` interactive marker for `/account`)
  - `VaultPages_NoInlineScriptHandlerOrStyle`
  - `ClipboardModule_IsServedAsLocalAsset`
- `VaultApiClientTests` (unit, fake `HttpMessageHandler`):
  - `ListSecrets_BuildsEscapedQuery`
  - `RevealSecret_PostsFieldAndIntentAsStrings`
  - `UpdateSecret_SendsIfMatchHeader`
  - `Status404_ReturnsNotFound`
  - `Status409_ReturnsConflict`
  - `Status429_ReturnsRateLimited`
  - `Status400_ReturnsInvalidWithDetail`
  - `Status401_ReturnsSignInRequired`
  - `Status403_ReturnsAccessDenied`
  - `Status500_ReturnsFailed`
  - `NoBodyOrValueIsLogged`
  - `DeleteSecret_204_ReturnsSuccessTrue`
- `VaultApiRegistrationTests` (unit):
  - `VaultClient_PostIsNotRetriedOn503` (handler returns 503 once; exactly one request)
  - `VaultClient_GetIsRetriedOn503`
- `SharpVaultApiClientTests`: the existing tests stay green after the transport refactor.
- `RepositoryScanTests` (unit, Security):
  - `ClipboardModule_Source_UsesOnlyWriteText`
  - `WebJs_OnlyClipboardAndReconnectModules_Exist` (only `wwwroot/js/clipboard.js` and `Components/Layout/ReconnectModal.razor.js` among `.js` files under `src`)

### 7.12 Performance (`ApiTests/Vault/VaultPerformanceTests.cs`, PerformanceTestGroup, Performance)

Seed with `VaultSeeder`. Each operation runs 5 warm iterations, then 20 measured iterations. Assert the median under the budget and the 95th percentile under twice the budget. Storage and unwrap counts are asserted on one iteration.

- `ListSecrets_PageOf50FromVaultOf5000_Under500Ms`
- `ListSecrets_FolderFilterInVaultOf5000_Under500Ms`
- `Search_FullScanOf5000_Under1500Ms`
- `GetSecret_InVaultOf5000_Under200Ms`
- `RevealValue_Under200Ms`
- `CreateSecret_Under300Ms`
- `UpdateSecretValue_Under300Ms`
- `ListFolders_1000Folders_Under500Ms`
- `DeleteFolderDeleteContents_10Folders500Secrets_Under5Seconds`
- `RestoreFolder_10Folders500Secrets_Under5Seconds`
- `PurgeRun_1000ExpiredItemsAcross10Users_Under30Seconds`
- `TwentyParallelReveals_AllUnder1Second`

Benchmarks (`tests/SharpVault.Benchmarks`, `[MemoryDiagnoser]`):

- `PasswordGenerationBenchmarks`: `GeneratePassword20AllSets`, `GeneratePassword128AllSets`, `GeneratePassphrase6Words`, `EntropyRequireEverySet128`.
- `SecretMetadataBenchmarks`: `DecryptPageOf50Summaries` (one session, four fields each) and `EncryptSecretForCreate`.
- `VaultEntityMappingBenchmarks`: `SecretToTableEntity` and `TableEntityToSecret`.

The pull request description includes the `--job short` results.

### 7.13 Commands the reviewer runs

```bash
dotnet --info
docker info
dotnet build SharpVault.slnx
dotnet test SharpVault.slnx --filter "Category!=Performance"
dotnet test SharpVault.slnx --filter "Category=Security"
dotnet test SharpVault.slnx --filter "Category=Performance"
dotnet list SharpVault.slnx package --vulnerable --include-transitive
dotnet test SharpVault.slnx --filter "Category!=Performance" --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults
dotnet run eng/CoverageGate.cs -- TestResults/coverage/Cobertura.xml SharpVault.Application=80 SharpVault.Infrastructure=80 SharpVault.Domain.Encryption=95 SharpVault.Infrastructure.Encryption=95 SharpVault.PasswordGeneration=95
dotnet run -c Release --project tests/SharpVault.Benchmarks -- --filter '*' --job short
```

Use the coverage collection command from `docs/TESTING.md` if it differs.

## 8. Performance budgets

Measured against Azurite with the development key provider. In production each unwrap is one Key Vault `unwrapKey` round trip (typically tens of milliseconds in region), which is why unwrap counts are budgeted separately. Counts exclude the access gate's `Users` read (0 in production within the 5-second cache, 1 in tests with CacheTtl 0).

| Endpoint | Vault reads (requests) | Vault writes (requests) | Audit writes | Unwraps | Wraps | Budget (median) |
|---|---|---|---|---|---|---|
| E1 list, no search | 2 (key ring, 1 page) | 0 | 0 | 1 (0 without a key ring, with 1 read) | 0 | 500 ms, page of 50 from 5,000 |
| E1 list, search | 1 + up to 5 pages | 0 | 0 | 1 | 0 | 1,500 ms scanning 5,000 |
| E2 get | 2 | 0 | 0 | 1 | 0 | 200 ms |
| E3 create, first ever | 1 | 2 (key ring add, secret add) | 2 | 0 | 2 (metadata key, secret key) | 300 ms |
| E3 create | 1 | 1 | 2 | 1 | 1 | 300 ms |
| E3 create in folder | 2 | 1 (`$batch`) | 2 | 1 | 1 | 300 ms |
| E4 update, metadata only | 2 | 1 | 2 | 1 | 0 | 300 ms |
| E4 update with value | 3 (secret, key ring, versions) | 1 (`$batch`) | 2 | 2 | 0 | 300 ms |
| E5 move | 3 (secret, folder, key ring) | 1 | 2 | 1 | 0 | 200 ms |
| E6 delete | 1 | 1 (`$batch`) | 2 | 0 | 0 | 200 ms |
| E7 reveal | 1 | 0 | 2 | 1 | 0 | 200 ms |
| E8 history list | 2 | 0 | 0 | 0 | 0 | 200 ms |
| E9 history reveal | 2 | 0 | 2 | 1 | 0 | 200 ms |
| E10 history restore | 4 (secret, version, key ring, versions) | 1 (`$batch`) | 2 | 2 | 0 | 300 ms |
| E11 folders | 2 | 0 | 0 | 1 | 0 | 500 ms with 1,000 folders |
| E12 create folder | 2 | 1 (`$batch`) | 1 | 1 | 0 | 300 ms |
| E13 rename | 2 | 1 | 1 | 1 | 0 | 200 ms |
| E14 move folder | 2 + 1 (key ring for the name) | 1 (`$batch`) | 1 | 1 | 0 | 300 ms |
| E15 `MoveToParent` (n secrets, c children) | 3 | ceil((n + c + 1) / 99) | 2n + c + 1 | 0 | 0 | 3 s for n = 500 |
| E15 `DeleteContents` (k folders, n secrets) | 2 + k | chunks by 99 actions and 3 MiB | 2n + k | 0 | 0 | 5 s for k = 10, n = 500 |
| E16 recycle bin | 2 | 0 | 0 | 1 | 0 | 500 ms for 50 |
| E17 restore secret | 2 or 3 (bin row, folder, key ring) | 1 (`$batch`) | 2 | 1 | 0 | 200 ms |
| E18 restore folder (k, n) | 5 (bin row, key ring, folders, batch, key ring open) | chunks | 2n + k | 1 | 0 | 5 s for 500 |
| E19 purge secret | 2 (bin row, versions) | 1 (`$batch`) | 2 | 0 | 0 | 200 ms |
| E20 purge folder (k, n) | 2 + n | n + ceil(k / 99) | 2n + k | 0 | 0 | 10 s for n = 500 |
| Purge run (U users, X expired items) | ceil(U / 100) user pages + U bin queries + versions query per secret | 1 per item + lease renewals | 2 per secret, 1 per folder | 0 | 0 | 30 s for 1,000 items across 10 users |

- "Wraps" are `IKeyWrapProvider.WrapAsync` calls. With Key Vault Crypto User (key read), these are local and make 0 remote calls (ENC section on call counts).
- The key ring read and the item read run in parallel wherever both are needed (E2, E4, E10, E11, E12, E13). Audit writes for bulk operations run 8 at a time.
- Hot paths with benchmarks: password generation, metadata decryption of a page, entity mapping (7.12).
- Every list endpoint has a maximum page size (200; folders are capped at 1,000 by the product limit; history at 20).

## 9. Risks and decisions

| # | Topic | Decision and reason |
|---|---|---|
| D1 | Data key per field (encryption-core D5) | **Per-user metadata key** for name, username, URL, tags and folder name. **Per-secret key** for value, notes and history values. A page of 50 per-secret-key names would cost 50 Key Vault unwraps per request, which breaks the 500 ms budget and Key Vault throttling. With D1 a list page, a search request and a folder list each cost exactly 1 unwrap. CLAUDE.md's "each secret value has its own random data key" still holds. Usernames and URLs sit under the metadata key so the list can show them without a second key. They are still encrypted (SEC-2). The key ring row is created lazily on the first create. On an insert race the loser disposes its session and opens the winner's key (`TwentyParallelFirstCreates_ShareOneMetadataKey`). Blast radius: one unwrapped metadata key exposes one user's names, which is the same trust boundary as the server itself (not zero-knowledge). |
| D2 | Transfers and sharing with D1 (encryption-core D5a) | **TRF-7**: `feature/offboarding-transfers` adds `byte[] ReEncryptTo(EncryptionContext source, IDataKeySession target, EncryptionContext targetContext, byte[] envelope)` to `IDataKeySession`. `DataKeySession` decrypts into its own buffer and seals with `target`'s key (the target must be a `DataKeySession`), with no string and with buffers zeroed. Name, username, URL, tags and folder names move from the source metadata key to the target's metadata key with it. Values, notes and history move with the existing `ReEncrypt`, under the same per-secret key, changing only the owner in the associated data. Secret ids are kept (TRF-8 shares stay keyed by secret id). A transfer of 1,000 secrets costs 1 + 1 + 1,000 unwraps. The member is **not** added here, because it would have no caller and no feature tests. **SHR**: the owner's metadata key is opened on the server for recipients, and only shared rows are decrypted, so 1 unwrap per distinct owner on a "Shared with me" page. Recommendation for sharing: `SecretShares` RowKey `{ownerId}_{secretId}` so rows group by owner (SHR-5). |
| D3 | One `Vaults` table instead of four (Secrets, Folders, SecretHistory, RecycleBin) | Entity group transactions only work within one table and one partition. "Update value and write history and prune", "move to bin", "folder change plus structure lock" and "secret into folder plus folder lock" must each be atomic, and V1 to V3 show they are with one table. RowKey prefixes keep each query a narrow range. The skill's `Secrets` table (PK owner, RK secret id) is honoured in spirit: same partitioning, prefixed row keys. |
| D4 | Concurrency model | Client `If-Match` for secret edits (lost-update protection for passwords). Key ring touch for structural folder changes (prevents cycles and limit races without a distributed lock). Folder touch for secret-into-folder (prevents orphans). All verified (V1). Cost: two parallel creates into the **same** folder can make one return 409. That is rare, and the UI offers a reload. |
| D5 | Folder delete semantics (FLD-3) | `MoveToParent` moves direct secrets and direct child folders to the parent. `DeleteContents` bins the whole subtree as one batch. The mode is required, with no default, so a missing mode can never delete. Restore uses the batch. Restoring a sub-folder of a batch restores only its subtree. A missing original parent means the root. |
| D6 | URL encrypted | SEC-2 does not list URLs. They reveal which internal systems a user can reach, so they are encrypted under the metadata key at no extra unwrap cost. |
| D7 | Reveal and copy (SEC-3) | One endpoint per field type with `intent` `View` or `Copy`, and no separate "copy happened" endpoint. An audit-only endpoint depends on the client choosing to call it. Tying the audit to the delivery of the value guarantees that every value that leaves the server is audited. Handler order: grant, read, field present, open key, decrypt, audit, return. A failed audit means no value. Notes are revealed explicitly too (secure notes are their own secret), so detail responses never include notes. |
| D8 | Version 7 GUIDs | Storage order (by RowKey) is then creation order, so lists and history are stable and meaningful without decrypting. Ids are not secrets (authorisation comes from the partition), and version 7 still has 74 random bits. |
| D9 | Search and sort with encrypted names | Server-side, bounded: decrypt and filter up to 5,000 rows per request, with a token to continue. The term never reaches storage. Sorting by name happens in the UI over the loaded items. An exact server sort would need the whole vault decrypted per request. Client-only search would mean shipping the whole vault index to every circuit. Accepted: very large vaults (over 5,000 secrets) need more than one request per search. |
| D10 | History scope (SEC-5) | Only the **value** is versioned, up to 20 versions, pruned in the same transaction. Each version uses its own id as the item id (ENC-D24), re-encrypted with `ReEncrypt` under the secret's key, so no string is created and a version cannot be swapped into the current value. Rolling back a whole row stays a residual risk (ENC-D24). `Revision` is stored for a future check. |
| D11 | Folder rename concurrency | Server-read ETag (last writer wins). Touches change folder ETags often, so a client ETag would cause spurious 409s. A folder name is low-stakes compared with a password. Structural changes stay fully serialised (D4). |
| D12 | Operations larger than one transaction | Chunks commit in order, with the key ring touch chained through every structural chunk. A later conflict returns 409 with earlier chunks applied. Handlers recompute from current state, so a retry completes. Capped at 1,000 items per operation, and visible to the user as "changed by another request". |
| D13 | Audit ordering for mutations | Data write first, then audit. Writing the audit first would record changes that never happened on every ETag conflict. **Residual risk**: if the audit insert fails after a successful write, the change is stored without its entry. The API returns 500 and logs an Error with the event type and item id. Reveals are fail-closed (D7). Review in `feature/audit-log`. |
| D14 | Rate limiting | ASP.NET Core rate limiter, fixed window per user oid: 600 vault requests and 60 reveals per minute. In-memory per instance, so with N instances the effective limit is N times higher. Accepted for now. A distributed limiter is a follow-up. |
| D15 | Generator as a library, no API endpoint | It runs where the UI runs (the Blazor Server circuit today, on-device in MAUI later). Generated passwords cross the network only when saved. There is no endpoint to protect, rate-limit or audit. A new project keeps the layering clean: UI.Shared cannot reference Application, and Application does not need it yet. An endpoint can be added later (for a browser extension) without changing the library. |
| D16 | Word list licensing | The EFF large list (CC BY 3.0 US) is bundled **unmodified** as a separate data file, with attribution in `THIRD-PARTY-NOTICES.md` and next to the file. CC BY only needs attribution. It is data, not code, so the AGPL code license is unaffected. GPL and AGPL password managers such as KeePassXC and Bitwarden ship the same list. A self-made list was rejected (low quality, entropy hard to justify), and so was BIP-39 (MIT, 2,048 words, 11 bits per word: 7 words needed for about 77 bits, against 6 here). The SHA-256 is pinned and checked at load and in tests. |
| D17 | System actor | `AuditActors.System = Guid.Empty`. Real users can never have an empty oid (V14), and `AuditEntry.ActorId` stays non-nullable. |
| D18 | Clipboard needs JavaScript | Blazor has no clipboard API. CLAUDE.md allows JS interop for Web Crypto in one-time links, and the Blazor skill expects "Minimal JS interop (for example, clipboard)" behind a platform interface. **Decision**: one 7-line module with `navigator.clipboard.writeText` only (no read, no third-party code), in the **Web** host behind `IClipboard`. MAUI will use its native clipboard. The clear runs in .NET with `TimeProvider`. **The orchestrator should tell the owner (SwartBergStudio), because this widens the CLAUDE.md JS exception from one case to two.** Risk: Safari needs the write inside the user gesture, and with Blazor Server the write happens after a server round trip, so Safari may refuse. Chromium, Edge and Firefox accept it within the activation window. The UI shows a "Copy failed" message in that case. Follow-up recorded. |
| D19 | Damaged rows | A row that fails decryption, has an unknown key version or has malformed tags is returned with `isDamaged = true` and no text. One tampered row must not take down a whole vault view. The session already logs a Warning with the item id, purpose and reason. Reveal of a damaged value is a generic 500. |
| D20 | Render mode | Interactive Server **without prerender** for vault routes, set in the Web host's `App.razor` (RCL components in UI.Shared must not declare render modes, or MAUI breaks). Account pages are excluded with `[ExcludeFromInteractiveRouting]`, so `feature/entra-sign-in`'s static pages and their tests stay as they are. Without prerender, secret data never appears in the first HTML response and the API is not called twice. Risk: navigating from an interactive page to an excluded page relies on Blazor's full reload for excluded routes. The implementer checks it manually in a browser, and it is covered by Playwright later (D21). |
| D21 | Playwright deferred again (foundation D23) | `cdn.playwright.dev` and `playwright.download.prss.microsoft.com` are blocked from the cloud session (checked), so the bundled browsers cannot be installed. `/usr/bin/google-chrome` exists and Playwright's `Channel = "chrome"` could use it, but end-to-end setup (Kestrel hosting for Web and API, a test OIDC sign-in, the API token) is a feature-sized piece of work. This plan covers UI behaviour with bUnit (V8) and HTTP-level Web tests, and keeps every id Playwright-ready. Follow-up: `feature/e2e-playwright` with the system Chrome channel. |
| D22 | HTTP retries from the Web to the API | The standard resilience handler would retry POST, PUT and DELETE (duplicate creates, double reveals) and 429s. `VaultApiClient` replaces it with one that has `DisableForUnsafeHttpMethods()` (V9). GETs keep retries. |
| D23 | `Result<T>` failure kinds | `NotFound`, `Conflict` and `Rejected` added, with `Invalid` the default, so existing `Failure(...)` calls and their 400 mapping are unchanged. NotFound covers "does not exist" and "not yours" with one body. |
| D24 | DTO mapping | Mapster maps domain entities to table entities (Infrastructure). DTOs are built by `SecretDtoFactory` and `FolderDtoFactory`, because every text field needs decrypting. This is a deliberate exception to "map with `.Adapt<T>()`". |
| D25 | Card type | `Card` is a type label. The card number goes in Value, and expiry and CVV in Notes. Structured card fields are a later-phase item. |
| D26 | Admin settings and idle auto-lock | The clipboard clear delay and the reveal auto-hide delay are 30 s from `VaultUiSettings.Default`. ADM-4 will provide them from settings. Idle auto-lock is a follow-up. |
| D27 | AUD-5 | Only the write side here: index rows per owner and secret, insert-only, written by `TableAuditLog`. `IAuditLog` keeps its single method, so `AuditLogContractTests` stays unchanged. `feature/audit-log` adds the query and the UI. On transfer, the TRF plan decides whether index rows move to the new owner. |
| D28 | Limits | Name 200, username 256, URL 2,048, value and notes 8,192 characters (at most 32,768 UTF-8 bytes, within the envelope limit), 20 tags of 50 characters, folder name 100, depth 10, 1,000 folders, 1,000 items per folder operation, 20 versions, page size 200, search scans of 5,000. All are constants in `VaultLimits`. PEM keys (RSA 4096 is about 3,200 characters) fit. |
| D29 | `If-Match` and the ETag in the body | Requests send `If-Match`. Responses carry the ETag in the DTO body (simpler for the Blazor client than reading headers). The strict format check rejects `*` (TH18). |
| D30 | Dependency on `feature/entra-sign-in` | T0 merges it first. The plan relies on its test infrastructure (`ApiSecurityTestBase`, `ThrowingAuditLog`, `CreateTimedFactory`, `TestUsers`). Adding `CallerKind.OtherUser` is safe: `AuthorizationTests` enumerates `CallerKind` only for anonymous paths, and `CallerClients.UserFor` maps it. |

## 10. Definition of done for this branch

- T1 to T22 complete. `dotnet build` has 0 warnings. All tests pass, including `Category=Security` and `Category=Performance`. Coverage gates pass, including `SharpVault.PasswordGeneration=95`. The vulnerable package check is clean.
- Every threat in section 6 has its named test passing. The full matrix in 7.1 passes. Every endpoint's storage and unwrap counts in section 8 are asserted (7.8), and no query runs without a partition key.
- No comments, TODOs or commented-out code. AGPL headers present. No `style=` attributes or inline scripts. Every interactive element has an id.
- The orchestrator has told the owner about D18 (the second JS exception).
- Docs updated (T22), and the reviewer has approved.
