# Plan: encryption core

- **Branch**: `feature/encryption-core` (already created, branched from the unmerged `feature/solution-foundation`)
- **PROGRESS.md item**: Next, item 2
- **Planner**: before writing this plan, every API, package version, analyzer outcome and test technique below was checked in a throwaway copy of this branch on .NET SDK 10.0.114 (Linux, Docker running). That copy builds with 0 warnings, all 207 existing unit tests and 394 existing integration tests pass with the changes below, and the prototype tests for the core passed. Where this plan says "verified", the implementer can rely on it. Foundation conventions still apply: CA1711 rejects type names ending in `Collection` (use `*TestGroup`), CA1707 is off under `tests/`, CA1848 requires `[LoggerMessage]`, xUnit1051 requires `TestContext.Current.CancellationToken`, and every `.cs` file starts with the AGPL header.

## 1. Goal

Build the server-side envelope encryption that every later feature uses to store secrets:

- AES-256-GCM encryption of individual values under a random 256-bit data key, with a versioned binary envelope and associated data that binds each ciphertext to its purpose (field), item id and owner id.
- Data keys wrapped by a key-encryption key behind `IKeyWrapProvider`, with two implementations:
  - Azure Key Vault (`CryptographyClient`, `DefaultAzureCredential`, RSA-OAEP-256)
  - a development provider that refuses to run outside the Development environment
- A session API, so one unwrap can serve many values for the length of one request, and the data key is zeroed when the session ends.
- Re-encryption for a new owner (TRF-7), and data key re-wrapping for key-encryption key rotation.
- Configuration with fail-fast startup guards, AppHost wiring for the development key material, and unit, integration, security and performance tests.
- `tests/SharpVault.Benchmarks`, which the foundation plan deferred to this item, with BenchmarkDotNet benchmarks for the hot paths.

### Out of scope

- Any entity, table, store, request, handler or endpoint. No secret is stored yet (`feature/vault-and-folders`).
- Choosing which secret fields use which data key. Section 10 (D5) records a recommendation for `feature/vault-and-folders`.
- One-time link encryption. That happens in the browser (OTL-3); this plan only fixes what the server core must never do for links (D25).
- Audit entries for reveals and transfers (SEC-3, TRF-7). The handlers that reveal or transfer write them.
- A rewrap maintenance job that migrates wrapped keys to the newest Key Vault key version. This plan provides `RewrapDataKeyAsync`; the job is added to PROGRESS.md (D8).
- Provisioning Key Vault, keys and role assignments (deployment feature).

## 2. Requirements

| Id | What this work delivers |
|---|---|
| CLAUDE.md Encryption | AES-256-GCM, a random data key per `CreateDataKeyAsync`, a unique 96-bit nonce per encryption, item id and owner id bound as associated data, data keys wrapped by a key-encryption key in Azure Key Vault behind `IKeyWrapProvider`, a development provider that refuses to run outside Development, and no secret values, data keys or decrypted content in logs |
| SEC-2 | The mechanism to encrypt values, notes, usernames, names, tags and folder names. Each field gets its own purpose label in the associated data. The vault feature applies it to the fields |
| SEC-3 | Decryption only through an explicit `OpenDataKeyAsync` plus `Decrypt`. Nothing is decrypted implicitly, so reveal handlers stay the single place to audit |
| SEC-5 | Every history version is its own envelope. D24 requires a distinct item id per history entry, so versions cannot be swapped |
| TRF-6 | The core never re-encrypts on its own. `ReEncrypt` is only called explicitly, by the transfer job after approval |
| TRF-7 | `IDataKeySession.ReEncrypt(source, target, envelope)` re-encrypts for a new owner without ever creating a plaintext string, at 1 unwrap and 0 wraps per data key |
| OTL-3 | Server and browser formats are kept apart (envelope versions `0x80`-`0xFF` are reserved for browser formats and rejected by the server). The server core never receives a link key (D25) |
| AUD-3 | Log entries and exception messages never contain secret values, keys or envelopes. This is proven by tests |

Pull requests and tests reference these ids.

## 3. Verified versions

All are stable on nuget.org on 2026-10-31. They restored and built here, and `dotnet list package --vulnerable --include-transitive` reports nothing.

| Package | Version | Used by |
|---|---|---|
| `Azure.Security.KeyVault.Keys` | 4.10.2 (needs Azure.Core 1.54.0 or later; 1.62.0 resolves today) | Infrastructure |
| `BenchmarkDotNet` | 0.15.8 | Benchmarks |

No other package is added. `Azure.Identity` 1.21.0, `Microsoft.Extensions.Options.ConfigurationExtensions` and `Microsoft.Extensions.Configuration.Binder` already reach Infrastructure. AES-GCM, HKDF, RSA (in tests) and `RandomNumberGenerator` come from the BCL.

## 4. Design

### 4.1 Types and where they live

| Layer | Namespace | Types |
|---|---|---|
| Domain | `SharpVault.Domain.Encryption` | `WrappedDataKey` |
| Application | `SharpVault.Application.Common.Encryption` | `EncryptionContext` |
| Application | `SharpVault.Application.Common.Exceptions` | `DecryptionFailureReason`, `SecretDecryptionException` |
| Application | `SharpVault.Application.Common.Interfaces` | `IEnvelopeEncryptor`, `IDataKeySession` |
| Infrastructure | `SharpVault.Infrastructure.Encryption` | `EnvelopeFormat`, `EnvelopeAssociatedData`, `EncryptionMessages` (internal), `IKeyWrapProvider`, `DataKeySession`, `EnvelopeEncryptor`, `KeyWrapProviderKind`, `EncryptionOptions`, `EncryptionSettingsGuard`, `EncryptionOptionsValidator`, `KeyWrapProviderStartupCheck`, `EncryptionRegistration` |
| Infrastructure | `SharpVault.Infrastructure.Encryption.KeyVault` | `KeyVaultKeyIds`, `IKeyVaultCryptographyClientFactory`, `KeyVaultCryptographyClientFactory`, `KeyVaultKeyWrapProvider` |
| Infrastructure | `SharpVault.Infrastructure.Encryption.Development` | `DevelopmentKeyWrapProvider` |

- The Domain layer only ever holds encrypted forms. Plaintext types never enter it.
- Handlers depend only on `IEnvelopeEncryptor` and `IDataKeySession`.
- `IKeyWrapProvider` is an Infrastructure detail (D14).

### 4.2 Handler-facing API

```csharp
public interface IEnvelopeEncryptor
{
    Task<IDataKeySession> CreateDataKeyAsync(CancellationToken cancellationToken);
    Task<IDataKeySession> OpenDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken);
    Task<WrappedDataKey> RewrapDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken);
}

public interface IDataKeySession : IDisposable
{
    WrappedDataKey WrappedKey { get; }
    byte[] Encrypt(EncryptionContext context, string plaintext);
    string Decrypt(EncryptionContext context, byte[] envelope);
    byte[] ReEncrypt(EncryptionContext source, EncryptionContext target, byte[] envelope);
}

public sealed record EncryptionContext(string Purpose, Guid ItemId, Guid OwnerId);
public sealed record WrappedDataKey(string KeyId, string Algorithm, byte[] Value);
```

- `CreateDataKeyAsync` generates a fresh random data key, wraps it once and returns a session that owns it.
- `OpenDataKeyAsync` unwraps a stored key once.
- `Dispose` zeroes the data key.
- A session is used in a `using` block inside one request or one job step, and is not thread-safe.
- Handlers keep `session.WrappedKey` and the envelopes, never the data key.

### 4.3 Envelope format, version 1 (D2)

```
offset 0              version              1 byte   0x01
offset 1..12          nonce               12 bytes  RandomNumberGenerator, new for every Encrypt and ReEncrypt
offset 13..13+n-1     ciphertext           n bytes  AES-256-GCM of the UTF-8 plaintext (n = UTF-8 byte count)
last 16 bytes         tag                 16 bytes
```

- Overhead is 29 bytes. The maximum plaintext is 32,768 UTF-8 bytes, so the maximum envelope is 32,797 bytes. That fits one Table Storage binary property (64 KiB).
- Version byte values `0x01`-`0x7F` are reserved for server envelope versions, and only `0x01` is accepted today.
- `0x80`-`0xFF` are reserved for browser-side formats (one-time links). The server always rejects them.
- Verified: an envelope built independently with raw `AesGcm` and this layout decrypts through the session. Encryption runs in place inside the envelope array, which is verified to work.

### 4.4 Associated data (D3)

Built in a `stackalloc` buffer, never stored:

```
byte    version (the envelope version byte, 0x01)
u16 BE  purpose length, then purpose as ASCII bytes (1 to 64 characters, [a-z][a-z0-9.-]*)
u16 BE  16, then ItemId as 16 bytes, big-endian (Guid.TryWriteBytes(span, bigEndian: true, out _))
u16 BE  16, then OwnerId as 16 bytes, big-endian
```

- The maximum length is 103 bytes.
- Golden vector (verified): purpose `secret.value`, item `00112233-4455-6677-8899-aabbccddeeff` and owner `ffeeddcc-bbaa-9988-7766-554433221100` encode as hex `01000c7365637265742e76616c75650010` `00112233445566778899aabbccddeeff` `0010` `ffeeddccbbaa99887766554433221100`.
- Purposes, item ids and owner ids are part of the persisted format. Changing any of them makes stored data undecryptable.

### 4.5 Key hierarchy, wrapped keys and storage representation

- **Key-encryption key (KEK)**: one RSA key in Key Vault (RSA 3072 or more recommended, HSM-backed in production), used with RSA-OAEP-256 for wrap and unwrap only.
- **Data key**: 32 random bytes per `CreateDataKeyAsync`. It never leaves the process unwrapped.
- **`WrappedDataKey`**:
  - `KeyId` is the full versioned Key Vault key id returned by Key Vault, for example `https://contoso.vault.azure.net/keys/sharpvault-kek/0f1e...`. For the development provider it is `development:` plus 16 hex characters.
  - `Algorithm` is `RSA-OAEP-256`, or `development-A256GCM` for the development provider.
  - `Value` is the wrapped bytes: 256, 384 or 512 for RSA 2048, 3072 or 4096, and exactly 60 for the development provider.
- Later features store a wrapped key as three Table Storage properties: `DataKeyId` (string, at most 512 characters), `DataKeyAlgorithm` (string) and `WrappedDataKey` (binary, at most 512 bytes). Each envelope is one binary property. The key version is part of `DataKeyId`, so no separate version column is needed (D6).

### 4.6 Key Vault provider behaviour (verified with the real SDK over an in-process fake transport)

- **Configuration**: `Encryption:KeyVaultKeyId` must be a **versionless** key id (`https://{vault}/keys/{name}`). The provider wraps with a client for that id. Unwrap uses a cached client per **versioned** stored id.
- **Wrap**: when the app identity may read the key (Key Vault Crypto User can), the SDK reads the public key once per client and then wraps locally, with 0 remote wraps. Otherwise it sends 1 `wrapKey` per wrap. Either way the result carries the versioned key id.
- **Unwrap**: always exactly 1 remote `unwrapKey`, plus 1 key read the first time each client is used.
- **Rotation**: a new key version is used for new wraps after the API restarts. Old wrapped keys keep unwrapping with their recorded version (D8).
- **Logging**: with SDK logging at Verbose and content logging off, no request or response body and no bearer token appears in the logs.
- **`KeyVaultKeyIdentifier.TryCreate` gap**: it accepts `/secrets/` and `/certificates/` URIs and parses `//keys/k` as name `keys`, version `k` (verified). `KeyVaultKeyIds` therefore also requires the path to start with `/keys/`.

### 4.7 Configuration

| Key | Meaning |
|---|---|
| `Encryption:Provider` | Optional: `KeyVault` or `Development`. Default: `Development` in the Development environment, `KeyVault` everywhere else |
| `Encryption:KeyVaultKeyId` | Versionless https Key Vault key id. Required when the provider is `KeyVault`. Set as an App Service setting or environment variable `Encryption__KeyVaultKeyId`, never in `appsettings*.json` |
| `Encryption:DevelopmentKeyMaterial` | At least 32 characters (after trimming). Only allowed in Development. The AppHost generates it (D12) |

## 5. Tasks

Ownership:

- `implementer`: T1 to T8. Each implementer runs `dotnet build SharpVault.slnx`, which must report `0 Warning(s)` and `0 Error(s)`. They also run `dotnet test SharpVault.slnx --filter "Category!=Performance"`, which must stay green, apart from the AppHost source tests that T7 knowingly breaks until T13 lands.
- `test-writer`: T9 to T14.
- `docs-writer`: T15.

```
T1 (alone, first)
 ├─ T2 → T3 → (T4 ∥ T5) → T6
 ├─ T7 (parallel with T2..T6)  → T13 directly after T7
 ├─ T8 (parallel with T2..T6)
 ├─ T9 (after T3) ─┬─ T10 (after T4 and T9)
 │                 └─ T11 (after T5 and T9)      T10 ∥ T11
 ├─ T12 (after T6 and T9)   T14 (after T6 and T9)   T12 ∥ T14
 └─ T15 (after all)
```

---

### T1. Packages, test build props and the Benchmarks project skeleton (sequential, first)

