# Encryption

This document describes the server-side encryption core in `SharpVault.Infrastructure.Encryption`, with the interfaces in `SharpVault.Application.Common` and the wrapped-key types in `SharpVault.Domain.Encryption`.

**SharpVault is not zero-knowledge.** The server encrypts and decrypts secrets. Admins must be able to transfer secrets when someone leaves, so the application, through the app identity and the Key Vault key, can decrypt them. Anyone who controls the Azure tenant, the Key Vault or the storage account can read the data. Protect those accordingly.

## Status

Built and tested in `feature/encryption-core`:

- AES-256-GCM envelope encryption, version 1, with purpose, item id and owner id bound as associated data.
- The session API (`IEnvelopeEncryptor`, `IDataKeySession`), including `ReEncrypt` and `RewrapDataKeyAsync`.
- Two key wrap providers behind `IKeyWrapProvider`: Azure Key Vault and a development provider that only runs in the Development environment.
- Startup guards, configuration validation and the AppHost parameter for development key material.

Not built yet:

- No entity stores a secret. The first store is `feature/vault-and-folders`.
- No rewrap maintenance job. `RewrapDataKeyAsync` exists, but nothing calls it yet.
- No Key Vault or key provisioning. The Key Vault provider is code only and is tested with fakes.
- No one-time link encryption. That happens in the browser (see [One-time links](#one-time-links)).

## Key hierarchy

| Key | Where it lives | Notes |
|---|---|---|
| Key-encryption key (KEK) | Azure Key Vault (RSA key, RSA-OAEP-256 for wrap and unwrap only). For the development provider, derived from `Encryption:DevelopmentKeyMaterial`. | Never leaves Key Vault in production. |
| Data key | 32 random bytes, created per `CreateDataKeyAsync`. | Lives in memory only while a session is open. Never stored unwrapped. |
| Wrapped data key | Stored with the item (`WrappedDataKey`: key id, algorithm, bytes). | The key version is part of the key id. |

Each data key encrypts the values that belong to one item. A session holds one unwrapped data key for one request or one job step.

## Envelope format, version 1

Each encrypted value is one envelope:

```
offset 0            version      1 byte    0x01
offset 1..12        nonce       12 bytes   random, new for every Encrypt and ReEncrypt
offset 13..          ciphertext   n bytes   AES-256-GCM of the UTF-8 plaintext (n = plaintext bytes)
last 16 bytes       tag         16 bytes   GCM authentication tag
```

- Overhead is 29 bytes. The maximum plaintext is 32,768 UTF-8 bytes, so the maximum envelope is 32,797 bytes. That fits one Table Storage binary property (64 KiB).
- Version byte `0x01` is the only version accepted today. Values `0x02` to `0x7F` are reserved for future server versions.
- Values `0x80` to `0xFF` are reserved for browser-side formats (one-time links). The server rejects them as `UnsupportedVersion`.
- Text is encoded with strict UTF-8, with no normalisation. Precomposed and decomposed forms stay byte-exact. Lone surrogates are rejected with `ArgumentException`.

## Associated data

The associated data is built in a stack buffer and is never stored. It binds each ciphertext to its context:

```
byte    version                      0x01
u16 BE  purpose length, then purpose as ASCII (1 to 64 characters)
u16 BE  16, then item id, 16 bytes, big-endian
u16 BE  16, then owner id, 16 bytes, big-endian
```

Golden vector for purpose `secret.value`, item `00112233-4455-6677-8899-aabbccddeeff` and owner `ffeeddcc-bbaa-9988-7766-554433221100`:

```
01 000c 7365637265742e76616c7565 0010 00112233445566778899aabbccddeeff 0010 ffeeddccbbaa99887766554433221100
```

Purposes, item ids and owner ids are part of the persisted format. Changing any of them, or the length prefixes, makes stored data undecryptable.

### Purposes

- 1 to 64 characters from `a` to `z`, `0` to `9`, `.` and `-`, starting with a letter.
- Each field gets its own purpose label, so a notes value cannot be moved into a password field. The labels are defined by the feature that stores the field. The suggested labels for `feature/vault-and-folders` are `secret.name`, `secret.username`, `secret.value`, `secret.notes`, `secret.url`, `secret.tags` and `folder.name`.
- Item ids and owner ids must not be empty.

## Session API

The handler-facing interfaces are:

- `IEnvelopeEncryptor.CreateDataKeyAsync(ct)`: creates a new random data key, wraps it once and returns a session that owns it.
- `IEnvelopeEncryptor.OpenDataKeyAsync(wrappedKey, ct)`: unwraps a stored key once and returns a session.
- `IEnvelopeEncryptor.RewrapDataKeyAsync(wrappedKey, ct)`: unwraps, wraps again with the current key version and returns the new `WrappedDataKey`. The data key is zeroed.
- `IDataKeySession.Encrypt(context, plaintext)`: returns an envelope.
- `IDataKeySession.Decrypt(context, envelope)`: returns the plaintext string. It is the only way to decrypt.
- `IDataKeySession.ReEncrypt(source, target, envelope)`: decrypts with the source context and encrypts with the target context, under the same data key. It never creates a string. It is used by approved transfers (TRF-7).
- `IDataKeySession.WrappedKey`: the wrapped key to store next to the envelopes.

Rules:

- Use a session in a `using` block inside one request or one job step. Handlers keep `WrappedKey` and the envelopes, never the data key.
- A session is not thread-safe. Providers and the encryptor are safe to use as singletons.
- After `Dispose`, `Encrypt`, `Decrypt` and `ReEncrypt` throw `ObjectDisposedException`. `Dispose` can be called twice.
- The core never re-encrypts on its own. `ReEncrypt` runs only when a caller asks for it.
- Nothing is decrypted implicitly. Reveal handlers call `Decrypt` and are the place to audit reveals (SEC-3).

Key Vault call counts per operation:

| Operation | Key Vault calls |
|---|---|
| `CreateDataKeyAsync` | When the app identity can read the key: one key read per process, then 0 `wrapKey` calls (the SDK wraps locally). Otherwise 1 `wrapKey` per call. |
| `OpenDataKeyAsync` | Exactly 1 `unwrapKey`, plus one key read the first time each key version is used in the process. |
| `RewrapDataKeyAsync` | 1 `unwrapKey` and 1 wrap (local or remote). |
| `Encrypt`, `Decrypt`, `ReEncrypt`, `Dispose` | None. |

## Wrapped keys and storage

A `WrappedDataKey` has three parts. Later entities store them as three properties:

| Part | Table property | Notes |
|---|---|---|
| `KeyId` | `DataKeyId` (string, at most 512 characters) | Key Vault: the full versioned key id returned by Key Vault. Development: `development:` plus 16 lowercase hex characters. |
| `Algorithm` | `DataKeyAlgorithm` (string) | `RSA-OAEP-256` or `development-A256GCM`. |
| `Value` | `WrappedDataKey` (binary, at most 512 bytes) | Key Vault: 256, 384 or 512 bytes (RSA 2048, 3072 or 4096). Development: exactly 60 bytes. |

Each envelope is one binary property. No table exists for secrets yet, so nothing is stored by this feature.

## Key wrap providers

`IKeyWrapProvider` is an Infrastructure detail. Only `EnvelopeEncryptor` uses it. Handlers never wrap or unwrap directly.

### Azure Key Vault (`KeyVaultKeyWrapProvider`)

- Algorithm: RSA-OAEP-256 on the wire and in storage. A stored algorithm other than `RSA-OAEP-256` is rejected before any Key Vault call.
- The configured key id is versionless (`https://{vault}/keys/{name}`). Wraps use a client for it. The result is the versioned key id that Key Vault returns, and the wrap result must be a version of the configured key with an RSA-sized value.
- Unwraps use a client for the stored versioned key id. The stored id must be a version of the configured vault and key name, with the same host and port, under `/keys/`, with no query, fragment or user information, and at most 512 characters. Otherwise the provider throws `KeyMismatch` without calling Key Vault.
- Unwrap returns exactly the Key Vault result. A 400 response means `AuthenticationFailed`. Any other failure propagates as `RequestFailedException`, and its message contains no request body.
- Clients are created lazily, so constructing the provider and starting the API make no Key Vault call. At most 64 clients are cached. Beyond that, new clients are created per call and not cached.
- Clients use `DefaultAzureCredential`, a 10 second network timeout, 3 retries, SDK content logging off and challenge resource verification on.

### Development (`DevelopmentKeyWrapProvider`)

- Only for local development. It refuses to run outside the Development environment (see below).
- The KEK is 32 bytes derived with HKDF-SHA256 from `Encryption:DevelopmentKeyMaterial` (trimmed, UTF-8). The key id is `development:` plus 16 hex characters, derived with a separate HKDF label, so changed key material shows as `KeyMismatch`.
- Wrap: AES-256-GCM with a random 12-byte nonce, the key id as associated data. The result is nonce, 32-byte ciphertext and 16-byte tag (60 bytes).
- Unwrap checks the algorithm and key id first, then the length, then the GCM tag.
- The KEK is zeroed on `Dispose`. A disposed provider refuses to wrap or unwrap.

### Refusal layers for the development provider

The development provider is refused outside Development at four layers:

1. `AddEncryption` runs `EncryptionSettingsGuard` while the host is configured. A forbidden configuration stops startup before any service is built.
2. `EncryptionOptionsValidator` runs with `ValidateOnStart`, so an invalid configuration fails at startup.
3. The `DevelopmentKeyWrapProvider` constructor checks the environment. `KeyWrapProviderStartupCheck` takes an `IKeyWrapProvider` in its constructor, which forces construction at host start.
4. An architecture test (`ProductionAssemblies_KeyWrapProviders_AreOnlyKeyVaultAndDevelopment`) allows only two `IKeyWrapProvider` implementations in the production assemblies.

When the development provider runs, the API logs a Warning at startup. The log never contains the key material.

## Configuration

| Key | Environment variable | Meaning |
|---|---|---|
| `Encryption:Provider` | `Encryption__Provider` | Optional. `KeyVault` or `Development`. Default: `Development` in the Development environment, `KeyVault` everywhere else. Numbers are rejected unless they name a defined value. |
| `Encryption:KeyVaultKeyId` | `Encryption__KeyVaultKeyId` | Required for `KeyVault`. A versionless https Key Vault key id, for example `https://contoso.vault.azure.net/keys/sharpvault-kek`. Set it as an app setting or environment variable, never in `appsettings*.json`. |
| `Encryption:DevelopmentKeyMaterial` | `Encryption__DevelopmentKeyMaterial` | Required for `Development`, at least 32 characters after trimming. Only allowed in Development. The AppHost generates it (see [Local development](#local-development)). |

Startup rules, checked in this order:

1. An undefined provider value is refused in every environment.
2. `Development` as the provider is refused outside Development.
3. `DevelopmentKeyMaterial` is refused outside Development. Error messages never echo the configured value.
4. `KeyVault` needs a valid versionless key id.
5. `Development` needs at least 32 characters of key material.

## Key Vault setup

1. Create an RSA key in the vault. Use RSA 3072 or larger, and an HSM-backed key in production.
2. Give the app identity (its managed identity in Azure) the built-in **Key Vault Crypto User** role, scoped to the vault or the key. The role allows `wrapKey` and `unwrapKey`, and it includes key read. With key read, the SDK wraps locally after the first read, so wrapping makes no remote call. Without key read, each wrap costs one remote `wrapKey`.
3. Set `Encryption__KeyVaultKeyId` to the **versionless** key id.
4. Use `Encryption__Provider=KeyVault` outside Development, or leave it unset.

Provisioning the vault, the key and the role assignment belongs to the deployment feature. This repository does not create them.

The application uses no account keys or SAS tokens for Key Vault. Local runs use `DefaultAzureCredential`, which picks up `az login`.

### Rotation (D8)

1. Create a new version of the key in Key Vault. Keep the old versions enabled.
2. Restart the API. A new version is used for new wraps after the restart, because the SDK caches the public key per process.
3. Old wrapped keys keep unwrapping with the version recorded in their key id.
4. Never disable or delete an old version while any wrapped key references it. The data becomes unreadable, and the failure surfaces as a `RequestFailedException` with status 403 or 404.
5. `RewrapDataKeyAsync` moves a wrapped key to the newest version. The rewrap maintenance job is not built yet. Until it exists, do not retire old versions.

## Local development

The AppHost, in run mode only, adds a secret parameter `development-key-material` with 64 generated characters (no special characters), and passes it to the API as `Encryption__DevelopmentKeyMaterial`. The value is persisted in the AppHost user secrets under `Parameters:development-key-material`, so it stays the same across runs. Persistent Azurite data stays readable.

- Publish mode declares no parameter.
- The web app receives no encryption settings.

Reset the key material only together with the local data:

```bash
dotnet user-secrets remove Parameters:development-key-material --project src/SharpVault.AppHost
```

Then wipe the Azurite volume (see [LOCAL-DEV.md](LOCAL-DEV.md#wipe-local-data)). Data encrypted with the old material cannot be decrypted with a new one.

To run the API against a real vault locally:

```bash
Encryption__Provider=KeyVault
Encryption__KeyVaultKeyId=https://<vault>.vault.azure.net/keys/<key>
az login
```

Your Azure identity needs the Key Vault Crypto User role on that key. This is a manual smoke test. It is not automated.

## Logging and errors

- Decryption failures log a Warning with the item id, the purpose and the reason. The log has no plaintext, no envelope bytes, no owner id and no key material.
- A failed data key open logs a Warning with the reason.
- Exceptions are `SecretDecryptionException`, with a `Reason` (`MalformedEnvelope`, `UnsupportedVersion`, `AuthenticationFailed`, `KeyMismatch` or `MalformedWrappedKey`), a constant message and no inner exception. Handlers must map every reason to the same generic response.
- Argument errors throw `ArgumentException` with constant messages. Guard messages never echo configuration values.
- Azure SDK logging keeps its defaults. Content logging is off, and the `Authorization` header is not logged.
- Audit entries for reveals, shares and transfers are written by the feature handlers, not by this core (SEC-3, SEC-5, TRF-7).

## One-time links

One-time link contents are encrypted in the browser with a key that lives in the URL fragment. The browser uses Web Crypto AES-GCM, with a 12-byte IV and the ciphertext followed by the tag, which has the same shape as section 4.3 of the plan. The server stores that blob as opaque bytes and checks only its size.

The server core never receives a link key and never decrypts link payloads. It rejects version bytes `0x80` to `0xFF`. JavaScript interop with Web Crypto is limited to this one case (OTL-3).

## Residual risks

- **Strings and SDK buffers cannot be zeroed.** .NET strings (inputs and decrypted results) cannot be cleared. The Key Vault SDK's JSON request and response buffers hold base64 copies of wrapped and unwrapped keys. Keys from the SDK are not pinned, and the session takes ownership of the returned array without copying it. Data keys that this code allocates are pinned and zeroed. This is accepted (D18).
- **Rollback of one row cannot be detected by AES-GCM alone.** An attacker with write access to storage can restore an older copy of the same row, and the envelope still authenticates. Each history entry uses its own item id, so a history envelope cannot be swapped into the current value (SEC-5, D24). Rollback of a whole row is covered by storage access control (managed identity only) and by the audit log. A version counter in the entity can be added if it matters (D24).
- **Key material in the AppHost user secrets is plain text on the developer's machine.** It is development only, and the provider refuses to run outside Development.
- **The server can decrypt everything it can unwrap.** That is the design (not zero-knowledge). Access to the Key Vault key is access to all data under it.

## Open decision: data key per field (D5 and D5a)

This branch does not decide which fields share a data key. The recommendation for `feature/vault-and-folders`:

- Names, tags and folder names under one per-user metadata data key, so listing a page of secrets needs one unwrap per request.
- Username, value, notes and URL under a per-secret data key, so each reveal needs one unwrap.

The problem (D5a): `ReEncrypt` works only inside one session, so source and target share one data key. Moving metadata fields from the source user's metadata key to the target user's key during a transfer (TRF-7) cannot be done with this API. Either the API needs a cross-session re-encrypt, or the fields that move in a transfer must stay under per-secret data keys.

`feature/vault-and-folders` or `feature/offboarding-transfers`, whichever comes first, must decide and record the choice in its plan.

## Testing

- `RecordingKeyWrapProvider` (unit tests): an in-memory provider that records the arrays it receives and returns. It proves zeroing and call counts.
- `FakeKeyVault` (unit tests): a `CryptographyClient` subclass with real in-memory RSA-OAEP-256 per key version. It covers call counts, rotation and zeroing.
- `FakeKeyVaultHttpHandler` (unit tests): an `HttpMessageHandler` behind the real Key Vault SDK through `HttpClientTransport`. It covers challenge authentication, local versus remote wraps, the wire algorithm, host pinning and SDK logs. Log checks use `KeyMaterialLeakAssert`.
- No live Azure in CI. Every Key Vault test uses the fakes.
- `EncryptionPerformanceTests` (`Category=Performance`, run in CI on pushes to `main`) check the budgets in the plan (for example 50 microseconds average for 1 KiB encryption and a 100,000-nonce uniqueness check).
- Benchmarks: `tests/SharpVault.Benchmarks` uses BenchmarkDotNet on the development provider. Run it with:

  ```bash
  dotnet run -c Release --project tests/SharpVault.Benchmarks -- --filter '*' --job short
  ```

  CI runs `--job dry` as a smoke test on pushes to `main`. The benchmark run fails only when a benchmark fails validation. It does not compare results.
- Coverage gate: `Application` and `Infrastructure` at 80%, and `Domain.Encryption` and `Infrastructure.Encryption` at 95%. See [TESTING.md](TESTING.md) and [LOCAL-DEV.md](LOCAL-DEV.md#coverage).