1. `Directory.Packages.props`: add `<PackageVersion Include="Azure.Security.KeyVault.Keys" Version="4.10.2" />` and `<PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />`, keeping alphabetical order (after `Azure.Identity` and before `coverlet.collector`).
2. `src/SharpVault.Infrastructure/SharpVault.Infrastructure.csproj`: add `<PackageReference Include="Azure.Security.KeyVault.Keys" />` after `Azure.Identity`.
3. `tests/Directory.Build.props`: add `Condition="'$(IsTestProject)' == 'true'"` to the `ItemGroup` that holds the four test packages. The `PropertyGroup` is unchanged. Verified: MSBuild evaluates item conditions after all properties, so a project that sets `IsTestProject` to false gets no xUnit packages (the xUnit v3 generated entry point would otherwise clash with BenchmarkDotNet's).
4. `tests/SharpVault.Benchmarks/SharpVault.Benchmarks.csproj`:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <IsTestProject>false</IsTestProject>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="BenchmarkDotNet" />
     </ItemGroup>
     <ItemGroup>
       <ProjectReference Include="../../src/SharpVault.Infrastructure/SharpVault.Infrastructure.csproj" />
     </ItemGroup>
   </Project>
   ```

5. `tests/SharpVault.Benchmarks/Program.cs`: header, `using BenchmarkDotNet.Running;`, the constants `const int SuccessExitCode = 0;` and `const int FailureExitCode = 1;`, then `var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);` and `return summaries.Any(summary => summary.HasCriticalValidationErrors || summary.Reports.Any(report => !report.Success)) ? FailureExitCode : SuccessExitCode;`. Without the exit code, BenchmarkDotNet returns 0 even when a benchmark fails validation or throws, so the T8 smoke run could never fail.
6. `dotnet sln SharpVault.slnx add --solution-folder tests tests/SharpVault.Benchmarks/SharpVault.Benchmarks.csproj`. Verified: it lands in `/tests/`, and `dotnet test SharpVault.slnx` skips it.
7. `tests/coverage.runsettings`: `Exclude` becomes `[SharpVault.*Tests]*,[SharpVault.AppHost]*,[SharpVault.Benchmarks]*`.

**Acceptance**

- The build is clean.
- `dotnet test SharpVault.slnx` passes as before.
- `dotnet list SharpVault.slnx package --vulnerable --include-transitive` reports no vulnerable package for any project, including `SharpVault.Benchmarks`.

---

### T2. Domain and Application contracts (after T1)

Files and exact members:

1. `src/SharpVault.Domain/Encryption/WrappedDataKey.cs`
   - `public sealed record WrappedDataKey(string KeyId, string Algorithm, byte[] Value)`
   - `public override string ToString() => $"WrappedDataKey {{ KeyId = {KeyId}, Algorithm = {Algorithm}, Length = {Value?.Length ?? 0} }}";`
   - Verified: CA1819 does not fire on the `byte[]` record property.
2. `src/SharpVault.Application/Common/Encryption/EncryptionContext.cs`: `public sealed record EncryptionContext(string Purpose, Guid ItemId, Guid OwnerId);`
3. `src/SharpVault.Application/Common/Exceptions/DecryptionFailureReason.cs`: `public enum DecryptionFailureReason { MalformedEnvelope, UnsupportedVersion, AuthenticationFailed, KeyMismatch, MalformedWrappedKey }`
4. `src/SharpVault.Application/Common/Exceptions/SecretDecryptionException.cs`: `public sealed class SecretDecryptionException : Exception` with:
   - `public const string DefaultMessage = "The encrypted value could not be decrypted.";`
   - one constructor `public SecretDecryptionException(DecryptionFailureReason reason) : base(DefaultMessage) { Reason = reason; }`
   - `public DecryptionFailureReason Reason { get; }`

   No inner exception, no data. Verified: CA1032 does not fire, so the standard constructors are not added.
5. `src/SharpVault.Application/Common/Interfaces/IEnvelopeEncryptor.cs` and `IDataKeySession.cs` exactly as in section 4.2. Add `///` XML docs:
   - `IDataKeySession`: "Owns one unwrapped data key. Not thread-safe. Dispose zeroes the key. Encrypt and ReEncrypt use a new random nonce every call."
   - `Decrypt`: "Throws SecretDecryptionException when the envelope is malformed, has an unsupported version, or fails authentication for the given context."
   - `ReEncrypt`: "Decrypts with the source context and encrypts with the target context under the same data key, without creating a string. Used by approved transfers (TRF-7)."

**Acceptance**: the build is clean. The existing `LayeringTests` still pass (Domain has no references; Application has no Azure or ASP.NET Core references).

---

### T3. Infrastructure core, options and guard (after T2)

All files are under `src/SharpVault.Infrastructure/Encryption/`. Every class is `sealed` or `static`.

1. `EnvelopeFormat.cs`: `public static class EnvelopeFormat` with these constants:
   - `Version1 = 0x01` (byte)
   - `VersionSize = 1`, `NonceSize = 12`, `TagSize = 16`
   - `HeaderSize = VersionSize + NonceSize`
   - `Overhead = HeaderSize + TagSize`
   - `DataKeySize = 32`
   - `MaxPlaintextBytes = 32_768`
   - `MaxEnvelopeBytes = MaxPlaintextBytes + Overhead`
2. `EncryptionMessages.cs`: `internal static class`. These `const string` values are exact, because tests copy them. None of them contains a configured value.
   - `InvalidPurpose` = `"The encryption purpose must be 1 to 64 lowercase letters, digits, dots or hyphens and start with a letter."`
   - `EmptyContextId` = `"The encryption context needs a non-empty item id and owner id."`
   - `PlaintextTooLarge` = `"The value is too large to encrypt."`
   - `PlaintextNotValidUnicode` = `"The value is not valid Unicode text."`
   - `InvalidDataKeyLength` = `"Data keys must be 32 bytes."`
   - `UnknownProvider` = `"Encryption:Provider must be KeyVault or Development."`
   - `DevelopmentProviderNotAllowed` = `"The development key wrap provider can only run in the Development environment."`
   - `DevelopmentKeyMaterialMissing` = `"The development key wrap provider needs Encryption:DevelopmentKeyMaterial with at least 32 characters."`
   - `DevelopmentKeyMaterialNotAllowed` = `"Encryption:DevelopmentKeyMaterial must not be set outside the Development environment."`
   - `KeyVaultKeyIdInvalid` = `"Encryption:KeyVaultKeyId must be an absolute https Key Vault key URI without a version, query string or user information."`
   - `KeyVaultReturnedUnexpectedKey` = `"Key Vault wrapped the data key with an unexpected key."`
3. `EnvelopeAssociatedData.cs` (verified):

   ```csharp
   public static class EnvelopeAssociatedData
   {
       public const int MaxPurposeLength = 64;
       public const int MaxLength = EnvelopeFormat.VersionSize + LengthPrefixSize + MaxPurposeLength + (2 * (LengthPrefixSize + GuidSize));
       private const int LengthPrefixSize = 2;
       private const int GuidSize = 16;
       private static readonly SearchValues<char> PurposeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789.-");

       public static void EnsureValid(EncryptionContext context, string parameterName)
       public static int Write(byte version, EncryptionContext context, Span<byte> destination)
   }
   ```

   - `EnsureValid` does the following:
     1. `ArgumentNullException.ThrowIfNull(context, parameterName)`
     2. Throws `ArgumentException(InvalidPurpose, parameterName)` unless the purpose is not null, is 1 to 64 characters long, its first character is in `a`-`z`, and `!purpose.AsSpan().ContainsAnyExcept(PurposeCharacters)`.
     3. Throws `ArgumentException(EmptyContextId, parameterName)` if `ItemId` or `OwnerId` is `Guid.Empty`.
   - `Write` writes the layout from section 4.4 with `BinaryPrimitives.WriteUInt16BigEndian`, `Encoding.ASCII.GetBytes(purpose, span)` and `Guid.TryWriteBytes(span, bigEndian: true, out _)`, and returns the number of bytes written. Callers run `EnsureValid` first.
4. `IKeyWrapProvider.cs`:

   ```csharp
   public interface IKeyWrapProvider
   {
       Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken);
       Task<byte[]> UnwrapAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken);
   }
   ```

   XML docs:
   - "WrapAsync must not copy or keep `dataKey`; the caller zeroes it."
   - "UnwrapAsync returns a new array that the caller owns and zeroes."
   - "Both throw SecretDecryptionException(KeyMismatch) for a wrapped key that belongs to another provider, key or algorithm."
5. `DataKeySession.cs`: `public sealed partial class DataKeySession : IDataKeySession`, with an **internal** constructor `DataKeySession(byte[] dataKey, WrappedDataKey wrappedKey, ILogger logger)`.
   - Fields:
     - `private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);`
     - `_dataKey` (the array passed in; the session owns it and does not copy it)
     - `_aesGcm = new AesGcm(dataKey, EnvelopeFormat.TagSize)`
     - `_logger`
     - `_disposed`
   - `Encrypt(context, plaintext)`:
     1. `ObjectDisposedException.ThrowIf(_disposed, this)`
     2. `EnvelopeAssociatedData.EnsureValid(context, nameof(context))`
     3. `ArgumentNullException.ThrowIfNull(plaintext)`
     4. `length = StrictUtf8.GetByteCount(plaintext)`. Catch `EncoderFallbackException` and throw `ArgumentException(PlaintextNotValidUnicode, nameof(plaintext))`.
     5. If `length > MaxPlaintextBytes`, throw `ArgumentException(PlaintextTooLarge, nameof(plaintext))`.
     6. `envelope = new byte[Overhead + length]`, `body = envelope.AsSpan(HeaderSize, length)`, `StrictUtf8.GetBytes(plaintext, body)`, then `Seal(context, envelope, body)`, then return the envelope.
   - `Decrypt(context, envelope)`:
     1. Disposed check, `EnsureValid` and `ArgumentNullException.ThrowIfNull(envelope)`.
     2. `length = ReadPlaintextLength(context, envelope)`.
     3. `rented = ArrayPool<byte>.Shared.Rent(length)` and `plaintext = rented.AsSpan(0, length)`.
     4. `try { Open(context, envelope, plaintext); return StrictUtf8.GetString(plaintext); } catch (DecoderFallbackException) { throw Fail(context, MalformedEnvelope); } finally { CryptographicOperations.ZeroMemory(plaintext); ArrayPool<byte>.Shared.Return(rented); }`. An authenticated envelope whose plaintext is not valid UTF-8 (only possible when it was built outside `Encrypt`) must not surface a `DecoderFallbackException`, because its message and `BytesUnknown` carry plaintext bytes (TH17).
   - `ReEncrypt(source, target, envelope)`:
     1. Disposed check, `EnsureValid` on both contexts, and a null check on the envelope.
     2. `length = ReadPlaintextLength(source, envelope)`, `reEncrypted = new byte[Overhead + length]`, `body = reEncrypted.AsSpan(HeaderSize, length)`.
     3. `try { Open(source, envelope, body); Seal(target, reEncrypted, body); return reEncrypted; } catch { CryptographicOperations.ZeroMemory(reEncrypted); throw; }`
   - `Seal(context, envelope, body)`:
     1. `stackalloc byte[EnvelopeAssociatedData.MaxLength]`, then `Write(Version1, ...)`.
     2. `envelope[0] = Version1`.
     3. `nonce = envelope.AsSpan(VersionSize, NonceSize)`, then `RandomNumberGenerator.Fill(nonce)`.
     4. `tag = envelope.AsSpan(envelope.Length - TagSize)`.
     5. `_aesGcm.Encrypt(nonce, body, body, tag, associatedData[..written])` (in place, verified).
   - `ReadPlaintextLength(context, envelope)`:
     1. If the length is below `Overhead` or above `MaxEnvelopeBytes`, `throw Fail(context, MalformedEnvelope)`.
     2. If `envelope[0] != Version1`, `throw Fail(context, UnsupportedVersion)`.
     3. Return `envelope.Length - Overhead`.
   - `Open(context, envelope, plaintext)`: build the associated data with `Version1`, slice the nonce, ciphertext and tag (`envelope[^TagSize..]`), call `_aesGcm.Decrypt(...)`, and catch `AuthenticationTagMismatchException` to `throw Fail(context, AuthenticationFailed)`.
   - `Fail(context, reason)` calls `LogDecryptionFailed(_logger, context.ItemId, context.Purpose, reason)` and returns `new SecretDecryptionException(reason)`.
   - `[LoggerMessage(Level = LogLevel.Warning, Message = "Decryption failed for item {ItemId} with purpose {Purpose}: {Reason}")] private static partial void LogDecryptionFailed(ILogger logger, Guid itemId, string purpose, DecryptionFailureReason reason);`
   - `Dispose()`: if already disposed, return. Otherwise set `_disposed = true`, call `_aesGcm.Dispose()`, then `CryptographicOperations.ZeroMemory(_dataKey)`.
6. `EnvelopeEncryptor.cs`: `public sealed partial class EnvelopeEncryptor(IKeyWrapProvider keyWrapProvider, ILogger<EnvelopeEncryptor> logger) : IEnvelopeEncryptor`.
   - `CreateDataKeyAsync`:
     1. `dataKey = GC.AllocateUninitializedArray<byte>(DataKeySize, pinned: true)`, then `RandomNumberGenerator.Fill(dataKey)`.
     2. `try { var wrapped = await keyWrapProvider.WrapAsync(dataKey, cancellationToken); return new DataKeySession(dataKey, wrapped, logger); } catch { CryptographicOperations.ZeroMemory(dataKey); throw; }`
   - `OpenDataKeyAsync(wrappedKey, ct)`:
     1. Null check.
     2. `dataKey = await UnwrapAsync(wrappedKey, ct)`.
     3. Return `new DataKeySession(dataKey, wrappedKey, logger)`. The session owns the provider's array; do not copy it (D18).
   - `RewrapDataKeyAsync(wrappedKey, ct)`:
     1. Null check.
     2. `dataKey = await UnwrapAsync(...)`.
     3. `try { return await keyWrapProvider.WrapAsync(dataKey, ct); } finally { ZeroMemory(dataKey); }`
   - Private `UnwrapAsync`:
     1. `try { unwrapped = await keyWrapProvider.UnwrapAsync(wrappedKey, ct); } catch (SecretDecryptionException exception) { LogDataKeyOpenFailed(logger, exception.Reason); throw; }`
     2. If `unwrapped.Length != DataKeySize`, zero it, log `MalformedWrappedKey` and throw `SecretDecryptionException(MalformedWrappedKey)`.
     3. Return `unwrapped`.
   - `[LoggerMessage(Level = LogLevel.Warning, Message = "A data key could not be opened: {Reason}")]`.
7. `KeyWrapProviderKind.cs`: `public enum KeyWrapProviderKind { KeyVault, Development }`
8. `EncryptionOptions.cs`: `public sealed class EncryptionOptions` with:
   - `public const string SectionName = "Encryption";`
   - `public KeyWrapProviderKind? Provider { get; set; }`
   - `public string? KeyVaultKeyId { get; set; }`
   - `public string? DevelopmentKeyMaterial { get; set; }`

   It is a class, not a record, so `ToString()` never prints the key material.
9. `KeyVault/KeyVaultKeyIds.cs`: `public static class KeyVaultKeyIds`.
   - Constants: `MaxKeyIdLength = 512`, private `KeysPathPrefix = "/keys/"`.
   - `public static bool TryParseConfigured(string? keyId, [NotNullWhen(true)] out KeyVaultKeyIdentifier? identifier)`: true only if `TryParse` succeeds and `Version` is null.
   - `public static bool IsVersionOf(string? keyId, KeyVaultKeyIdentifier configured)`: true only if all of these hold:
     - `TryParse` succeeds
     - `Version` is not null or empty
     - `Uri.Compare(parsed.VaultUri, configured.VaultUri, UriComponents.SchemeAndServer, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0` (so the port counts)
     - the names are equal (OrdinalIgnoreCase)
   - private `TryParse`: false if the value is null or whitespace, is longer than 512 characters, is not an absolute URI, is not https, has a query, fragment or user info, has an `AbsolutePath` that does not start with `/keys/` (OrdinalIgnoreCase), or fails `KeyVaultKeyIdentifier.TryCreate`.
10. `EncryptionSettingsGuard.cs`: `public static class EncryptionSettingsGuard`.
    - `public const int MinimumDevelopmentKeyMaterialLength = 32;`
    - `public static bool TryResolveProvider(EncryptionOptions options, IHostEnvironment environment, out KeyWrapProviderKind kind, [NotNullWhen(false)] out string? error)` checks these rules in order. The first rule that fails sets `error` to the message named and returns false:
      1. `options.Provider` has a value that `Enum.IsDefined` rejects: `UnknownProvider`. This applies in every environment and catches `"7"`, because the binder accepts numbers.
      2. Not Development and `Provider == Development`: `DevelopmentProviderNotAllowed`.
      3. Not Development and `!string.IsNullOrEmpty(DevelopmentKeyMaterial)`: `DevelopmentKeyMaterialNotAllowed`.
      4. `kind = Provider ?? (IsDevelopment ? Development : KeyVault)`.
      5. `kind == KeyVault` and `!KeyVaultKeyIds.TryParseConfigured(KeyVaultKeyId, out _)`: `KeyVaultKeyIdInvalid`.
      6. `kind == Development` and `(DevelopmentKeyMaterial?.Trim().Length ?? 0) < 32`: `DevelopmentKeyMaterialMissing`.
      7. Otherwise return true.
    - `public static KeyWrapProviderKind ResolveProvider(EncryptionOptions options, IHostEnvironment environment)` calls `ArgumentNullException.ThrowIfNull` on both arguments, then `TryResolveProvider`, and throws `InvalidOperationException(error)` on failure.

**Acceptance**: the build is clean. No code in this task logs or formats a key, plaintext or envelope.

---

### T4. Development key wrap provider (after T3, parallel with T5)

`src/SharpVault.Infrastructure/Encryption/Development/DevelopmentKeyWrapProvider.cs`: `public sealed class DevelopmentKeyWrapProvider : IKeyWrapProvider, IDisposable` (verified).

- Constants:
  - `Algorithm = "development-A256GCM"`, `KeyIdPrefix = "development:"`
  - `WrappedKeyLength = NonceSize + DataKeySize + TagSize` (60)
  - private `KeyIdFingerprintLength = 8`
  - private static `KeyEncryptionKeyInfo = "SharpVault development key encryption key v1"u8.ToArray()`
  - private static `KeyIdInfo = "SharpVault development key id v1"u8.ToArray()`
- Constructor `(IHostEnvironment environment, IOptions<EncryptionOptions> options)`:
  1. Null checks.
  2. `if (!environment.IsDevelopment()) throw new InvalidOperationException(DevelopmentProviderNotAllowed)`. This is the second refusal layer after the guard.
  3. `material = options.Value.DevelopmentKeyMaterial?.Trim()`. If it is null or shorter than 32 characters, throw `InvalidOperationException(DevelopmentKeyMaterialMissing)`.
  4. `inputKeyMaterial = Encoding.UTF8.GetBytes(material)`.
  5. `_keyEncryptionKey = GC.AllocateUninitializedArray<byte>(32, pinned: true)`, then `HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKeyMaterial, _keyEncryptionKey, [], KeyEncryptionKeyInfo)`.
  6. `KeyId = KeyIdPrefix + Convert.ToHexStringLower(HKDF.DeriveKey(SHA256, inputKeyMaterial, 8, [], KeyIdInfo))`.
  7. `_associatedData = Encoding.UTF8.GetBytes(KeyId)`.
  8. In a `finally`, `ZeroMemory(inputKeyMaterial)`.
- `public string KeyId { get; }`
- `private bool _disposed;`
- `WrapAsync(dataKey, ct)`:
  1. `ObjectDisposedException.ThrowIf(_disposed, this)`, then a null check, and `ArgumentException(InvalidDataKeyLength, nameof(dataKey))` unless the key is 32 bytes.
  2. `wrapped = new byte[60]` with a random 12-byte nonce in front.
  3. `using var aesGcm = new AesGcm(_keyEncryptionKey, 16)` (one per call, which makes the provider thread-safe), then `Encrypt(nonce, dataKey, wrapped[12..44], wrapped[44..], _associatedData)`.
  4. Return `Task.FromResult(new WrappedDataKey(KeyId, Algorithm, wrapped))`.
- `UnwrapAsync(wrappedKey, ct)`:
  1. `ObjectDisposedException.ThrowIf(_disposed, this)`, then a null check.
  2. If the algorithm or key id differs from this provider's (Ordinal), throw `SecretDecryptionException(KeyMismatch)`.
  3. If `Value is null` or its length is not 60, throw `MalformedWrappedKey`.
  4. `dataKey = GC.AllocateUninitializedArray<byte>(32, pinned: true)`, then decrypt into it.
  5. Catch `AuthenticationTagMismatchException`: zero `dataKey` and throw `AuthenticationFailed`.
  6. Return `Task.FromResult(dataKey)`.
- `Dispose()`: if already disposed, return. Otherwise set `_disposed = true`, then `CryptographicOperations.ZeroMemory(_keyEncryptionKey)`. A disposed provider never wraps or unwraps with the zeroed KEK.

**Acceptance**: the build is clean. The provider never logs.

---

### T5. Key Vault key wrap provider (after T3, parallel with T4)

Files in `src/SharpVault.Infrastructure/Encryption/KeyVault/`:

1. `IKeyVaultCryptographyClientFactory.cs`: `CryptographyClient GetClient(Uri keyId);`
2. `KeyVaultCryptographyClientFactory.cs`: `public sealed class KeyVaultCryptographyClientFactory(TokenCredential credential, CryptographyClientOptions options) : IKeyVaultCryptographyClientFactory`.
   - Constants: `MaxCachedClients = 64`, `MaxRetries = 3`, and `public static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(10);`
   - `ConcurrentDictionary<string, CryptographyClient> _clients` with `StringComparer.OrdinalIgnoreCase`.
   - `GetClient(keyId)`:
     1. A null check, then `key = keyId.AbsoluteUri`.
     2. Return the cached client if there is one.
     3. Otherwise `client = new CryptographyClient(keyId, credential, options)` and return `_clients.Count < MaxCachedClients ? _clients.GetOrAdd(key, client) : client`. The limit stops a flood of fake versions from growing memory.
   - `public static CryptographyClientOptions CreateOptions()`: `new CryptographyClientOptions()` with `Diagnostics.IsLoggingContentEnabled = false`, `Retry.MaxRetries = MaxRetries` and `Retry.NetworkTimeout = NetworkTimeout`. Leave `DisableChallengeResourceVerification` at its default, `false`.
   - Verified: constructing a `CryptographyClient` makes no network call.
3. `KeyVaultKeyWrapProvider.cs`: `public sealed class KeyVaultKeyWrapProvider : IKeyWrapProvider`.
   - Constants:
     - `public const string Algorithm = "RSA-OAEP-256";`. Verified: `KeyWrapAlgorithm.RsaOaep256.ToString()` equals it.
     - private `Rsa2048WrappedLength = 256`, `Rsa3072WrappedLength = 384`, `Rsa4096WrappedLength = 512`.
   - Constructor `(IKeyVaultCryptographyClientFactory clientFactory, IOptions<EncryptionOptions> options)`:
     1. Null checks.
     2. `if (!KeyVaultKeyIds.TryParseConfigured(options.Value.KeyVaultKeyId, out var configured)) throw new InvalidOperationException(KeyVaultKeyIdInvalid)`.
     3. Store `_configuredKey = configured.Value` and `_wrapClient = clientFactory.GetClient(configured.Value.SourceId)`.
   - `WrapAsync(dataKey, ct)`:
     1. A null check, and an `ArgumentException(InvalidDataKeyLength)` unless the key is 32 bytes. Neither makes a Key Vault call.
     2. `result = await _wrapClient.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dataKey, ct)`. Pass the caller's array as is, with no copy.
     3. Unless `KeyVaultKeyIds.IsVersionOf(result.KeyId, _configuredKey)` and `IsAllowedWrappedLength(result.EncryptedKey)`, throw `InvalidOperationException(KeyVaultReturnedUnexpectedKey)`.
     4. Return `new WrappedDataKey(result.KeyId, Algorithm, result.EncryptedKey)`.
   - `UnwrapAsync(wrappedKey, ct)`:
     1. A null check.
     2. If `!string.Equals(wrappedKey.Algorithm, Algorithm, StringComparison.Ordinal)` or `!IsVersionOf(wrappedKey.KeyId, _configuredKey)`, throw `SecretDecryptionException(KeyMismatch)`.
     3. If `!IsAllowedWrappedLength(wrappedKey.Value)`, throw `MalformedWrappedKey`.
     4. `client = _clientFactory.GetClient(new Uri(wrappedKey.KeyId))`.
     5. `try { result = await client.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrappedKey.Value, ct); } catch (RequestFailedException exception) when (exception.Status == 400) { throw new SecretDecryptionException(AuthenticationFailed); }`. Every other status propagates unchanged.
     6. Return `result.Key`. The caller owns it; the encryptor checks its length.
   - Steps 2 and 3 run before any Key Vault call.
   - `private static bool IsAllowedWrappedLength(byte[]? value) => value?.Length is Rsa2048WrappedLength or Rsa3072WrappedLength or Rsa4096WrappedLength;`

**Acceptance**: the build is clean. No Key Vault call happens in a constructor.

---

### T6. Options validation, startup check, registration and test factory settings (after T4 and T5)

1. `src/SharpVault.Infrastructure/Encryption/EncryptionOptionsValidator.cs`: `public sealed class EncryptionOptionsValidator(IHostEnvironment environment) : IValidateOptions<EncryptionOptions>`. `Validate(string? name, EncryptionOptions options)` returns `ValidateOptionsResult.Success` when `TryResolveProvider` succeeds and `ValidateOptionsResult.Fail(error)` otherwise.
2. `src/SharpVault.Infrastructure/Encryption/KeyWrapProviderStartupCheck.cs`: `public sealed partial class KeyWrapProviderStartupCheck(IKeyWrapProvider keyWrapProvider, ILogger<KeyWrapProviderStartupCheck> logger) : IHostedService`. Injecting the provider forces construction, and so the constructor guards, at host start.
   - `StartAsync`: if the provider is a `DevelopmentKeyWrapProvider`, `LogDevelopmentProviderInUse(logger)`. Otherwise `LogProviderInUse(logger, keyWrapProvider.GetType().Name)`. Return `Task.CompletedTask`.
   - `StopAsync` returns `Task.CompletedTask`.
   - Messages:
     - Information: `"Data keys are wrapped by {KeyWrapProvider}"`
     - Warning: `"Data keys are wrapped by the development key wrap provider, which is only allowed in the Development environment"`
3. `src/SharpVault.Infrastructure/Encryption/EncryptionRegistration.cs`: `public static class EncryptionRegistration` with `public static IHostApplicationBuilder AddEncryption(this IHostApplicationBuilder builder)`:

   ```csharp
   ArgumentNullException.ThrowIfNull(builder);
   var section = builder.Configuration.GetSection(EncryptionOptions.SectionName);
   var options = section.Get<EncryptionOptions>() ?? new EncryptionOptions();
   var kind = EncryptionSettingsGuard.ResolveProvider(options, builder.Environment);

   builder.Services.AddOptions<EncryptionOptions>().Bind(section).ValidateOnStart();
   builder.Services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();

   if (kind == KeyWrapProviderKind.Development)
   {
       builder.Services.AddSingleton<IKeyWrapProvider, DevelopmentKeyWrapProvider>();
   }
   else
   {
       builder.Services.AddSingleton<IKeyVaultCryptographyClientFactory>(_ =>
           new KeyVaultCryptographyClientFactory(new DefaultAzureCredential(), KeyVaultCryptographyClientFactory.CreateOptions()));
       builder.Services.AddSingleton<IKeyWrapProvider, KeyVaultKeyWrapProvider>();
   }

   builder.Services.AddSingleton<IEnvelopeEncryptor, EnvelopeEncryptor>();
   builder.Services.AddHostedService<KeyWrapProviderStartupCheck>();
   return builder;
   ```

   The guard runs during registration, so a forbidden configuration stops startup before anything is built, which matches the Table Storage guard. Verified: binding `"Development"` works, and the API in Production starts with the Key Vault provider and makes no network call.
4. `src/SharpVault.Infrastructure/DependencyInjection.cs`: add `using SharpVault.Infrastructure.Encryption;` and call `builder.AddEncryption();` directly after `builder.Services.TryAddSingleton(TimeProvider.System);`.
5. Supporting test changes. These are required in this task, because without them every integration test fails at startup (verified: with them, all 394 existing integration tests pass):
   - `tests/SharpVault.IntegrationTests/Factories/ApiTestFactory.cs`:
     - Add `public string DevelopmentKeyMaterial { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));`
     - In `ConfigureWebHost`, before the logging settings, add `builder.UseSetting($"{EncryptionOptions.SectionName}:{nameof(EncryptionOptions.DevelopmentKeyMaterial)}", DevelopmentKeyMaterial);`
   - `tests/SharpVault.IntegrationTests/Factories/ProductionApiTestFactory.cs`:
     - Add `public const string KeyVaultKeyId = "https://sharpvaultfoundationtest.vault.azure.net/keys/sharpvault-kek";`
     - Add `builder.UseSetting($"{EncryptionOptions.SectionName}:{nameof(EncryptionOptions.KeyVaultKeyId)}", KeyVaultKeyId);`

**Acceptance**

- The build is clean.
- `dotnet test SharpVault.slnx --filter "Category!=Performance"` passes (AppHost source tests excepted only if T7 is already in and T13 is not).
- `GET /health` still makes exactly one storage call.

---

### T7. AppHost: generated development key material (after T1, parallel with T2 to T6; T13 follows immediately)

1. `src/SharpVault.AppHost/AppHostResourceNames.cs`: add
   - `DevelopmentKeyMaterial = "development-key-material"`
   - `DevelopmentKeyMaterialVariable = "Encryption__DevelopmentKeyMaterial"`
   - `public const int DevelopmentKeyMaterialLength = 64;`
2. `src/SharpVault.AppHost/AppHost.cs`: directly after the `api` resource and before the web resource, add (verified):

   ```csharp
   if (builder.ExecutionContext.IsRunMode)
   {
       var developmentKeyMaterial = builder.AddParameter(
           AppHostResourceNames.DevelopmentKeyMaterial,
           new GenerateParameterDefault { MinLength = AppHostResourceNames.DevelopmentKeyMaterialLength, Special = false },
           secret: true,
           persist: true);
       api.WithEnvironment(AppHostResourceNames.DevelopmentKeyMaterialVariable, developmentKeyMaterial);
   }
   ```

   - The value is generated once, stored in the AppHost user secrets (`Parameters:development-key-material`) and reused on every run, so data in the persistent Azurite volume stays decryptable.
   - 64 characters from 56 alphanumerics give about 371 bits.
   - Publish mode declares no parameter (verified).

**Acceptance**: the build is clean. The existing AppHost tests `AppHost_Model_DeclaresNoParameterResources` and the `AddParameter`, `WithEnvironment` and `secret:` rows of `AppHost_Source_ContainsNoParametersEnvironmentOverridesOrKeys` fail until T13 replaces them. This is expected (D12).

---

### T8. CI (after T1, parallel with T2 to T6)

`.github/workflows/ci.yml`:

1. The coverage gate step becomes `dotnet run eng/CoverageGate.cs -- TestResults/coverage/Cobertura.xml SharpVault.Application=80 SharpVault.Infrastructure=80 SharpVault.Domain.Encryption=95 SharpVault.Infrastructure.Encryption=95`.
2. Job `performance`, after the performance tests: a step "Benchmarks smoke run" running `dotnet run -c Release --project tests/SharpVault.Benchmarks -- --filter '*' --job dry`. It proves the benchmarks still run: the step fails when `Program` returns exit code 1 (any summary with critical validation errors or a failed report, T1 step 5). The results are not compared.

**Acceptance**: the YAML parses with the foundation's `python3 -c "import yaml..."` check.

---

### T9. Test support and core unit tests (test-writer, after T3)

Support files in `tests/SharpVault.UnitTests/Support/Encryption/` (all `internal sealed` or `internal static`):

- `RecordingKeyWrapProvider : IKeyWrapProvider`:
  - `WrapAsync` honours the cancellation token, records the **same** `dataKey` array instance in `WrapInputs`, stores a copy under a random 16-byte handle, and returns `WrappedDataKey("recording:1", "recording", handle)`.
  - `UnwrapAsync` returns a **new** array with the stored key and records that array in `UnwrapOutputs`.
  - Counters: `WrapCount` and `UnwrapCount`, with `Interlocked` on private fields.
  - Settable `Func<Exception>? WrapFailure` and `UnwrapFailure`, and `int? UnwrapLengthOverride` to return a key of the wrong length.
- `ByteAssert`: `AllZero(byte[])` and `NotAllZero(byte[])`.
- `EncryptionContexts`: `Random(string purpose = "secret.value")` with new Guids.
- `EncryptionMessageText`: copies of every `EncryptionMessages` constant (the same pattern as `TableStorageMessageText`).
- `TestEncryptionOptions`: `Development(string? material = null)` returns `Options.Create(new EncryptionOptions { DevelopmentKeyMaterial = material ?? <48 random base64 characters> })`, and `KeyVault(string keyId)`.

Write the tests in sections 8.1 to 8.4.

### T10. Development provider tests (test-writer, after T4 and T9)

Section 8.5.

### T11. Key Vault tests (test-writer, after T5 and T9)

Support files in `tests/SharpVault.UnitTests/Support/Encryption/`:

- `FakeKeyVault : IKeyVaultCryptographyClientFactory, IDisposable` (verified pattern):
  - Constants `VaultUri = "https://sharpvault-test.vault.azure.net"` and `KeyName = "sharpvault-kek"`, and `public static string VersionlessKeyId`. Make the members that use no instance data `static` (CA1822 fired on the prototype).
  - Holds one `RSA.Create(2048)` per version. `Rotate()` adds a version and makes it current.
  - Records every `GetClient` key id. Counts `WrapCount` and `UnwrapCount`, and records the `WrapInputs` array instances and the `UnwrapOutputs`.
  - Overrides: `WrapKeyIdOverride`, `WrapLengthOverride`, `UnwrapLengthOverride`, and `UnwrapFailureStatus` (throws `RequestFailedException(status, "...")`).
  - Nested `FakeCryptographyClient : CryptographyClient` uses the protected parameterless constructor and overrides `WrapKeyAsync` and `UnwrapKeyAsync`. A versionless client wraps with the current version, as remote mode does. Results come from `Azure.Security.KeyVault.Keys.CryptographyModelFactory.WrapResult` and `UnwrapResult`. RSA-OAEP-SHA256 decrypt failures are thrown as `RequestFailedException(400, "BadParameter")`.
- `FakeKeyVaultHttpHandler : HttpMessageHandler` (verified pattern), used with `new HttpClientTransport(new HttpClient(handler))` as `CryptographyClientOptions.Transport`:
  - With no `Authorization` header it returns 401 with `WWW-Authenticate: Bearer authorization="https://login.microsoftonline.com/{tenant}", resource="https://vault.azure.net"`.
  - `GET /keys/{name}[/{version}]` returns, when `AllowKeyRead` is true, `{"key":{"kid":"{vault}/keys/{name}/{version}","kty":"RSA","key_ops":["wrapKey","unwrapKey"],"n":..,"e":..},"attributes":{"enabled":true}}` (base64url). Otherwise it returns 403.
  - `POST .../wrapKey` and `.../unwrapKey` (match case-insensitively; the SDK sends `wrapKey` and `unwrapKey`) read `{"alg","value"}` and answer `{"kid","value"}`. A failed unwrap returns 400.
  - Records `KeyVaultHttpRequest(string Method, string Host, string Path, bool Authorized, string? Algorithm, string? RequestValue, string? ResponseValue)`.
  - Observed paths: `GET /keys/{name}`, `POST /keys/{name}/wrapKey`, `GET /keys/{name}/{version}`, `POST /keys/{name}/{version}/unwrapKey`.
- `StaticTokenCredential : TokenCredential` returns `new AccessToken("test-access-token", DateTimeOffset.UtcNow.AddHours(1))`.
- Capture SDK logs with `Azure.Core.Diagnostics.AzureEventSourceListener((args, message) => ..., EventLevel.Verbose)`.

Section 8.6 to 8.9.

### T12. Registration, startup, integration and architecture tests (test-writer, after T6 and T9)

Section 8.10 to 8.13. Add `tests/SharpVault.IntegrationTests/Support/EncryptionMessageText.cs`, a copy of the messages used there.

### T13. AppHost tests (test-writer, directly after T7)

Section 8.14.

### T14. Performance tests and benchmarks (test-writer, after T6 and T9)

Section 8.15 and section 9.2.

### T15. Documentation (docs-writer, after all)

- `docs/ENCRYPTION.md` (new), covering:
  - the key hierarchy, the envelope and associated data layouts (with the golden vector), the version byte ranges and the purpose rules
  - the session API and how to use it (`using`, one request), the configuration keys, and the two providers and their refusal layers
  - Key Vault setup: an RSA 3072+ key, HSM-backed in production, `wrapKey`/`unwrapKey` operations, the "Key Vault Crypto User" role on the app identity, and `Encryption__KeyVaultKeyId` set to the **versionless** key id
  - the rotation procedure (D8): rotate, then restart the API, and never disable or delete an old version until a rewrap has migrated every wrapped key
  - the Key Vault call counts, how the wrapped key and envelopes are stored (section 4.5), the separation from OTL-3, and the residual risks (D18, D24)
- Local development, in `docs/ENCRYPTION.md` (and linked from `docs/LOCAL-DEV.md` if that file exists by then):
  - the AppHost generates the development key material into the AppHost user secrets
  - how to reset it: `dotnet user-secrets remove Parameters:development-key-material --project src/SharpVault.AppHost`, then wipe the Azurite volume
  - how to run against a real vault locally: `Encryption:Provider=KeyVault`, `Encryption:KeyVaultKeyId`, and `az login` for `DefaultAzureCredential`
- Testing notes, in `docs/ENCRYPTION.md` (and linked from `docs/TESTING.md` if it exists): `RecordingKeyWrapProvider`, `FakeKeyVault`, `FakeKeyVaultHttpHandler`, no live Azure in CI, and `dotnet run -c Release --project tests/SharpVault.Benchmarks -- --filter '*'`.
- `README.md`: add `tests/SharpVault.Benchmarks` to the architecture tree.
- `docs/PROGRESS.md`:
  - move item 2 to Done
  - add to Next:
    - "Key rewrap maintenance job using `RewrapDataKeyAsync` (D8)"
    - "vault-and-folders decides the data key per field (D5) and uses distinct item ids for history entries (D24)"
    - "Ask to allow learn.microsoft.com in the session to confirm Key Vault throttling limits (D27)"

## 6. Threats

Every surface this work touches has threats listed with their proving tests. The surfaces are: the session API, the envelope encryptor, both key wrap providers, the Key Vault client factory, configuration and startup (Api), and the AppHost parameter. There are no endpoints, so there is no HTTP authorization matrix in this work (section 8.16).

| # | Category | Threat | Mitigation | Proving tests |
|---|---|---|---|---|
| TH1 | Tampering | Nonce reuse under one data key breaks GCM confidentiality and integrity | 96-bit nonce from `RandomNumberGenerator` for every `Encrypt` and `ReEncrypt`. A fresh random data key per `CreateDataKeyAsync`. Sessions encrypt only a handful of values, far below the 2^32 random-nonce limit. No `System.Random` anywhere | `Encrypt_HundredThousandTimes_NeverRepeatsNonce`, `Encrypt_SameTextTwice_ProducesDifferentEnvelopes`, `ReEncrypt_ToNewOwner_ProducesFreshNonce`, `CreateDataKeyAsync_TwoSessions_UseDifferentDataKeys`, `WrapAsync_SameDataKeyTwice_ProducesDifferentWrappedValues`, `SourceFiles_Src_DoNotUseSystemRandom` |
| TH2 | Access / tampering | Ciphertext copied from one owner's row into another owner's row | Owner id in the associated data | `Decrypt_DifferentOwner_ThrowsAuthenticationFailed`, `ReEncrypt_ToNewOwner_DecryptsOnlyWithNewOwnerContext` |
| TH3 | Tampering | Ciphertext swapped between secrets (same or different data key) | Item id in the associated data, and a different data key per item | `Decrypt_DifferentItem_ThrowsAuthenticationFailed`, `Decrypt_EnvelopeSwappedBetweenItemsWithSameDataKey_ThrowsAuthenticationFailed`, `Decrypt_EnvelopeFromAnotherDataKey_ThrowsAuthenticationFailed` |
| TH4 | Tampering | Ciphertext swapped between fields of one secret (notes into password) | Purpose in the associated data | `Decrypt_DifferentPurpose_ThrowsAuthenticationFailed` |
| TH5 | Tampering | Ambiguous associated data encoding lets two contexts collide | Length-prefixed fields, a strict purpose alphabet, empty ids rejected, and a golden vector | `Write_KnownContext_ProducesDocumentedByteLayout`, `Write_DifferentPurposeItemOrOwner_ProducesDifferentBytes`, `EnsureValid_InvalidPurpose_ThrowsArgumentException`, `EnsureValid_EmptyItemId_ThrowsArgumentException`, `EnsureValid_EmptyOwnerId_ThrowsArgumentException` |
| TH6 | Tampering | Modified ciphertext, tag or nonce | GCM authentication. No partial plaintext is returned | `Decrypt_AnySingleBitFlipped_ThrowsExpectedReason`, `Decrypt_TagReplacedWithZeros_ThrowsAuthenticationFailed`, `Decrypt_ExtraByteAppended_ThrowsAuthenticationFailed`, `ReEncrypt_TamperedSource_ThrowsAndReturnsNothing` |
| TH7 | Tampering | Version byte flipped, or a browser one-time link blob fed to the server decryptor | Only `0x01` is accepted. The version is also bound in the associated data. `0x80`-`0xFF` are reserved and rejected | `Decrypt_UnknownVersionByte_ThrowsUnsupportedVersion` |
| TH8 | Tampering | Modified wrapped data key | RSA-OAEP fails in Key Vault (400 is mapped to AuthenticationFailed). The development provider uses a GCM tag | `UnwrapAsync_TamperedWrappedValue_ThrowsAuthenticationFailed`, `UnwrapAsync_AnyByteFlipped_ThrowsAuthenticationFailed` |
| TH9 | Key confusion | A development-wrapped key accepted by the Key Vault provider, or the reverse | Exact algorithm and key id checks before any call | `UnwrapAsync_DevelopmentWrappedKey_ThrowsKeyMismatchWithoutCallingKeyVault`, `UnwrapAsync_KeyVaultWrappedKey_ThrowsKeyMismatch` |
| TH10 | Key confusion / downgrade | Stored algorithm changed to `RSA1_5` to get a padding oracle out of Key Vault | Always sends RSA-OAEP-256, and the stored algorithm must equal it exactly | `UnwrapAsync_AlgorithmNotRsaOaep256_ThrowsKeyMismatchWithoutCallingKeyVault`, `KeyVaultRequests_WrapAndUnwrap_UseRsaOaep256OnTheWire` |
| TH11 | SSRF / leakage | A stored key id points unwrap at another host, vault or key, so the wrapped key and a bearer token go to the attacker | The key id must be an https version of the configured vault and key under `/keys/`, with no query, fragment or user info, and at most 512 characters. Challenge resource verification stays on | `UnwrapAsync_KeyIdNotAVersionOfConfiguredKey_ThrowsKeyMismatchWithoutCallingKeyVault`, `IsVersionOf_Mismatch_ReturnsFalse`, `TryParseConfigured_InvalidKeyId_ReturnsFalse`, `KeyVaultRequests_AnyOperation_GoOnlyToConfiguredVaultHost`, `CreateOptions_Always_KeepsChallengeResourceVerification` |
| TH12 | Key confusion | Key Vault (or a misconfiguration) wraps with an unexpected key | The wrap result must be a version of the configured key with an RSA-sized value | `WrapAsync_KeyVaultReturnsUnexpectedKeyId_ThrowsUnexpectedKey`, `WrapAsync_KeyVaultReturnsUnexpectedWrappedLength_ThrowsUnexpectedKey` |
| TH13 | Key confusion | An unwrap returns a key that is not 32 bytes | The encryptor rejects it and zeroes it | `OpenDataKeyAsync_UnwrappedKeyWrongLength_ZeroesItAndThrowsMalformedWrappedKey` |
| TH14 | Escalation / config | Development provider runs outside Development | Four layers: the registration guard, the options validator on start, the constructor guard (forced at startup by the hosted check), and an architecture test limiting providers to two. Undefined enum numbers are rejected | `Constructor_NonDevelopmentEnvironment_Throws`, `ResolveProvider_NonDevelopmentWithExplicitDevelopmentProvider_Throws`, `ResolveProvider_UndefinedProviderValue_Throws`, `Validate_NonDevelopmentWithDevelopmentProvider_Fails`, `AddEncryption_ProductionWithDevelopmentProvider_ThrowsDuringRegistration`, `Startup_ProductionWithDevelopmentProvider_FailsWithoutEchoingKeyMaterial`, `Startup_StagingWithDevelopmentProvider_Fails`, `Startup_ProductionWithProviderAsNumberOne_Fails`, `ProductionAssemblies_KeyWrapProviders_AreOnlyKeyVaultAndDevelopment` |
| TH15 | Leakage / config | Development key material leaks into non-development configuration, the repository or a publish manifest | The guard rejects the material outside Development. The repository configuration test forbids `KeyMaterial`. The AppHost parameter exists only in run mode, only for the API | `ResolveProvider_NonDevelopmentWithDevelopmentKeyMaterial_ThrowsWithoutEchoingValue`, `Startup_ProductionWithDevelopmentKeyMaterial_FailsWithoutEchoingIt`, `ConfigurationFile_Content_ContainsNoConnectionStringsOrSecrets` (updated), `AppHost_PublishMode_DeclaresNoParameterResources`, `AppHost_WebProject_ReceivesNoEncryptionSettings` |
| TH16 | Leakage | Plaintext, data keys, key material, envelopes or tokens in logs | Logs carry only the item id, purpose and reason. SDK content logging is off and `Authorization` is not a logged header | `Decrypt_FailedAuthentication_LogsIdsAndReasonWithoutData`, `EnvelopeEncryptor_DevelopmentHostRoundTrip_LogsNoPlaintextKeyMaterialOrEnvelope`, `EnvelopeEncryptor_DevelopmentHostTamperedEnvelope_LogsWarningWithoutData`, `KeyVaultRequests_VerboseSdkLogging_NeverContainDataKeyWrappedKeyOrToken`, `CreateOptions_Always_DisablesContentLogging`, `CreateOptions_Always_DoesNotLogAuthorizationHeader` |
| TH17 | Leakage | Secrets in exception messages or `ToString()` of exceptions | Constant messages. `SecretDecryptionException` has no inner exception or data. Guard messages never echo configuration. A `DecoderFallbackException` (which carries the offending bytes) is mapped to `MalformedEnvelope` | `Exceptions_FromEveryFailurePath_ContainNoPlaintextKeyOrEnvelope`, `Decrypt_AuthenticatedInvalidUtf8_ThrowsMalformedEnvelopeWithoutBytes`, `SecretDecryptionException_AnyReason_UsesConstantMessage`, `Encrypt_PlaintextOverMaximumBytes_ThrowsWithoutPlaintextInMessage`, `ResolveProvider_NonDevelopmentWithInvalidKeyId_ThrowsWithoutEchoingValue`, `Startup_DevelopmentWithShortKeyMaterial_FailsWithoutEchoingIt` |
| TH18 | Leakage | `ToString()`, string interpolation or a debugger shows key material | `WrappedDataKey.ToString` shows only the key id, algorithm and length. Sessions, providers and options are classes without custom formatting | `WrappedDataKey_ToString_ShowsKeyIdAlgorithmAndLengthOnly`, `DataKeySession_ToString_ContainsNoKeyOrPlaintext`, `DevelopmentKeyWrapProvider_ToString_ContainsNoKeyMaterial`, `EncryptionOptions_ToString_ContainsNoKeyMaterial`, `KeyId_Always_DoesNotContainKeyMaterial` |
| TH19 | Leakage (memory) | Data keys or plaintext bytes stay in memory | The session zeroes its key on `Dispose`. Every failure path zeroes. Pooled plaintext buffers are zeroed before they go back to the pool. The Key Vault provider passes the caller's array without copying. The development KEK is zeroed on dispose, and a disposed development provider refuses to wrap or unwrap. Residual risk: D18 | `Dispose_AfterCreate_ZeroesTheDataKeyPassedToWrap`, `Dispose_AfterOpen_ZeroesTheDataKeyReturnedByUnwrap`, `DataKey_BeforeDispose_IsNotAllZeros`, `CreateDataKeyAsync_WrapThrows_ZeroesDataKeyAndPropagates`, `CreateDataKeyAsync_Cancelled_ZeroesDataKeyAndThrowsOperationCanceled`, `OpenDataKeyAsync_UnwrappedKeyWrongLength_ZeroesItAndThrowsMalformedWrappedKey`, `RewrapDataKeyAsync_Always_UnwrapsOnceWrapsOnceAndZeroesKey`, `RewrapDataKeyAsync_WrapThrows_ZeroesUnwrappedKey`, `WrapAsync_ValidDataKey_PassesSameArrayWithoutCopy`, `WrapAsync_AfterDispose_ThrowsObjectDisposedException`, `UnwrapAsync_AfterDispose_ThrowsObjectDisposedException`, `Dispose_CalledTwice_DoesNotThrowAndKeyStaysZero`, `Dispose_AfterKeyVaultOpen_ZeroesTheSdkReturnedKey` |
| TH20 | Bug | A session used after `Dispose` encrypts with a zeroed key | `ObjectDisposedException` on every member | `Encrypt_AfterDispose_ThrowsObjectDisposedException`, `Decrypt_AfterDispose_ThrowsObjectDisposedException`, `ReEncrypt_AfterDispose_ThrowsObjectDisposedException`, `Dispose_Twice_DoesNotThrow` |
| TH21 | Availability / rotation | Rotating the KEK makes old data unreadable, or new data keeps the old version | Unwrap uses the recorded versioned key id. New wraps use the latest version (after a restart when the SDK wraps locally). `RewrapDataKeyAsync` migrates. The development key id changes with the key material | `UnwrapAsync_AfterKeyRotation_UnwrapsOldVersionWithItsOwnClient`, `WrapAsync_AfterKeyRotation_UsesNewVersion`, `RewrapDataKeyAsync_AfterRotation_WrapsWithNewVersionAndOpensToSameKey`, `UnwrapAsync_WrappedByOtherKeyMaterial_ThrowsKeyMismatch`, `KeyId_DifferentKeyMaterial_Differs` |
| TH22 | Bug / tampering | Truncated envelope or wrapped key causes an out-of-range read or a crash | Length checks before slicing | `Decrypt_EnvelopeShorterThanOverhead_ThrowsMalformedEnvelope`, `Decrypt_EnvelopeTruncatedByOneByte_ThrowsAuthenticationFailed`, `Decrypt_NullEnvelope_ThrowsArgumentNullException`, `UnwrapAsync_WrappedValueLengthNotRsaSize_ThrowsMalformedWithoutCallingKeyVault`, `UnwrapAsync_WrongLength_ThrowsMalformedWrappedKey`, `UnwrapAsync_NullValue_ThrowsMalformedWrappedKey` |
| TH23 | Denial of service | Oversized plaintext, envelopes, wrapped keys or key ids, or a flood of fake key versions | 32 KiB plaintext, 32,797-byte envelope, RSA-sized wrapped values only, 512-character key ids, and at most 64 cached clients. All checked before any allocation or Key Vault call | `Encrypt_PlaintextOverMaximumBytes_ThrowsWithoutPlaintextInMessage`, `Encrypt_MultiByteTextOverMaximumBytes_Throws`, `Decrypt_EnvelopeOverMaximumLength_ThrowsMalformedEnvelope`, `UnwrapAsync_WrappedValueLengthNotRsaSize_ThrowsMalformedWithoutCallingKeyVault`, `TryParseConfigured_InvalidKeyId_ReturnsFalse` (513 characters), `GetClient_MoreThanCacheLimit_StopsCachingNewClients` |
| TH24 | Bug | Invalid Unicode silently corrupted, or normalisation changes a password | Strict UTF-8 that throws on lone surrogates. No normalisation | `Encrypt_LoneSurrogate_ThrowsArgumentException`, `Encrypt_PrecomposedAndDecomposedText_RoundTripsByteExact` |
| TH25 | Races | Parallel use corrupts state | The providers create `AesGcm` per call or are stateless. Sessions are per request and documented as not thread-safe | `Sessions_UsedInParallel_EachRoundTrips`, `WrapAndUnwrap_InParallel_AllRoundTrip` |
| TH26 | Denial of service / bug | A Key Vault outage or throttling is treated as success, or hangs requests | 10 s network timeout and 3 retries. Every non-400 failure propagates as `RequestFailedException` and is never swallowed | `UnwrapAsync_KeyVaultFailsWithOtherStatus_PropagatesRequestFailedException`, `CreateOptions_Always_UsesTenSecondTimeoutAndThreeRetries` |
| TH27 | Availability | Startup calls Key Vault, or needs a token, so the API cannot start when Key Vault is unreachable | Clients are created lazily, with no network call at construction | `Startup_Production_ResolvesKeyVaultKeyWrapProvider`, `GetClient_Constructed_MakesNoNetworkCall`, `Constructor_ValidKeyId_RequestsOnlyConfiguredClientAndMakesNoCalls` |
| TH28 | Tampering (rollback) | An attacker with storage write access swaps a current envelope for an older history envelope | History entries use distinct item ids (D24), so the swap fails authentication. Replaying the same row with an older copy is outside what AES-GCM can detect, and is covered by storage access control and audit (D24) | `Decrypt_DifferentItem_ThrowsAuthenticationFailed` |
| TH29 | Escalation (TRF-6) | Data re-encrypted for the target before approval | Re-encryption only happens through an explicit `ReEncrypt` call, with no implicit path, and it makes no Key Vault calls. The transfer feature tests the approval gate | `ReEncrypt_Always_KeepsWrappedKeyAndMakesNoKeyWrapCalls` |
| TH30 | Leakage (tests) | Test runs write generated key material into a developer's AppHost user secrets | AppHost tests pass the parameter through configuration (verified: nothing is written) | `AppHost_Model_DeclaresOnlyTheDevelopmentKeyMaterialParameter` (built with the configured value) |

Side channels: SharpVault code compares no secret values. The platform AES-GCM verifies tags in constant time, and the key id and algorithm checks only look at non-secret data. So there is no test, by design.

## 7. Tests: conventions

- Names follow `Scenario_Condition_ExpectedResult`. Use xUnit `Assert` only.
- Pass `TestContext.Current.CancellationToken` to every call that accepts a token.
- Security tests get `[Trait("Category", "Security")]` (class level when every test in the class is security), and performance tests `[Trait("Category", "Performance")]`.
- Unit tests go in `tests/SharpVault.UnitTests/Infrastructure/Encryption/`, model tests in `tests/SharpVault.UnitTests/Application/Encryption/`, and integration tests in `tests/SharpVault.IntegrationTests/ApiTests/`. Every integration test class has `[Collection(...)]`.
- Never assert on a message unless you compare it with the exact `EncryptionMessageText` constant.
- "Without echoing" means the exception's `ToString()` and every captured log entry contain none of: the configured value, the plaintext marker, the hex or base64 of the key, and the hex or base64 of the envelope.
- No fixed delays.

## 8. Tests

### 8.1 `EnvelopeAssociatedDataTests`

- `Write_KnownContext_ProducesDocumentedByteLayout`: the golden hex from section 4.4.
- `Write_MaximumLengthPurpose_ReturnsMaxLength`
- `Write_DifferentPurposeItemOrOwner_ProducesDifferentBytes` (Theory: which part changes)
- `EnsureValid_ValidPurpose_DoesNotThrow` (Theory: `a`, `secret.value`, `folder.name`, `secret.history-value`, 64 times `a`)
- `EnsureValid_InvalidPurpose_ThrowsArgumentException` (Security; Theory: `""`, `" "`, `Secret.value`, `1secret`, `.secret`, `secret value`, `secret/value`, `secret_value`, `sécret`, 65 times `a`, `"secret.value\n"`, `"' or PartitionKey ne '"`). The message equals `InvalidPurpose`.
- `EnsureValid_NullPurpose_ThrowsArgumentException`
- `EnsureValid_EmptyItemId_ThrowsArgumentException` and `EnsureValid_EmptyOwnerId_ThrowsArgumentException` (message `EmptyContextId`)
- `EnsureValid_NullContext_ThrowsArgumentNullException`

### 8.2 `DataKeySessionTests` (sessions created through `EnvelopeEncryptor` with `RecordingKeyWrapProvider`)

- `EncryptThenDecrypt_SameContext_ReturnsOriginalText` (Theory: `""`, `a`, `hunter2`, `pässwörd 密码 😀`, 32,768 ASCII characters, 8,192 four-byte emoji)
- `Encrypt_Envelope_HasVersionOneAndLengthOfPlaintextBytesPlusOverhead`
- `Decrypt_EnvelopeBuiltIndependentlyWithAesGcm_ReturnsPlaintext`. Use a provider that returns a known key, build the envelope with raw `AesGcm` and the layouts in sections 4.3 and 4.4 (verified).
- `Encrypt_PrecomposedAndDecomposedText_RoundTripsByteExact` (`"é"` and `"é"` round-trip to exactly themselves and produce different lengths)
- `Encrypt_PlaintextExactlyMaximumBytes_Succeeds`
- `ReEncrypt_ToNewOwner_DecryptsOnlyWithNewOwnerContext` (TRF-7)
- `ReEncrypt_SameContext_ProducesNewEnvelopeThatDecrypts`
- `ReEncrypt_Always_KeepsWrappedKeyAndMakesNoKeyWrapCalls`
- `Dispose_Twice_DoesNotThrow`
- `Encrypt_AfterDispose_ThrowsObjectDisposedException`, `Decrypt_AfterDispose_ThrowsObjectDisposedException`, `ReEncrypt_AfterDispose_ThrowsObjectDisposedException`
- `Encrypt_NullPlaintext_ThrowsArgumentNullException`, `Decrypt_NullEnvelope_ThrowsArgumentNullException`, `ReEncrypt_NullEnvelope_ThrowsArgumentNullException`
- `Encrypt_InvalidContext_ThrowsBeforeEncrypting` (Theory: bad purpose, empty item, empty owner)
- `ReEncrypt_InvalidTargetContext_ThrowsArgumentExceptionBeforeDecrypting`

### 8.3 `DataKeySessionSecurityTests` (class-level Security trait)

- `Encrypt_HundredThousandTimes_NeverRepeatsNonce` (one session, a `HashSet` of nonce hex strings; about 0.3 s, verified)
- `Encrypt_SameTextTwice_ProducesDifferentEnvelopes`
- `ReEncrypt_ToNewOwner_ProducesFreshNonce`
- `Decrypt_DifferentOwner_ThrowsAuthenticationFailed`
- `Decrypt_DifferentItem_ThrowsAuthenticationFailed`
- `Decrypt_DifferentPurpose_ThrowsAuthenticationFailed`
- `Decrypt_EnvelopeSwappedBetweenItemsWithSameDataKey_ThrowsAuthenticationFailed`
- `Decrypt_EnvelopeFromAnotherDataKey_ThrowsAuthenticationFailed`
- `Decrypt_AnySingleBitFlipped_ThrowsExpectedReason`: for every byte index of a 64-byte-plaintext envelope and every bit, index 0 gives `UnsupportedVersion` and every other index gives `AuthenticationFailed`.
- `Decrypt_TagReplacedWithZeros_ThrowsAuthenticationFailed`
- `Decrypt_ExtraByteAppended_ThrowsAuthenticationFailed`
- `Decrypt_UnknownVersionByte_ThrowsUnsupportedVersion` (Theory: `0x00`, `0x02`, `0x7F`, `0x80`, `0xFF`)
- `Decrypt_EnvelopeShorterThanOverhead_ThrowsMalformedEnvelope` (Theory: every length 0 to 28)
- `Decrypt_EnvelopeTruncatedByOneByte_ThrowsAuthenticationFailed`
- `Decrypt_EnvelopeOverMaximumLength_ThrowsMalformedEnvelope` (`MaxEnvelopeBytes + 1`, with version byte `0x01`)
- `Decrypt_AuthenticatedInvalidUtf8_ThrowsMalformedEnvelopeWithoutBytes` (TH17): build a valid envelope with raw `AesGcm` under the session's data key (`RecordingKeyWrapProvider.WrapInputs[0]`) over the bytes `0x68 0xFF 0xFE`. `Decrypt` throws `SecretDecryptionException` with reason `MalformedEnvelope` and no inner exception. The test checks the byte patterns (`FF`, `FE` and `[`) on `exception.Message`, and checks that `exception.ToString()` contains neither `Unable to translate` nor `BytesUnknown`.
- `Encrypt_PlaintextOverMaximumBytes_ThrowsWithoutPlaintextInMessage` (32,769 ASCII bytes; message `PlaintextTooLarge`)
- `Encrypt_MultiByteTextOverMaximumBytes_Throws` (10,923 `€` = 32,769 bytes)
- `Encrypt_LoneSurrogate_ThrowsArgumentException` (Theory: `"\ud800"`, `"a\udc00b"`; message `PlaintextNotValidUnicode`)
- `ReEncrypt_TamperedSource_ThrowsAndReturnsNothing`
- `ReEncrypt_WrongSourceContext_ThrowsAuthenticationFailed`
- `Decrypt_FailedAuthentication_LogsIdsAndReasonWithoutData`: build the session with a `CollectingLogger`. There is exactly one Warning, containing the item id, purpose and `AuthenticationFailed`, and none of the plaintext, the envelope hex or base64, or the owner id.
- `Dispose_AfterCreate_ZeroesTheDataKeyPassedToWrap` (`RecordingKeyWrapProvider.WrapInputs[0]` is `NotAllZero` before and `AllZero` after)
- `Dispose_AfterOpen_ZeroesTheDataKeyReturnedByUnwrap`
- `DataKey_BeforeDispose_IsNotAllZeros`
- `DataKeySession_ToString_ContainsNoKeyOrPlaintext`
- `Exceptions_FromEveryFailurePath_ContainNoPlaintextKeyOrEnvelope`: collect exceptions from tampering, wrong context, version, truncation, oversize and surrogate cases, and check their `ToString()`.

### 8.4 `EnvelopeEncryptorTests` (Security where marked)

- `CreateDataKeyAsync_Always_CallsWrapOnceAndUnwrapNever`
- `CreateDataKeyAsync_TwoSessions_UseDifferentDataKeys` (Security)
- `CreateDataKeyAsync_DataKey_Is32Bytes`
- `CreateDataKeyAsync_WrapThrows_ZeroesDataKeyAndPropagates` (Security)
- `CreateDataKeyAsync_Cancelled_ZeroesDataKeyAndThrowsOperationCanceled` (Security; a token that is already cancelled)
- `OpenDataKeyAsync_Always_CallsUnwrapOnceAndWrapNever`
- `OpenDataKeyAsync_UnwrappedKeyWrongLength_ZeroesItAndThrowsMalformedWrappedKey` (Security; Theory: 0, 16, 31, 33, 64; the array returned by the provider is `AllZero`)
- `OpenDataKeyAsync_ProviderThrowsDecryptionException_LogsReasonAndRethrowsSameException`
- `OpenDataKeyAsync_ProviderThrowsOtherException_PropagatesWithoutLogging`
- `OpenDataKeyAsync_NullWrappedKey_ThrowsArgumentNullException`
- `RewrapDataKeyAsync_Always_UnwrapsOnceWrapsOnceAndZeroesKey` (Security)
- `RewrapDataKeyAsync_WrapThrows_ZeroesUnwrappedKey` (Security)
- `RewrapDataKeyAsync_Result_OpensToSameDataKey` (an old envelope decrypts with a session opened from the rewrapped key)
- `RewrapDataKeyAsync_NullWrappedKey_ThrowsArgumentNullException`
- `Sessions_UsedInParallel_EachRoundTrips` (Security; 64 parallel tasks each create, encrypt, open and decrypt)

`Application/Encryption/EncryptionModelsTests` (Security):

- `WrappedDataKey_ToString_ShowsKeyIdAlgorithmAndLengthOnly` (no hex or base64 of `Value`)
- `WrappedDataKey_ToStringWithNullValue_ShowsLengthZero`
- `SecretDecryptionException_AnyReason_UsesConstantMessage` (Theory over every `DecryptionFailureReason`; `InnerException` is null)
- `EncryptionContext_ToString_ContainsOnlyPurposeAndIds`

### 8.5 `DevelopmentKeyWrapProviderTests`

- `Constructor_NonDevelopmentEnvironment_Throws` (Security; Theory: `Production`, `Staging`, `Test`, `Dev`, `Development2`, `""`; message `DevelopmentProviderNotAllowed`)
- `Constructor_DevelopmentEnvironmentInAnyCase_Succeeds` (Theory: `Development`, `development`). This documents the framework's case-insensitive `IsDevelopment`.
- `Constructor_MissingOrShortKeyMaterial_ThrowsWithoutEchoingIt` (Security; Theory: null, `""`, 40 spaces, 31 characters, 31 characters padded with spaces)
- `Constructor_KeyMaterialOfThirtyTwoCharacters_Succeeds`
- `KeyId_SameKeyMaterial_IsStable`
- `KeyId_DifferentKeyMaterial_Differs`
- `KeyId_Format_IsPrefixAndSixteenLowercaseHexCharacters`
- `KeyId_Always_DoesNotContainKeyMaterial` (Security)
- `WrapThenUnwrap_Always_ReturnsOriginalDataKey`
- `WrapAsync_SameDataKeyTwice_ProducesDifferentWrappedValues` (Security)
- `WrapAsync_Output_IsSixtyBytesWithDevelopmentAlgorithmAndKeyId`
- `WrapAsync_DataKeyNot32Bytes_Throws` (Theory: 0, 16, 31, 33; message `InvalidDataKeyLength`)
- `WrapAsync_NullDataKey_ThrowsArgumentNullException`
- `UnwrapAsync_WrappedByOtherKeyMaterial_ThrowsKeyMismatch` (Security)
- `UnwrapAsync_KeyIdRewrittenToThisProvider_ThrowsAuthenticationFailed` (Security; a value from provider A with A's key id replaced by B's, unwrapped by B)
- `UnwrapAsync_KeyVaultWrappedKey_ThrowsKeyMismatch` (Security; `https://...` key id, `RSA-OAEP-256`, 256 bytes)
- `UnwrapAsync_AlgorithmChanged_ThrowsKeyMismatch` (Security)
- `UnwrapAsync_AnyByteFlipped_ThrowsAuthenticationFailed` (Security; Theory: 0, 11, 12, 43, 44, 59)
- `UnwrapAsync_WrongLength_ThrowsMalformedWrappedKey` (Security; Theory: 0, 59, 61, 256)
- `UnwrapAsync_NullValue_ThrowsMalformedWrappedKey`
- `UnwrapAsync_TwoCalls_ReturnDifferentArrays`
- `WrapAsync_AfterDispose_ThrowsObjectDisposedException` (Security)
- `UnwrapAsync_AfterDispose_ThrowsObjectDisposedException` (Security; a value wrapped before `Dispose`)
- `Dispose_CalledTwice_DoesNotThrowAndKeyStaysZero` (Security; reads the private `_keyEncryptionKey` array by reflection before disposing, then asserts the second `Dispose` does not throw and every byte is zero)
- `DevelopmentKeyWrapProvider_ToString_ContainsNoKeyMaterial` (Security)
- `WrapAndUnwrap_InParallel_AllRoundTrip` (Security; 1,000 parallel)

### 8.6 `KeyVaultKeyIdsTests` (Security)

- `TryParseConfigured_VersionlessHttpsKeyId_ReturnsTrue` (Theory: `https://x.vault.azure.net/keys/k`, the same with a trailing `/`, `https://x.managedhsm.azure.net/keys/k`, `https://x.vault.azure.cn/keys/k`, `https://x.vault.azure.net:8443/keys/k`)
- `TryParseConfigured_InvalidKeyId_ReturnsFalse` (Theory: null, `""`, `"   "`, `not a uri`, `/keys/k`, `http://x.vault.azure.net/keys/k`, `https://x.vault.azure.net/keys/k/0123abcd`, `https://x.vault.azure.net/secrets/k`, `https://x.vault.azure.net/certificates/k`, `https://x.vault.azure.net//keys/k`, `https://x.vault.azure.net/keys`, `https://x.vault.azure.net/keys/k/v/extra`, `https://user:pw@x.vault.azure.net/keys/k`, `https://x.vault.azure.net/keys/k?sig=abc`, `https://x.vault.azure.net/keys/k#f`, a valid id padded to 513 characters). The `secrets`, `certificates` and `//keys` rows guard the verified `KeyVaultKeyIdentifier.TryCreate` gap.
- `IsVersionOf_SameVaultAndNameWithVersion_ReturnsTrue` (Theory: exact; host in upper case; name in upper case)
- `IsVersionOf_Mismatch_ReturnsFalse` (Theory: other vault, `x.vault.azure.net.evil.example`, other key name, versionless, http, other port, `/secrets/` path, `development:0123456789abcdef`, null, `""`, 513 characters)

### 8.7 `KeyVaultCryptographyClientFactoryTests`

- `CreateOptions_Always_DisablesContentLogging` (Security)
- `CreateOptions_Always_KeepsChallengeResourceVerification` (Security; `DisableChallengeResourceVerification` is false)
- `CreateOptions_Always_DoesNotLogAuthorizationHeader` (Security; `LoggedHeaderNames` does not contain `Authorization`, verified)
- `CreateOptions_Always_UsesTenSecondTimeoutAndThreeRetries`
- `GetClient_SameKeyIdTwice_ReturnsSameInstance`
- `GetClient_KeyIdsDifferingOnlyInCase_ReturnSameInstance`
- `GetClient_DifferentVersions_ReturnDifferentInstances`
- `GetClient_MoreThanCacheLimit_StopsCachingNewClients` (Security; after 64 distinct ids, two calls with a 65th id return different instances)
- `GetClient_Constructed_MakesNoNetworkCall` (Security; `FakeKeyVaultHttpHandler` records 0 requests)
- `GetClient_NullKeyId_ThrowsArgumentNullException`

### 8.8 `KeyVaultKeyWrapProviderTests` (with `FakeKeyVault`)

- `Constructor_InvalidConfiguredKeyId_Throws` (Security; Theory: null, versioned, http, `/secrets/`; message `KeyVaultKeyIdInvalid`)
- `Constructor_ValidKeyId_RequestsOnlyConfiguredClientAndMakesNoCalls`
- `WrapAsync_ValidDataKey_ReturnsVersionedKeyIdRsaOaep256AndWrappedValue`
- `WrapAsync_ValidDataKey_PassesSameArrayWithoutCopy` (Security; `Assert.Same`)
- `WrapAsync_DataKeyNot32Bytes_ThrowsWithoutCallingKeyVault` (Theory: 0, 16, 31, 33, 64)
- `WrapAsync_KeyVaultReturnsUnexpectedKeyId_ThrowsUnexpectedKey` (Security; Theory over the key id `FakeKeyVault` returns: other key name, other vault, sibling-suffix host `x.vault.azure.net.evil.example`, `http`, other port, `/secrets/` path, `/certificates/` path, `//keys` path, a query string, a development id `development:0123456789abcdef`, and the versionless configured id; every row throws `InvalidOperationException` with message `KeyVaultReturnedUnexpectedKey`)
- `WrapAsync_KeyVaultReturnsUnexpectedWrappedLength_ThrowsUnexpectedKey` (Security)
- `WrapAsync_AfterKeyRotation_UsesNewVersion` (Security)
- `WrapAsync_WithToken_PassesTokenToClient`
- `UnwrapAsync_WrappedByProvider_ReturnsOriginalDataKey`
- `UnwrapAsync_AfterKeyRotation_UnwrapsOldVersionWithItsOwnClient` (Security; the client is requested for the old versioned id)
- `UnwrapAsync_KeyIdDifferingOnlyInCase_IsAccepted`
- `UnwrapAsync_AlgorithmNotRsaOaep256_ThrowsKeyMismatchWithoutCallingKeyVault` (Security; Theory: `RSA1_5`, `RSA-OAEP`, `rsa-oaep-256`, `""`, `development-A256GCM`)
- `UnwrapAsync_KeyIdNotAVersionOfConfiguredKey_ThrowsKeyMismatchWithoutCallingKeyVault` (Security; Theory: other host, sibling suffix host, other key, versionless, http, other port, `/secrets/`, development key id, 513 characters)
- `UnwrapAsync_DevelopmentWrappedKey_ThrowsKeyMismatchWithoutCallingKeyVault` (Security; uses real `DevelopmentKeyWrapProvider` output)
- `UnwrapAsync_WrappedValueLengthNotRsaSize_ThrowsMalformedWithoutCallingKeyVault` (Security; Theory: 0, 1, 255, 257, 1,024)
- `UnwrapAsync_NullValue_ThrowsMalformedWrappedKey`
- `UnwrapAsync_TamperedWrappedValue_ThrowsAuthenticationFailed` (Security)
- `UnwrapAsync_KeyVaultFailsWithOtherStatus_PropagatesRequestFailedException` (Theory: 401, 403, 404, 429, 500, 503)
- `UnwrapAsync_WithToken_PassesTokenToClient`
- `CreateDataKeyAsync_KeyVault_CallsWrapOnceAndUnwrapNever`, `OpenDataKeyAsync_KeyVault_CallsUnwrapOnceAndWrapNever`, `RewrapDataKeyAsync_KeyVault_CallsUnwrapOnceAndWrapOnce`, `RewrapDataKeyAsync_AfterRotation_WrapsWithNewVersionAndOpensToSameKey` (these use `EnvelopeEncryptor` over the provider)
- `Session_EncryptDecryptAndReEncryptHundredTimes_MakesNoKeyVaultCalls`
- `Dispose_AfterKeyVaultOpen_ZeroesTheSdkReturnedKey` (Security; `FakeKeyVault.UnwrapOutputs[0]` is `AllZero` after `Dispose`)

### 8.9 `KeyVaultSdkPipelineTests` (real `CryptographyClient`, `KeyVaultCryptographyClientFactory` built with `StaticTokenCredential` and `CreateOptions()` plus `Transport = new HttpClientTransport(new HttpClient(handler))`)

- `CreateDataKeyAsync_ReadableKeyTwice_ReadsKeyOnceAndWrapsLocally` (verified: one authorised GET of the key, 0 `wrapKey` requests)
- `CreateDataKeyAsync_KeyReadForbidden_SendsOneWrapKeyRequestPerCall` (verified)
- `OpenDataKeyAsync_Twice_ReadsKeyOnceAndSendsOneUnwrapKeyRequestPerCall` (the factory caches the versioned client)
- `KeyVaultRequests_WrapAndUnwrap_UseRsaOaep256OnTheWire` (Security; forbidden key read, so wrap is remote)
- `KeyVaultRequests_AnyOperation_GoOnlyToConfiguredVaultHost` (Security)
- `KeyVaultRequests_VerboseSdkLogging_NeverContainDataKeyWrappedKeyOrToken` (Security). Use `AzureEventSourceListener` at Verbose. No captured message contains any `RequestValue` or `ResponseValue` recorded by the handler, or `test-access-token` (verified).
- `RoundTrip_RealSdk_DecryptsEnvelope`

### 8.10 `EncryptionSettingsGuardTests` (Security) and `EncryptionOptionsValidatorTests`

Use `HostEnvironments.Named(...)`.

- `ResolveProvider_DevelopmentWithKeyMaterial_ReturnsDevelopment`
- `ResolveProvider_DevelopmentWithoutKeyMaterial_ThrowsKeyMaterialMissing`
- `ResolveProvider_DevelopmentWithShortKeyMaterial_ThrowsWithoutEchoingValue` (31 characters; 31 characters padded with spaces)
- `ResolveProvider_DevelopmentWithExplicitKeyVaultAndValidKeyId_ReturnsKeyVault`
- `ResolveProvider_DevelopmentWithExplicitKeyVaultAndNoKeyId_ThrowsKeyIdInvalid`
- `ResolveProvider_NonDevelopmentWithValidKeyId_ReturnsKeyVault` (Theory: `Production`, `Staging`, `Test`)
- `ResolveProvider_NonDevelopmentWithExplicitDevelopmentProvider_Throws` (Theory: `Production`, `Staging`, `Test`, `Dev`)
- `ResolveProvider_NonDevelopmentWithDevelopmentKeyMaterial_ThrowsWithoutEchoingValue` (Theory: `Production`, `Staging`)
- `ResolveProvider_NonDevelopmentWithoutKeyId_Throws`
- `ResolveProvider_NonDevelopmentWithInvalidKeyId_ThrowsWithoutEchoingValue` (Theory: the invalid rows from 8.6)
- `ResolveProvider_UndefinedProviderValue_Throws` (Theory: `(KeyWrapProviderKind)2` and `(KeyWrapProviderKind)(-1)`, in both environments; message `UnknownProvider`)
- `TryResolveProvider_InvalidSettings_ReturnsFalseWithSameMessageAsResolve`
- `ResolveProvider_NullArguments_ThrowArgumentNullException`
- `Validate_ValidDevelopmentSettings_Succeeds`
- `Validate_ValidProductionSettings_Succeeds`
- `Validate_NonDevelopmentWithDevelopmentProvider_Fails` (the failure message equals `DevelopmentProviderNotAllowed`)
- `EncryptionOptions_ToString_ContainsNoKeyMaterial`

### 8.11 `EncryptionRegistrationTests` and `KeyWrapProviderStartupCheckTests`

Use `WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = ... })`, then `builder.Configuration.AddInMemoryCollection(...)`, then `builder.AddEncryption()`.

- `AddEncryption_Development_RegistersDevelopmentProviderAndSingletonEncryptor`
- `AddEncryption_Production_RegistersKeyVaultProvider` (resolving the provider makes no network call)
- `AddEncryption_ProductionWithDevelopmentProvider_ThrowsDuringRegistration` (Security)
- `AddEncryption_ProviderConfiguredAsUndefinedNumber_Throws` (Security; `"7"`)
- `AddEncryption_ProviderConfiguredAsNumberOneInProduction_Throws` (Security; `"1"` binds to Development)
- `AddEncryption_Registered_AddsOptionsValidatorAndStartupCheck`
- `AddInfrastructure_Development_ResolvesEnvelopeEncryptor` (with the Azurite well-known connection string and key material)
- `StartAsync_DevelopmentProvider_LogsWarning` (`CollectingLogger`, exact text)
- `StartAsync_OtherProvider_LogsInformationWithProviderName`
- `StartAndStop_Always_MakeNoWrapOrUnwrapCalls`

### 8.12 Integration tests

`ApiTests/EncryptionStartupTests.cs` (`[Collection(nameof(StorageTestGroup))]`, derives from `BaseIntegrationTest`):

- `Startup_Development_ResolvesDevelopmentKeyWrapProvider`
- `Startup_Development_ResolvesEnvelopeEncryptorAsSingleton`
- `EnvelopeEncryptor_DevelopmentHostRoundTrip_MakesNoStorageCalls` (`StorageRequests.Count == 0`)
- `EnvelopeEncryptor_DevelopmentHostRoundTrip_LogsNoPlaintextKeyMaterialOrEnvelope` (Security; marker `SV-PLAINTEXT-5c1e`, `Factory.DevelopmentKeyMaterial`, and the envelope's base64 and hex, all at Debug level)
- `EnvelopeEncryptor_DevelopmentHostTamperedEnvelope_LogsWarningWithoutData` (Security)
- `Startup_DevelopmentHost_LogsDevelopmentProviderWarning`: call `Logs.Clear()` first, then create a derived factory with `Factory.WithWebHostBuilder(_ => { })` and call `CreateClient()`. Verified: asserting on the shared factory is order-dependent, because other tests clear the logs.
- `Startup_DevelopmentWithoutKeyMaterial_FailsWithoutEchoingConfiguration` (Security; derived factory with an empty value; message `DevelopmentKeyMaterialMissing`)
- `Startup_DevelopmentWithShortKeyMaterial_FailsWithoutEchoingIt` (Security; a 31-character marker)
- `Startup_DevelopmentWithExplicitKeyVaultProvider_ResolvesKeyVaultKeyWrapProvider` (derived factory: `Encryption:Provider=KeyVault` and a key id)

`ApiTests/ProductionEncryptionStartupTests.cs` (`[Collection(nameof(ProductionTestGroup))]`, Security):

- `Startup_Production_ResolvesKeyVaultKeyWrapProvider` (verified to start without a network call)
- `Startup_Production_LogsKeyVaultProviderInUse` (derived factory after `Logs.Clear()`)
- `Startup_ProductionWithDevelopmentProvider_FailsWithoutEchoingKeyMaterial` (message `DevelopmentProviderNotAllowed`)
- `Startup_ProductionWithDevelopmentKeyMaterial_FailsWithoutEchoingIt` (marker `SV-MARKER-...`, 40 characters; message `DevelopmentKeyMaterialNotAllowed`; verified)
- `Startup_StagingWithDevelopmentProvider_Fails`
- `Startup_ProductionWithoutKeyVaultKeyId_Fails` (verified)
- `Startup_ProductionWithInvalidKeyVaultKeyId_FailsWithoutEchoingIt` (Theory: versioned, http, `?sig=abc123`, `https://user:hunter2@...`, `/secrets/`)
- `Startup_ProductionWithProviderAsNumberOne_Fails`

### 8.13 Architecture and repository tests (Security)

In `Architecture/LayeringTests.cs`, add:

- `ProductionAssemblies_KeyWrapProviders_AreOnlyKeyVaultAndDevelopment`: the concrete types that implement `IKeyWrapProvider` across the production assemblies are exactly `KeyVaultKeyWrapProvider` and `DevelopmentKeyWrapProvider`.
- `SourceFiles_Src_DoNotUseSystemRandom`: no `.cs` file under `src/` (excluding `bin` and `obj`) contains `new Random(` or `Random.Shared`.

In `Architecture/RepositoryConfigurationTests.cs`, add `"KeyMaterial"` to `ForbiddenFragments`.

### 8.14 AppHost tests (`tests/SharpVault.AppHost.Tests/AppHostModelTests.cs`)

- `CreateBuilderAsync()` becomes `DistributedApplicationTestingBuilder.CreateAsync<Projects.SharpVault_AppHost>([$"--Parameters:{AppHostResourceNames.DevelopmentKeyMaterial}={TestKeyMaterial}"], TestContext.Current.CancellationToken)`, where `TestKeyMaterial` is a `static readonly` random base64 string. Verified: no AppHost user secrets are written when the value is supplied.
- Replace `AppHost_Model_DeclaresNoParameterResources` with `AppHost_Model_DeclaresOnlyTheDevelopmentKeyMaterialParameter`. It asserts:
  - exactly one `ParameterResource`
  - its name is `development-key-material`
  - `Secret` is true
  - `Default` is not null. It is Aspire's internal `UserSecretsParameterDefault`, verified; do not assert the type.
- Add `AppHost_PublishMode_DeclaresNoParameterResources`, with args `--operation publish --publisher manifest --output-path <unique temp directory>` (verified).
- Add `AppHost_ApiProject_ReceivesDevelopmentKeyMaterialParameter`. Run every `EnvironmentCallbackAnnotation` callback of the `api` resource with `new EnvironmentCallbackContext(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run), cancellationToken: ...)`, then `Assert.Same(parameter, context.EnvironmentVariables["Encryption__DevelopmentKeyMaterial"])`. Verified; do not use the obsolete `GetEnvironmentVariableValuesAsync`.
- Add `AppHost_ApiProject_SetsNoOtherEncryptionVariables`: exactly one key starting with `Encryption__`.
- Add `AppHost_WebProject_ReceivesNoEncryptionSettings`.
- Rename `AppHost_Source_ContainsNoParametersEnvironmentOverridesOrKeys` to `AppHost_Source_ContainsNoKeysOrConnectionStrings`. Remove its `AddParameter`, `WithEnvironment` and `secret:` rows, and add the rows `vault.azure.net` and `KeyVaultKeyId`.
- Add `AppHost_Source_GeneratesKeyMaterialAsPersistedSecretWithoutSpecialCharacters`: `AppHost.cs` contains `secret: true`, `persist: true`, `Special = false` and `IsRunMode`, and `AppHostResourceNames.DevelopmentKeyMaterialLength` is at least 64.

### 8.15 Performance tests (`Infrastructure/Encryption/EncryptionPerformanceTests.cs`)

`[Trait("Category", "Performance")]` and `[Collection(nameof(EncryptionPerformanceTestGroup))]`, with `[CollectionDefinition(nameof(EncryptionPerformanceTestGroup), DisableParallelization = true)] public sealed class EncryptionPerformanceTestGroup;` in the same folder. Warm up once, then measure with `Stopwatch.GetTimestamp` and `Stopwatch.GetElapsedTime`, and allocations with `GC.GetAllocatedBytesForCurrentThread()`.

- `Encrypt_OneKibibyteTenThousandTimes_AveragesUnder50Microseconds`
- `Decrypt_OneKibibyteTenThousandTimes_AveragesUnder50Microseconds`
- `ReEncrypt_OneKibibyteTenThousandTimes_AveragesUnder100Microseconds`
- `EncryptAndDecrypt_MaximumSizeThousandTimes_AverageUnder500Microseconds`
- `CreateDataKeyAsync_DevelopmentProviderThousandTimes_AveragesUnder200Microseconds`
- `OpenDataKeyAsync_RealSdkWithInProcessTransport_AveragesUnder20Milliseconds` (20 calls after warm-up)
- `Encrypt_OneKibibyte_AllocatesOnlyTheEnvelope` (allocated bytes at most `envelope.Length + 64`; measured 1,080 for 1,053)
- `Decrypt_OneKibibyte_AllocatesOnlyTheString` (at most `2 * 1024 + 64`; measured 2,072)
- `ReEncrypt_OneKibibyte_AllocatesOnlyTheNewEnvelope` (at most `envelope.Length + 64`)

### 8.16 Authorization matrix

This work adds no endpoint, so the HTTP authorization matrix does not apply. The existing foundation matrix (`AuthorizationTests`, `ProductionHostingTests`) must still pass unchanged. That proves the encryption registration opened no route. In particular, `Endpoints_ProductionEnvironment_OnlyHealthEndpointsAreAnonymous` still lists only `/alive` and `/health`.

### 8.17 Commands the reviewer runs

```bash
docker info
dotnet build SharpVault.slnx
dotnet test SharpVault.slnx --filter "Category!=Performance"
dotnet test SharpVault.slnx --filter "Category=Security"
dotnet test SharpVault.slnx --filter "Category=Performance"
dotnet test SharpVault.slnx -c Release --filter "Category!=Performance" --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults
dotnet tool restore
dotnet reportgenerator "-reports:TestResults/**/coverage.cobertura.xml" "-targetdir:TestResults/coverage" "-reporttypes:Cobertura"
dotnet run eng/CoverageGate.cs -- TestResults/coverage/Cobertura.xml SharpVault.Application=80 SharpVault.Infrastructure=80 SharpVault.Domain.Encryption=95 SharpVault.Infrastructure.Encryption=95
dotnet list SharpVault.slnx package --vulnerable --include-transitive
dotnet run -c Release --project tests/SharpVault.Benchmarks -- --filter '*' --job short
```

## 9. Performance budgets

### 9.1 Budgets

The reference column is a BenchmarkDotNet `--job short` run of the prototype in this sandbox (Release, x64 Linux). The budgets leave a margin of 15 times or more, so they catch regressions without being flaky.

| Operation | Budget (test in 8.15) | Reference | Key Vault calls | Table Storage calls | Allocation |
|---|---|---|---|---|---|
| `Encrypt`, 32 B | n/a | 2.3 µs | 0 | 0 | 88 B |
| `Encrypt`, 1 KiB | average under 50 µs | 3.2 µs | 0 | 0 | the envelope only (1,080 B) |
| `Decrypt`, 1 KiB | average under 50 µs | 1.7 µs | 0 | 0 | the result string only (2,072 B) |
| `ReEncrypt`, 1 KiB | average under 100 µs | measured by `EnvelopeEncryptionBenchmarks` | 0 | 0 | the new envelope only |
| `Encrypt` and `Decrypt`, 32 KiB | average under 500 µs together | 11 µs and 14 µs | 0 | 0 | 32,824 B and 65,560 B |
| `CreateDataKeyAsync`, development | average under 200 µs | about 10 µs | n/a | 0 | small |
| `OpenDataKeyAsync` then `Decrypt` 1 KiB, development | n/a | 6.1 µs | n/a | 0 | 2,624 B |
| `CreateDataKeyAsync`, Key Vault | network bound | n/a | at most 1 `wrapKey`; 0 once the SDK has read the public key (one read per client per process) | 0 | n/a |
| `OpenDataKeyAsync`, Key Vault | under 20 ms of our own and SDK overhead (in-process transport) | n/a | exactly 1 `unwrapKey`, plus 1 key read on first use of each key version per process | 0 | n/a |
| `RewrapDataKeyAsync`, Key Vault | network bound | n/a | 1 `unwrapKey` plus at most 1 `wrapKey` | 0 | n/a |
| Any session call, or `Dispose` | as above | as above | 0 | 0 | as above |
| API startup | n/a | n/a | 0 | 0 (no new tables) | n/a |
| 100,000 nonce uniqueness check | test finishes in under 5 s | 0.3 s | 0 | 0 | n/a |

- **Downstream budgets**: reveal (skill default: under 200 ms) is dominated by one Key Vault `unwrapKey`. That is a network round trip, typically tens of milliseconds in region (an estimate; no vault was available here). The AES work is microseconds. Transfer of 1,000 secrets (under 60 s) costs 1,000 unwraps and 0 wraps with per-secret keys (D16).
- **Queries without a partition key**: none. This work makes no Table Storage call, and `EnvelopeEncryptor_DevelopmentHostRoundTrip_MakesNoStorageCalls` asserts it.

### 9.2 Benchmarks (`tests/SharpVault.Benchmarks`, T14)

- `Support/BenchmarkHostEnvironment.cs`: `internal sealed class` implementing `IHostEnvironment` with Development, `ApplicationName` `SharpVault.Benchmarks`, `AppContext.BaseDirectory` and a `NullFileProvider`.
- `Support/BenchmarkEncryption.cs`: `internal static class` with `CreateDevelopmentEncryptor()`, which returns an `EnvelopeEncryptor` over a `DevelopmentKeyWrapProvider` with 48-character random material, and `NullLogger<EnvelopeEncryptor>.Instance`.
- `Encryption/EnvelopeEncryptionBenchmarks.cs`: `[MemoryDiagnoser] public class` (**not sealed**: BenchmarkDotNet rejects sealed benchmark classes, verified) with `[Params(32, 1024, 32768)] PlaintextBytes`. It has an `async Task` `[GlobalSetup]`, a `[GlobalCleanup]` that disposes the session, and the benchmarks `Encrypt`, `Decrypt` and `ReEncrypt`. Hold the session in `IDataKeySession? _session` behind a `Session` property that throws `InvalidOperationException` when it is null, so no `!` is needed.
- `Encryption/DataKeyBenchmarks.cs`: `[MemoryDiagnoser] public class` with `CreateDataKey`, `OpenDataKey`, `RewrapDataKey` and `OpenAndDecryptOneKibibyte`, all on the development provider. The Key Vault paths are network-bound and covered by the call count tests.
- A pull request that touches these hot paths pastes the before and after `--job short` tables into its description.

## 10. Risks and decisions

| # | Topic | Decision and reason |
|---|---|---|
| D1 | API shape | **A session API** (`CreateDataKeyAsync` or `OpenDataKeyAsync` returns an `IDataKeySession`), not one call per value. One Key Vault unwrap then serves every value under that data key for one request, and the key is zeroed when the session ends, as the skill requires ("cache unwrapped data keys only for the duration of a request"). A call-per-value API would multiply Key Vault calls by the number of fields. |
| D2 | Envelope format | Version byte, 12-byte nonce, ciphertext, 16-byte tag (section 4.3). The nonce and tag are inside the envelope, so one binary property holds everything for a value. `0x01`-`0x7F` are reserved for server formats and `0x80`-`0xFF` for browser formats (OTL-3), so the two can never be confused. Future versions (other algorithms) get new numbers, and the decryptor dispatches on the byte. |
| D3 | Associated data | Version, purpose, item id and owner id, each length-prefixed (section 4.4). The item id and owner id are required by CLAUDE.md. The purpose stops a field swap within one secret (TH4). The version stops downgrade. Length prefixes keep the encoding unambiguous if id types change. Guids are written big-endian, so the bytes match the canonical string order on every platform. |
| D4 | Purpose labels | Lowercase dotted labels, at most 64 characters, defined as constants by the consuming feature. Suggested for vault-and-folders: `secret.name`, `secret.username`, `secret.value`, `secret.notes`, `secret.url`, `secret.tags`, `folder.name`. They are persisted format: never rename them. |
| D5 | Data key per field (recommendation for vault-and-folders) | With one data key per secret for every field, listing a page of 50 secrets (names are encrypted, SEC-2) needs 50 Key Vault unwraps per request. That threatens the 500 ms list budget, and Key Vault throttles crypto operations per vault per 10 seconds. **Recommended**: names, tags and folder names under one per-user "metadata" data key (one unwrap per list request), and username, value, notes and URL under a per-secret data key (one unwrap per reveal, which keeps CLAUDE.md's "each secret value has its own random data key"). The API here supports both. vault-and-folders makes the final decision and records it in its plan. |
| D5a | D5 and TRF-7 do not fit yet | `ReEncrypt` works only within one session, so source and target share one data key. Moving names, tags and folder names from the source user's metadata key (D5) to the target user's metadata key during a transfer (TRF-7) therefore cannot be done with ReEncrypt. The only path with the current API is `Decrypt` with the source session followed by `Encrypt` with the target session (D16), and that creates a plaintext string. The later plan therefore chooses between a cross-session re-encrypt member such as `byte[] ReEncrypt(EncryptionContext source, IDataKeySession targetSession, EncryptionContext target, byte[] envelope)`, keeping the fields that move in a transfer under per-secret data keys, or accepting the plaintext string. This branch does not add that member. `feature/vault-and-folders` (or `feature/offboarding-transfers`, whichever comes first) must decide and record the choice in its plan. |
| D6 | Wrapped key storage | Three properties: `DataKeyId`, `DataKeyAlgorithm` and `WrappedDataKey` (section 4.5), rather than one binary blob, so a future rewrap job can see versions without parsing. The version is inside `DataKeyId`, so a separate version column cannot drift out of step. |
| D7 | Key Vault key id | Configured **versionless**; stored **versioned**, exactly as Key Vault returns it. Every stored id is validated as a version of the configured vault and key before use (TH11). This needed an explicit `/keys/` path check, because `KeyVaultKeyIdentifier.TryCreate` accepts `/secrets/` and `/certificates/` (verified). The vault host is not restricted to `*.vault.azure.net`, so Managed HSM and sovereign clouds work, and configuration is trusted. |
| D8 | Rotation | When the identity may read the key, the SDK caches the public key per client and wraps locally (verified), so a new KEK version is used for new wraps only after the API restarts. That is acceptable and documented: rotate, restart, keep old versions enabled. A rewrap job using `RewrapDataKeyAsync` is a PROGRESS.md follow-up. Old versions must never be disabled or deleted while any wrapped key references them, or data becomes unreadable (the error surfaces as `RequestFailedException` 403 or 404, TH26). |
| D9 | Client and credential | `CryptographyClient` with its own `DefaultAzureCredential` instance (managed identity in Azure), RSA-OAEP-256 only, a 10 s network timeout, 3 retries, content logging off and challenge resource verification on. Clients are cached singletons (the skill says reuse `CryptographyClient`), with at most 64 cached. Required role: "Key Vault Crypto User" on the vault. It includes key read, which enables local wrap; without it, each wrap costs 1 remote call (verified). |
| D10 | Testing without a vault | No live Azure in CI. Two fakes: (1) `FakeKeyVault`, a `CryptographyClient` subclass with real in-memory RSA-OAEP-256 per version, for exact call counts, rotation and zeroing; (2) `FakeKeyVaultHttpHandler` behind the **real** SDK through `HttpClientTransport`, for challenge auth, local versus remote wrap, wire algorithm, host pinning and SDK logs. Both techniques were verified. A manual smoke test against a real vault (`Encryption:Provider=KeyVault` in Development with `az login`) is documented, not automated. |
| D11 | Development provider | AES-256-GCM wrap under a KEK derived with HKDF-SHA256 from `Encryption:DevelopmentKeyMaterial`, with the key id bound as associated data. The key id is a 16-hex fingerprint (from a separate HKDF label), so changed material shows as `KeyMismatch` rather than a mystery tag failure. Refusal layers: the registration guard, `ValidateOnStart`, the constructor guard forced at startup by `KeyWrapProviderStartupCheck`, and the architecture test. Plus a startup Warning whenever it runs. |
| D12 | Development key material source | **The AppHost generates a 64-character secret parameter with `persist: true`** (stored in the AppHost user secrets) and passes it to the API, only in run mode. `dotnet run --project src/SharpVault.AppHost` then works with no setup, and the persistent Azurite data stays readable across runs. This **intentionally changes two foundation AppHost tests** that forbade parameters and environment overrides. The replacement tests pin it down to exactly this one secret parameter, API only and run mode only (8.14). Alternatives rejected: a key in source (secrets in the repository), an ephemeral random key (persistent Azurite data becomes unreadable after a restart), and manual `dotnet user-secrets` on the Api (a setup step, and the Api has no `UserSecretsId`). Building the AppHost test model would itself persist a generated value, so the tests supply it through configuration (verified, TH30). |
| D13 | Configuration shape | Flat keys `Encryption:Provider`, `Encryption:KeyVaultKeyId` and `Encryption:DevelopmentKeyMaterial`, so environment variables stay simple (`Encryption__KeyVaultKeyId`). The guard runs during registration (fail fast, the same pattern as the Table Storage guard), and an `IValidateOptions` validator with `ValidateOnStart` keeps the coding standard. Numbers bound into the enum are checked with `Enum.IsDefined`. |
| D14 | `IKeyWrapProvider` location | Infrastructure, not Application. Only `EnvelopeEncryptor` uses it, and handlers must never wrap or unwrap directly. Handlers see `IEnvelopeEncryptor` and `IDataKeySession` (Application). |
| D15 | Domain contents | Only `WrappedDataKey`, so later entities can hold the encrypted form. Plaintext never has a Domain type. Envelopes are `byte[]`. |
| D16 | Re-encryption for transfers (TRF-7) | `ReEncrypt` keeps the same data key and wrapped key and only re-binds the associated data to the new owner with a fresh nonce: 1 unwrap and 0 wraps per data key, and plaintext only in a zeroed byte buffer, never a string. Reusing the data key is sound with GCM and fresh nonces. The source rows are deleted by the transfer job after it verifies the copies. A full key change is available by combining `CreateDataKeyAsync`, `Decrypt` and `Encrypt` if a later plan needs it. |
| D17 | Limits | 32 KiB plaintext per value (the envelope fits a 64 KiB binary property with room to spare), purposes of at most 64 characters, key ids of at most 512 characters, RSA-sized wrapped keys only, and at most 64 cached clients. Feature validators set tighter per-field limits in characters. |
| D18 | Memory hygiene and residual risk | Data keys live in pinned arrays (`GC.AllocateUninitializedArray(pinned: true)`) when we allocate them. Keys from the Key Vault SDK are not pinned, and the session takes ownership without copying, so a test can prove they are zeroed. Plaintext UTF-8 bytes live only inside the envelope (encrypted in place) or in a pooled buffer that is zeroed before it goes back. **Accepted residual risk**: .NET strings (inputs and decrypted results) cannot be zeroed, and the SDK's JSON request and response buffers hold base64 copies of the data key that we cannot clear. |
| D19 | Errors | `SecretDecryptionException` with a `Reason` and a constant message, a single constructor and no inner exception (CA1032 verified not to fire). The reason is for server logs and tests. API handlers must map every reason to the same generic response. Key Vault 400 on unwrap means AuthenticationFailed. Every other Key Vault failure propagates as `RequestFailedException`, whose message has no request body. Argument problems throw `ArgumentException` with constant messages. |
| D20 | Benchmarks project | Lives under `tests/` as the skill says, with `IsTestProject` false. The xUnit package `ItemGroup` in `tests/Directory.Build.props` is now conditional (verified). Benchmark classes are `public` and **unsealed**, because BenchmarkDotNet requires it (verified); the sealed architecture test covers only `src` assemblies. A `--job dry` smoke run is added to the main-branch performance job. |
| D21 | Coverage | The gate adds `SharpVault.Domain.Encryption=95` and `SharpVault.Infrastructure.Encryption=95` (the skill's 95% for encryption code). Verified: coverlet counts only real lines of the records, and the gate's prefix matching includes the `KeyVault` and `Development` sub-namespaces. Catch blocks that cannot happen (for example `AesGcm` with a valid key) are deliberately not written, so coverage stays honest. |
| D22 | Thread safety | Sessions are not thread-safe (one `AesGcm` per session) and are documented that way. Both providers and the encryptor are safe as singletons. |
| D23 | Platform | No `AesGcm.IsSupported` check. Linux, Windows and macOS servers support it, and MAUI never runs this code. |
| D24 | SEC-5 and rollback | Each history entry must use its own id as `ItemId`, so a history envelope cannot be swapped in as the current value. Restoring an older copy of the *same* row is not detectable with AES-GCM alone. It is covered by storage access control (managed identity only), and vault-and-folders must consider a version counter in its entity if this matters. |
| D25 | OTL-3 | One-time link contents are encrypted in the browser with a fragment key (Web Crypto AES-GCM, 12-byte IV, ciphertext followed by the tag, which is the same shape as section 4.3). The server stores them as opaque bytes and only checks their size. The server core never receives a link key, never decrypts link payloads, and rejects version bytes `0x80`-`0xFF`, which one-time-links uses for its format. The snapshot (OTL-5) comes from an audited reveal on the creator's side. |
| D26 | Logging | `DataKeySession` logs a Warning with the item id, purpose and reason on every decryption failure (security-relevant). `EnvelopeEncryptor` logs the reason when a data key cannot be opened. No log carries owner ids, key ids, bytes or values. Azure SDK logs keep their default levels; Aspire forwards them, and content logging is off. |
| D27 | Key Vault throttling figures | learn.microsoft.com is not reachable from this session, so the plan states the limits qualitatively (per vault per 10 seconds, lower for HSM-backed and larger keys). It must be allowed in the session's network settings to confirm the current figures. D5 does not depend on the exact numbers. |
| D28 | Pull request base | The branch sits on the unmerged `feature/solution-foundation`. Open the pull request after the foundation merges (rebase onto `main`), or as a stacked pull request against `feature/solution-foundation`. The foundation's docs task had not landed at this commit, so T15 puts everything in the new `docs/ENCRYPTION.md` and only links from `LOCAL-DEV.md` and `TESTING.md` if they exist, to avoid conflicts. |
| D29 | Analyzer notes for implementers | Seen while verifying: CA1822 fires on test fake members that use no instance data (make them static). CA1819 and CA1032 do not fire for the shapes in this plan. If any other analyzer fires because of a test convention, report it to the planner instead of suppressing it. |

## 11. Definition of done for this branch

- T1 to T15 are complete, and `dotnet build SharpVault.slnx` reports 0 warnings and 0 errors.
- Every test in section 8 exists and passes, including the Security and Performance categories. Every threat in section 6 has its passing test.
- The coverage gate passes with Application and Infrastructure at 80% or more and both encryption namespaces at 95% or more.
- `dotnet list SharpVault.slnx package --vulnerable --include-transitive` reports no vulnerable packages.
- The benchmarks run (`--job short`), and the results are pasted into the pull request description.
- No inline comments, commented-out code or TODOs, and the AGPL header is on every new `.cs` file.
- No key material, key id or connection string appears in any `appsettings*.json` or `launchSettings.json`.
- The reviewer has approved. The pull request title is `feat: encryption core`, and it lists the requirement ids from section 2.
