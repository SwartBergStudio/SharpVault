# Plan: Entra sign-in

- **Branch**: `feature/entra-sign-in` (already created from `feature/solution-foundation`, which is pull request SwartBergStudio/SharpVault#4 and not merged yet)
- **PROGRESS.md item**: Next, item 3
- **Not included**: `feature/encryption-core` (stacked separately, under review). Nothing in this plan depends on it (decision D9).
- **Planner verification**: every package version and every item marked "verified" was restored, built or run on .NET SDK 10.0.114 (Linux) in throwaway projects before this plan was written. The implementer can rely on those items.

## 1. Goal

Sign users in with Microsoft Entra ID on the API and the Web, and enforce the tenancy, role and lock rules on every API request:

- **API**: Microsoft.Identity.Web JWT bearer validation replaces the placeholder scheme. The API accepts only v2 access tokens for its own audience, issued by the configured tenant, carrying the `access_as_user` delegated scope. A `tid` check rejects any other tenant.
- **Authorization**: the `SharpVault.User` and `SharpVault.Admin` app roles map to two policies. The fallback and default policies require the User policy. Guests are rejected by default, with a settings hook for ADM-1. Every authorized request runs a lock check against the `Users` table, behind a small bounded cache.
- **Current user**: `ICurrentUser` reads the `oid` claim. Email is never used as identity.
- **Users table**: on sign-in a user row is created or updated (oid, display name, email, status, created, last sign-in) with ETag concurrency. The last sign-in write is throttled, so ordinary requests never write.
- **Audit**: a minimal append-only `AuditLog` table and `IAuditLog` writer. Sign-in is the first audited event (AUD-2).
- **Endpoints**: `GET /api/me` (User policy) and `GET /api/admin/users` (Admin policy, paged, status and last sign-in, groundwork for VIS-3).
- **Web**: OpenID Connect with code flow and PKCE through Microsoft.Identity.Web, a hardened session cookie, server-side token acquisition with an encrypted token cache, and a typed `HttpClient` that calls the API. Adds sign-in and sign-out endpoints, an account menu and these pages: account, access denied, signed out and sign-in error.
- **Tests**: locally signed JWTs validated by the real JwtBearer and Microsoft.Identity.Web pipeline, the full authorization matrix, threat tests, concurrency tests and performance budgets with storage call counts.

### Out of scope

- Locking and unlocking commands, reasons, scheduled locks, session revocation and admin UI (`feature/user-lock`, LCK-2 to LCK-6). This feature only reads the lock status and offers the cache invalidation hook that user-lock will call.
- Storing the guest setting and an admin settings UI (`feature/admin-settings`, ADM-1). This feature adds the `IGuestAccessPolicy` hook, backed by configuration.
- Audit search, filters, CSV export and user history (`feature/audit-log`, AUD-4 and AUD-5).
- Secret and share counts per user in the admin list (VIS-3 counts come with vault-and-folders and sharing).
- MAUI sign-in with MSAL (`feature/maui-app`).
- Microsoft Graph directory search (`feature/sharing`, SHR-1).
- Deployment: managed identity federated credential, Data Protection key storage, forwarded headers and HTTPS ingress (deployment feature).
- Playwright E2E tests. There is still no interactive UI (foundation D23). Web behaviour is tested over HTTP with `WebApplicationFactory`.
- Benchmarks. The Benchmarks project lives on `feature/encryption-core` (decision D27).

## 2. Requirements

| Id | Delivered by this work |
|---|---|
| TEN-1 | `AzureAd:TenantId` must be a GUID (rejects `common`, `organizations`, `consumers` and domain names), and the authority and issuer are tenant-specific. Checked at startup in the API and the Web. |
| TEN-2 | The issuer must be the configured tenant's v2 issuer, and the `tid` claim must equal the configured tenant. Microsoft.Identity.Web's issuer validator does **not** check `tid` (verified), so the API checks it explicitly. The Web checks `tid` on the ID token. |
| TEN-3 | Guests are rejected with 403 by default. Detection uses the `acct` claim plus an `idp` cross-check (D5). The `IGuestAccessPolicy` hook is backed by `Tenancy:AllowGuests` (default `false`) until ADM-1 stores it. |
| TEN-4 | The `SharpVault.User` or `SharpVault.Admin` role is required on every API request, including the fallback policy. Admin endpoints require `SharpVault.Admin`. |
| TEN-5 | A `Users` table row (oid, display name, email, status, created, last sign-in) is created on first use and updated on each sign-in. |
| LCK-1 | The lock status is checked on every authorized API request, with at most 5 seconds of staleness (configurable, 0 disables the cache) and immediate invalidation on the instance that writes a lock (D8). A locked user with a valid token gets 403. |
| AUD-2 (sign-in only) | A `SignIn` audit entry is written once per sign-in interval per user (D10). |
| AUD-1, AUD-3 (groundwork) | The append-only `IAuditLog` (one `AppendAsync` method, insert only) and the entry shape: ids, actor, target, time, IP and reason, never secret values, tokens, names or emails. |
| VIS-2, VIS-3 (groundwork) | The admin user list returns only directory data, status and last sign-in. `IsAdmin` never grants data access. |

Pull requests and tests reference these ids.

## 3. Verified versions and facts

### 3.1 Packages (all stable on nuget.org on 2026-10-31)

| Package | Version | Used by | Why |
|---|---|---|---|
| `Microsoft.Identity.Web` | 4.16.1 | Api, Web | JWT bearer for the API; OIDC, token acquisition and token cache for the Web |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | 10.0.14 | transitive pin | Microsoft.Identity.Web asks for 10.0.0. The pin keeps it in step with JwtBearer 10.0.14 |
| `Microsoft.AspNetCore.Components.Authorization` | 10.0.14 | UI.Shared | `AuthorizeView`, `[Authorize]`, `AuthenticationStateProvider` (MAUI compatible, no framework reference) |
| `Microsoft.Extensions.Caching.Memory` | 10.0.14 | Infrastructure | Bounded user access cache |
| `Microsoft.Extensions.TimeProvider.Testing` | 10.11.0 | UnitTests, IntegrationTests | `FakeTimeProvider` |
| `System.Security.Cryptography.Pkcs` | 10.0.14 | transitive pin | **Required**: Microsoft.Identity.Web.TokenCache 4.16.1 pulls 10.0.10, which has moderate and high advisories (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx). Without the pin, restore fails with NU1902 and NU1903 as errors (verified) |
| `System.Security.Cryptography.Xml` | 10.0.14 | transitive pin | **Required**: the same package and `Microsoft.AspNetCore.DataProtection` 10.0.7 pull 10.0.10, which has high advisories (GHSA-7hqp-7c5m-4vq6, GHSA-c399-9853-6jmj, GHSA-r955-8q5j-3h83). Without the pin, the test projects fail restore with NU1903 (verified) |
| `NSubstitute` | 6.2.0 (already central) | IntegrationTests (new reference) | Fake `ITokenAcquisition` |

With these additions in a copy of this branch, `dotnet build SharpVault.slnx` reported **0 warnings and 0 errors**, and `dotnet list SharpVault.slnx package --vulnerable --include-transitive` reported no vulnerable packages in any project (verified).

### 3.2 Verified behaviour (throwaway projects in the planner scratchpad)

- **V1**: `AddMicrosoftIdentityWebApi(jwt => ..., identity => section.Bind(identity))` with a `PostConfigure<JwtBearerOptions>("Bearer", ...)` that sets both `Configuration` and `ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config)` (namespace `Microsoft.IdentityModel.Protocols`) validates locally signed RS256 tokens with **no network call**.
- **V2**: Microsoft.Identity.Web's `AadIssuerValidator` fetches issuer metadata from the network unless `TokenValidationParameters.ValidIssuers` is set. With `ValidIssuers = ["https://login.microsoftonline.com/{tenant}/v2.0"]` it accepts that issuer offline and rejects other tenants' issuers (IDX40001).
- **V3**: the issuer validator does **not** compare the `tid` claim. A token with our issuer and a foreign `tid` was **accepted**, and so was a token with no `tid`. An `OnTokenValidated` handler set in the `jwt => ...` callback runs (it chains with Microsoft.Identity.Web), and `context.Fail(...)` there returns 401.
- **V4**: with `MapInboundClaims = false`, `NameClaimType = "name"` and `RoleClaimType = "roles"`, the principal has the raw claim types (`oid`, `tid`, `roles`, `scp`, `acct`) and `IsInRole("SharpVault.Admin")` is true only for a `roles` claim. A `role` claim or the `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` claim does **not** satisfy `IsInRole`.
- **V5**: rejected with 401: alg `none`, unknown RSA key with the same `kid`, HS256 with a random key, missing `exp`, expired, `nbf` in the future, wrong audience, and `aud` = `api://{clientId}` on a `ver` 2.0 token (Microsoft.Identity.Web accepts the bare client id for v2 tokens). A token expired 3 minutes ago was **accepted** under the default 5-minute clock skew, so the skew must be set explicitly (D2).
- **V6**: the Web OIDC challenge with a static `Configuration` and `StaticConfigurationManager` in `PostConfigure<OpenIdConnectOptions>("OpenIdConnect", ...)` returns 302 to `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize` with `client_id`, `redirect_uri=https://localhost/signin-oidc`, `response_type=code`, the API scope, `code_challenge` and `code_challenge_method=S256`, `state` and `nonce`. It sets nonce and correlation cookies with `secure; samesite=none; httponly` on path `/signin-oidc`. Setting only `Configuration` is **not** enough: the handler then tries the network and throws IDX20803.
- **V7**: Microsoft.Identity.Web Web defaults are `MapInboundClaims=true`, `NameClaimType=preferred_username`, `RoleClaimType=` the long URI, cookie `ExpireTimeSpan` 14 days sliding and `SecurePolicy=SameAsRequest`. A `PostConfigure<OpenIdConnectOptions>` registered after `AddMicrosoftIdentityWebApp` changes them (verified for claim mapping). `ResponseType=code`, `UsePkce=true` and `SaveTokens=false` are the defaults. `Events.OnTokenValidated` is already set by Microsoft.Identity.Web, so our handler must chain it.
- **V8**: a chained `OnRemoteFailure` that redirects to `/account/sign-in-error` and calls `HandleResponse()` turns both a forged callback (`state=garbage`) and an `error=access_denied` callback into a 302 to `/account/sign-in-error`.
- **V9**: `RejectSessionCookieWhenAccountNotInCacheEvents` does **not** exist in Microsoft.Identity.Web 4.16.1 (it is a documentation sample). It is not used (D13).
- **V10**: a minimal API `POST` with a `[FromForm]` parameter is validated by `UseAntiforgery` automatically: 400 without a token, 200 with one. A `POST` with **no** form parameter and `.WithMetadata(new RequireAntiforgeryTokenAttribute())` was **not** validated: it returned 200 without a token. So sign-out validates explicitly with `IAntiforgery.IsRequestValidAsync` (D16).
- **V11**: SwartBerg.Mediator 4.0.0 registers `IMediator` and handlers as **transient**, so handlers resolved through a scoped `IMediator` receive scoped services from the same request scope. An open generic `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(X<,>))` runs around handlers.
- **V12**: `MsalDistributedTokenCacheAdapterOptions.Encrypt` exists. `ITokenAcquisition.GetAccessTokenForUserAsync` has two overloads. The six-parameter one is `(IEnumerable<string> scopes, string? authenticationScheme, string? tenantId, string? userFlow, ClaimsPrincipal? user, TokenAcquisitionOptions? tokenAcquisitionOptions)`.

## 4. Design

### 4.1 Request flow (API)

```
Authorization: Bearer <jwt>
  -> JwtBearer (Microsoft.Identity.Web): signature RS256, issuer (tenant v2), audience, lifetime (2 min skew)
  -> OnTokenValidated: AccessTokenValidation   tid == tenant, exactly one valid oid, scp has access_as_user, idtyp != app
        any failure -> 401 (bare "Bearer" challenge, identical ProblemDetails)
  -> AuthorizationMiddleware with policy SharpVaultUser / SharpVaultAdmin (also fallback and default)
        SharpVaultAccessHandler:
          1. read identity (oid, name, email, account kind, roles)        no storage
          2. guest and guests not allowed  -> 403                          no storage
          3. no accepted role              -> 403                          no storage
          4. IUserAccessGate.CheckAsync    -> Locked -> 403                0 to 3 storage calls (section 8)
             (memoized per request in HttpContext.Items)
  -> endpoint -> IMediator -> handler (ICurrentUser from oid)
```

Every 403 is the same ProblemDetails response whatever the reason. Reasons are only logged, as ids and an enum value.

### 4.2 Where each type lives

| Layer | Path | Types |
|---|---|---|
| Domain | `Users/` | `User`, `UserStatus` |
| Domain | `Audit/` | `AuditEntry`, `AuditEventType` |
| Application | `Common/Interfaces/` | `ICurrentUser`, `IUserStore`, `IAuditLog`, `IUserAccessCache`, `IGuestAccessPolicy` |
| Application | `Common/Models/` | `Result<T>`, `Versioned<T>`, `StoreWriteResult`, `UserListPage`, `CachedUserAccess` |
| Application | `Common/Behaviors/` | `ValidationBehavior<TRequest, TResponse>` |
| Application | `Features/Users/Access/` | `IUserAccessGate`, `UserAccessGate`, `SignedInUser`, `UserAccessDecision`, `UserAccessOptions`, `UserAccessOptionsValidator`, `UserAccessMessages`, `UserProfileText` |
| Application | `Features/Users/Queries/` | `GetCurrentUser.cs` (query and handler), `ListUsers.cs` (query, validator and handler) |
| Application | `Features/Users/Dtos/` | `CurrentUserDto`, `UserSummaryDto`, `UserPageDto` |
| Application | `Features/Tenancy/` | `TenancyOptions`, `ConfiguredGuestAccessPolicy` |
| Application | `Mapping/` | `UserMappingRegister` |
| Infrastructure | `Storage/` | `TableNames` (add `Users`, `AuditLog`) |
| Infrastructure | `Users/` | `UserTableEntity` (internal), `UserKeys`, `UserStatusText`, `TableUserStore`, `UserAccessCache`, `UserStorageMappingRegister` |
| Infrastructure | `Audit/` | `AuditLogTableEntity` (internal), `AuditKeys`, `TableAuditLog`, `AuditStorageMappingRegister` |
| ServiceDefaults | `Identity/` | `EntraApplicationSettings`, `EntraSettingsMessages`, `EntraClaimTypes`, `AppRoles`, `EntraTokenChecks`, `TokenCheckResult`, `AccountClassifier`, `AccountKind` |
| ServiceDefaults | `Security/` | `SecurityHeadersPolicy.ForWeb(Uri)` change, `SecurityHeaderValues` change |
| Api | `Security/` | `ApiAuthenticationExtensions` (rewritten), `AccessTokenValidation`, `Policies`, `ApiScopes`, `SignedInIdentity`, `SignedInIdentityReader`, `SharpVaultAccessRequirement`, `SharpVaultAccessHandler`, `AccessDenialReason`, `HttpContextCurrentUser` |
| Api | `Errors/` | `ValidationExceptionHandler`, `ResultHttpExtensions` |
| Api | `Endpoints/` | `MeEndpoints`, `AdminUserEndpoints` |
| UI.Shared | `Models/` | `CurrentUserProfile` |
| UI.Shared | `Services/` | `ISharpVaultApiClient`, `ApiResponse<T>`, `ApiCallStatus` |
| UI.Shared | `Navigation/` | `AccountRoutes` |
| UI.Shared | `Layout/`, `Pages/` | `AccountMenu.razor`, `Account.razor`, `AccessDenied.razor`, `SignedOut.razor`, `SignInError.razor`, and changes to `MainLayout`, `Home`, `NotFound` |
| Web | `Security/` | `WebAuthenticationExtensions`, `WebCredentialGuard`, `WebCredentialMessages`, `ReturnUrls`, `WebTokenValidation` |
| Web | `Account/` | `AccountEndpoints` |
| Web | `Api/` | `SharpVaultApiOptions`, `SharpVaultApiOptionsValidator`, `SharpVaultApiClient`, `SharpVaultApiRegistration` |

Foundation conventions that apply to every file:

- The AGPL header from `.editorconfig` on every `.cs` file.
- No comments, `#region`, TODOs or commented-out code. Razor, CSS and JS files carry no comments.
- Classes are `sealed`, abstract or static. `ProductionAssemblies_ConcreteClasses_AreSealed` enforces this.
- Namespaces match the folder path and start with the assembly name.
- One public type per file, except the feature files that colocate a request, its validator and its handler.
- Every log goes through the `[LoggerMessage]` source generator (CA1848). Message templates never contain tokens, names, emails or request bodies.
- No type name ends in `Collection` (CA1711, foundation D16).
- Enums whose default value could grant access put the **fail-closed** value at 0: `UserStatus.Locked = 0`, `AccountKind.Unknown = 0`, `UserAccessDecision.Denied = 0`, `TokenCheckResult.Invalid = 0` and `ApiCallStatus.Failed = 0`.

### 4.3 Configuration

| Key | Host | Where it comes from | Default or rule |
|---|---|---|---|
| `AzureAd:Instance` | Api, Web | `appsettings.json` | `https://login.microsoftonline.com/`. Must be absolute https, path `/`, no query or user info |
| `AzureAd:TenantId` | Api, Web | user-secrets locally, environment in Azure | GUID only (TEN-1) |
| `AzureAd:ClientId` | Api, Web | user-secrets or environment | GUID. The API's own client id on the Api, the Web registration's id on the Web |
| `AzureAd:CallbackPath`, `AzureAd:SignedOutCallbackPath` | Web | `appsettings.json` | `/signin-oidc`, `/signout-callback-oidc` |
| `AzureAd:ClientCredentials` | Web | environment in Azure (managed identity federated credential). User-secrets with a client secret only in Development | Outside Development: at least one credential, never `ClientSecret` (D18) |
| `SharpVaultApi:BaseAddress` | Web | `appsettings.json` | `https+http://api` (Aspire service discovery) |
| `SharpVaultApi:Scopes` | Web | user-secrets or environment | One or more scopes, normally `api://{api-client-id}/access_as_user` |
| `UserAccess:CacheTtl` | Api | optional | `00:00:05`, between 0 and 30 s. 0 disables the cache |
| `UserAccess:CacheMaxEntries` | Api | optional | 10,000, between 100 and 1,000,000 |
| `UserAccess:SignInRecordInterval` | Api | optional | `01:00:00`, between 1 minute and 24 hours |
| `Tenancy:AllowGuests` | Api | optional | `false` (TEN-3, ADM-1 hook) |

No tenant ids, client ids, secrets or scopes go into `appsettings*.json` or `launchSettings.json`. `RepositoryConfigurationTests` already forbids `ClientSecret` and `Password` there.

## 5. Tasks

Ownership: T1 to T9 go to `implementer`, T10 to T15 to `test-writer`, and T16 to `docs-writer`.

```
T1 (alone, first)
 ├─ T2 ──┬─ T4 ─┐
 │       └─ T5 ─┤          T4 and T5 in parallel after T2
 ├─ T3 ─────────┼─ T6 ── T7          T6 after T3, T4, T5
 │              │
 ├─ T8 ─────────┴─ T9                T9 after T3 and T8 (parallel with T6 and T7)
 │                                   T2, T3 and T8 in parallel after T1
 ├─ T10 after T7 and T9
 ├─ T11 after T10
 ├─ T12 after T4, T5, T6 and T9 (each test file can start once its target exists)
 ├─ T13, T14, T15 after T10, in parallel
 └─ T16 after all
```

Every implementer runs `dotnet build SharpVault.slnx` before finishing, and it must report `0 Warning(s)` and `0 Error(s)`.

---

### T1. Packages, project references and user secrets (sequential, first)

**Files**

1. `Directory.Packages.props`: add these `<PackageVersion>` entries, keeping the list sorted alphabetically:
   - `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.14
   - `Microsoft.AspNetCore.Components.Authorization` 10.0.14
   - `Microsoft.Extensions.Caching.Memory` 10.0.14
   - `Microsoft.Extensions.TimeProvider.Testing` 10.11.0
   - `Microsoft.Identity.Web` 4.16.1
   - `System.Security.Cryptography.Pkcs` 10.0.14
   - `System.Security.Cryptography.Xml` 10.0.14

   The two `System.Security.Cryptography.*` entries are transitive pins only. No project references them (`CentralPackageTransitivePinningEnabled` is already true).
2. `src/SharpVault.Api/SharpVault.Api.csproj`: add `<PackageReference Include="Microsoft.Identity.Web" />`. Keep the JwtBearer reference. Add `<PropertyGroup><UserSecretsId>NEW-GUID</UserSecretsId></PropertyGroup>`, with a new value from `uuidgen`.
3. `src/SharpVault.Web/SharpVault.Web.csproj`: add `<PackageReference Include="Microsoft.Identity.Web" />` and its own new `<UserSecretsId>`.
4. `src/SharpVault.Infrastructure/SharpVault.Infrastructure.csproj`: add `<PackageReference Include="Microsoft.Extensions.Caching.Memory" />` and `<ItemGroup><InternalsVisibleTo Include="SharpVault.UnitTests" /></ItemGroup>`.
5. `src/SharpVault.UI.Shared/SharpVault.UI.Shared.csproj`: add `<PackageReference Include="Microsoft.AspNetCore.Components.Authorization" />`. No `FrameworkReference` (`UiSharedProjectFile_FrameworkReferences_AreEmpty`).
6. `tests/SharpVault.UnitTests/SharpVault.UnitTests.csproj`: add `Microsoft.Extensions.TimeProvider.Testing`.
7. `tests/SharpVault.IntegrationTests/SharpVault.IntegrationTests.csproj`: add `Microsoft.Extensions.TimeProvider.Testing` and `NSubstitute`.

**Acceptance**

- `dotnet build SharpVault.slnx` gives 0 warnings, and `dotnet list SharpVault.slnx package --vulnerable --include-transitive` lists no vulnerable package (verified with exactly this set).
- No `Version=` attribute in any `.csproj`.
- No project reference is added or removed (`ProjectFiles_ProjectReferences_MatchTheAllowedLayering` must still pass).

---

### T2. Domain entities and Application contracts (after T1, parallel with T3 and T8)

**Domain** (namespace `SharpVault.Domain.Users` and `SharpVault.Domain.Audit`). These are lean POCOs with no methods:

- `Users/UserStatus.cs`: `public enum UserStatus { Locked = 0, Active = 1 }`.
- `Users/User.cs`: `public sealed class User` with `Guid Id`, `string DisplayName = string.Empty`, `string? Email`, `UserStatus Status` (no initializer: the default is `Locked`, and code that creates a user sets `Active` explicitly), `DateTimeOffset CreatedAt` and `DateTimeOffset LastSignInAt`. All `{ get; set; }`.
- `Audit/AuditEventType.cs`: `public enum AuditEventType { SignIn = 1 }`. Later features append values. They are stored as strings, so the numbers never matter.
- `Audit/AuditEntry.cs`: `public sealed class AuditEntry` with `Guid Id`, `AuditEventType EventType`, `Guid ActorId`, `string? TargetId`, `DateTimeOffset OccurredAt`, `string? IpAddress` and `string? Reason`.

**Application, `Common/Models`** (namespace `SharpVault.Application.Common.Models`):

- `Result.cs`: `public sealed class Result<T>` as in the clean-architecture skill. Members: `IsSuccess`, `T? Value`, `string? Error`, `IReadOnlyList<string> Errors`, `static Result<T> Success(T value)`, `static Result<T> Failure(string error)` and `static Result<T> Failure(IReadOnlyList<string> errors)`. Private constructors.
- `Versioned.cs`: `public sealed record Versioned<T>(T Value, string ETag);`
- `StoreWriteResult.cs`: `public enum StoreWriteResult { Written, AlreadyExists, Conflict, NotFound }`.
- `UserListPage.cs`: `public sealed record UserListPage(IReadOnlyList<User> Users, Guid? NextAfterUserId);`
- `CachedUserAccess.cs`: `public sealed record CachedUserAccess(UserStatus Status, DateTimeOffset LastSignInAt);`

**Application, `Common/Interfaces`**. All async members take a `CancellationToken cancellationToken` as the last parameter.

- `ICurrentUser.cs`: `Guid UserId { get; }`, `string DisplayName { get; }`, `bool IsAdmin { get; }`. Add an XML doc comment saying the value comes from the Entra `oid` claim. There is deliberately **no** `Email` member, so email can never be used as identity.
- `IUserStore.cs`:

  ```csharp
  Task<Versioned<User>?> GetAsync(Guid userId, CancellationToken cancellationToken);
  Task<StoreWriteResult> AddAsync(User user, CancellationToken cancellationToken);
  Task<StoreWriteResult> ReplaceAsync(User user, string etag, CancellationToken cancellationToken);
  Task<UserListPage> ListAsync(int pageSize, Guid? afterUserId, CancellationToken cancellationToken);
  ```

- `IAuditLog.cs`: exactly one member, `Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken);`. An XML doc comment states that the log is append-only (AUD-1).
- `IUserAccessCache.cs`: `bool TryGet(Guid userId, [NotNullWhen(true)] out CachedUserAccess? access);`, `void Set(Guid userId, CachedUserAccess access);` and `void Invalidate(Guid userId);`. The XML doc for `Invalidate` says that `feature/user-lock` calls it after writing a lock.
- `IGuestAccessPolicy.cs`: `ValueTask<bool> AreGuestsAllowedAsync(CancellationToken cancellationToken);`. The XML doc says it is the ADM-1 hook.

**Application, `Features/Users/Access`**, contracts only (the logic is T4):

- `UserAccessDecision.cs`: `public enum UserAccessDecision { Denied = 0, Allowed = 1 }`.
- `SignedInUser.cs`: `public sealed record SignedInUser(Guid UserId, string? DisplayName, string? Email, string? IpAddress);`. These are raw claim values; the gate normalizes them.
- `IUserAccessGate.cs`: `Task<UserAccessDecision> CheckAsync(SignedInUser user, CancellationToken cancellationToken);`
- `UserAccessOptions.cs`: `public sealed class UserAccessOptions`:
  - `public const string SectionName = "UserAccess";`
  - `TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(5);`
  - `int CacheMaxEntries { get; set; } = 10_000;`
  - `TimeSpan SignInRecordInterval { get; set; } = TimeSpan.FromHours(1);`
  - Bound constants: `MaxCacheTtl` = 30 s, `MinCacheMaxEntries` = 100, `MaxCacheMaxEntries` = 1,000,000, `MinSignInRecordInterval` = 1 minute, `MaxSignInRecordInterval` = 24 hours.
- `UserAccessMessages.cs` (internal static): constant validation messages. Include one per option bound, for example `"UserAccess:CacheTtl must be between 0 and 30 seconds."`.
- `UserAccessOptionsValidator.cs`: `public sealed class UserAccessOptionsValidator : IValidateOptions<UserAccessOptions>`. It fails with the matching constant message when a value is outside its bound. A negative `CacheTtl` fails.

**Application, `Features/Tenancy`**:

- `TenancyOptions.cs`: `public const string SectionName = "Tenancy";` and `bool AllowGuests { get; set; }` (default `false`).

**Acceptance**

- The build is clean.
- Domain still has no package references (`DomainProjectFile_PackageReferences_AreEmpty`).
- Application references neither `Azure.*` nor `Microsoft.AspNetCore.*`.

---

### T3. ServiceDefaults identity helpers and the Web CSP (after T1, parallel with T2 and T8)

Namespace `SharpVault.ServiceDefaults.Identity`. The Api and the Web both use these helpers. ServiceDefaults keeps no SharpVault project references.

1. `Identity/EntraClaimTypes.cs` (static, `const string`):
   - `ObjectId = "oid"`, `TenantId = "tid"`, `Roles = "roles"`, `Name = "name"`
   - `Scope = "scp"`, `IdentityType = "idtyp"`, `AccountType = "acct"`, `IdentityProvider = "idp"`
   - `Email = "email"`, `PreferredUsername = "preferred_username"`
   - `AppOnlyIdentityType = "app"`, `MemberAccountType = "0"`, `GuestAccountType = "1"`
2. `Identity/AppRoles.cs`: `User = "SharpVault.User"`, `Admin = "SharpVault.Admin"`.
3. `Identity/EntraSettingsMessages.cs` (internal static). Constant messages that **never** contain a configured value:
   - `InstanceInvalid` = `"AzureAd:Instance must be an absolute https URI with no path, query or user information."`
   - `TenantIdInvalid` = `"AzureAd:TenantId must be the directory (tenant) id GUID. Multi-tenant values such as common, organizations and consumers are not allowed."`
   - `ClientIdInvalid` = `"AzureAd:ClientId must be the application (client) id GUID."`
4. `Identity/EntraApplicationSettings.cs`: `public sealed record EntraApplicationSettings(Uri Instance, Guid TenantId, Guid ClientId)` with:
   - `public const string SectionName = "AzureAd";`
   - `public static EntraApplicationSettings Read(IConfiguration section)`. It reads `Instance`, `TenantId` and `ClientId` and throws `InvalidOperationException` with the matching constant message when:
     - Instance is missing, not absolute, not https, has a query or user info, or has a path other than `/`;
     - TenantId fails `Guid.TryParseExact(value, "D", out var id)` or is `Guid.Empty`;
     - ClientId fails the same GUID check.
   - `public string V2Issuer`: `$"{Instance.GetLeftPart(UriPartial.Authority)}/{TenantId:D}/v2.0"`, for example `https://login.microsoftonline.com/<tid>/v2.0`.
   - `public string V1Issuer`: `$"https://sts.windows.net/{TenantId:D}/"`. It is used only by `AccountClassifier` to recognise our own tenant in `idp`, never to accept tokens.
   - `public Uri IdentityProviderOrigin`: `new Uri(Instance.GetLeftPart(UriPartial.Authority))`.
5. `Identity/TokenCheckResult.cs`: `public enum TokenCheckResult { Invalid = 0, Valid, WrongTenant, InvalidObjectId, AppOnlyToken, MissingScope }`.
6. `Identity/EntraTokenChecks.cs` (static):
   - `public static TokenCheckResult CheckAccessToken(ClaimsPrincipal? principal, EntraApplicationSettings settings, string requiredScope)` checks in this order:
     1. If `principal` is null, return `Invalid`.
     2. Exactly one `tid` claim that parses with `TryParseExact("D")` and equals `settings.TenantId`, otherwise `WrongTenant`.
     3. Exactly one `oid` claim that parses with `TryParseExact("D")` and is not `Guid.Empty`, otherwise `InvalidObjectId`.
     4. If any `idtyp` claim equals `"app"` (Ordinal), return `AppOnlyToken`.
     5. Exactly one `scp` claim whose value, split on `' '` with `RemoveEmptyEntries`, contains `requiredScope` (Ordinal), otherwise `MissingScope`.
     6. Otherwise `Valid`.
   - `public static TokenCheckResult CheckIdToken(ClaimsPrincipal? principal, EntraApplicationSettings settings)` runs steps 1 to 3 only. The Web uses it.
7. `Identity/AccountKind.cs`: `public enum AccountKind { Unknown = 0, Member, Guest }`.
8. `Identity/AccountClassifier.cs` (static): `public static AccountKind Classify(ClaimsPrincipal principal, EntraApplicationSettings settings)`:
   1. Read the `acct` claims. If there is not exactly one, return `Unknown`. A value of `"1"` returns `Guest`. Any value other than `"0"` returns `Unknown`.
   2. With `acct = "0"`: when an `idp` claim is present and its value is neither `settings.V2Issuer` nor `settings.V1Issuer` (Ordinal), return `Guest`. Otherwise return `Member`.
9. `Security/SecurityHeaderValues.cs`: replace `WebContentSecurityPolicy` with `WebContentSecurityPolicyFormat` = `"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' {0}; upgrade-insecure-requests"`.
10. `Security/SecurityHeadersPolicy.cs`: replace `ForWeb()` with `public static SecurityHeadersPolicy ForWeb(Uri identityProviderOrigin)`:
    - Throw `ArgumentException` unless the URI is absolute and https.
    - Format the CSP with `string.Format(CultureInfo.InvariantCulture, SecurityHeaderValues.WebContentSecurityPolicyFormat, identityProviderOrigin.GetLeftPart(UriPartial.Authority))`.
    - The exempt path list stays empty.

    This resolves foundation D21: Chromium enforces `form-action` on the redirect after the sign-out form post, which goes to Entra's logout endpoint.

**Acceptance**

- The build is clean.
- `ServiceDefaults_ReferencedAssemblies_ContainNoOtherSharpVaultProject` still passes.
- No message contains a configured value.

---

### T4. Application logic: access gate, guest policy, queries, validation and mapping (after T2, parallel with T5)

1. `Features/Users/Access/UserProfileText.cs` (public static):
   - `public const int DisplayNameMaxLength = 256;`, `EmailMaxLength = 320`, `IpAddressMaxLength = 45`.
   - `public static string NormalizeDisplayName(string? value)`: null or whitespace becomes `string.Empty`. Otherwise trim, remove every `char.IsControl` character and truncate to the maximum length. Never end on a lone high surrogate: if the last kept char is a high surrogate, drop it.
   - `public static string? NormalizeEmail(string? value)`: the same rules with `EmailMaxLength`, but empty becomes `null`, and a value without `'@'` becomes `null`.
   - `public static string? NormalizeIpAddress(string? value)`: `null` when it is empty, longer than 45 characters or not parseable by `IPAddress.TryParse`. Otherwise the parsed address's `ToString()`.
2. `Features/Users/Access/UserAccessGate.cs`: `public sealed partial class UserAccessGate(IUserStore users, IAuditLog auditLog, IUserAccessCache cache, TimeProvider timeProvider, IOptions<UserAccessOptions> options, ILogger<UserAccessGate> logger) : IUserAccessGate`.

   `CheckAsync(SignedInUser user, CancellationToken cancellationToken)` runs exactly this algorithm. **The order of writes matters** (D10).

   ```
   now = timeProvider.GetUtcNow()
   if cache.TryGet(user.UserId, out cached):
       if cached.Status != Active:
           return Denied                                          (no storage read, no write, even when a sign-in is due; D33)
       if not IsSignInDue(cached.LastSignInAt, now):
           return Allowed
   stored = await users.GetAsync(user.UserId)
   if stored is null:
       return await ProvisionAsync(user, now)
   if stored.Value.Status != Active:
       cache.Set(id, new(stored.Value.Status, stored.Value.LastSignInAt)); LogLockedUserDenied(id); return Denied
   if not IsSignInDue(stored.Value.LastSignInAt, now):
       cache.Set(...); return Allowed
   return await RecordSignInAsync(user, stored, now)

   ProvisionAsync:
       newUser = User { Id, DisplayName = Normalize, Email = Normalize, Status = Active, CreatedAt = now, LastSignInAt = now }
       result = await users.AddAsync(newUser)
       if result == Written: await AppendSignInAsync(user, now); cache.Set(...Active, now); LogUserProvisioned(id); return Allowed
       return await ReloadAsync(user.UserId)                      (AlreadyExists: another request won the race)

   RecordSignInAsync:
       updated = copy of stored.Value with DisplayName, Email refreshed from the token and LastSignInAt = now (Status unchanged)
       result = await users.ReplaceAsync(updated, stored.ETag)
       if result == Written: await AppendSignInAsync(user, now); cache.Set(...); LogSignInRecorded(id); return Allowed
       LogSignInWriteConflict(id); return await ReloadAsync(user.UserId)   (Conflict or NotFound)

   ReloadAsync:
       reloaded = await users.GetAsync(id)
       if reloaded is null: throw new InvalidOperationException(UserAccessMessages.UserRowMissing)
       cache.Set(...); return Decide(reloaded.Value.Status)

   AppendSignInAsync:
       entry = AuditEntry { Id = Guid.NewGuid(), EventType = SignIn, ActorId = id, TargetId = null,
                            OccurredAt = now, IpAddress = NormalizeIpAddress(user.IpAddress), Reason = null }
       try await auditLog.AppendAsync(entry) catch (Exception ex) { LogSignInAuditFailed(logger, id, ex); throw; }

   IsSignInDue(last, now) = now - last >= options.Value.SignInRecordInterval
   Decide(status)         = status == Active ? Allowed : Denied
   ```

   - The user row is written **before** the audit entry. The ETag (on replace) or the insert conflict (on add) makes exactly one concurrent request the winner, and only the winner writes the audit entry. A lock written between the read and the replace makes the replace fail with `Conflict`, so the lock is never overwritten and no sign-in is recorded.
   - The gate never catches storage exceptions except to log and rethrow the audit failure. Nothing is cached after an exception.
   - A cached entry that is not `Active` is denied straight from the cache, **before** `IsSignInDue` is evaluated: no `GetAsync`, no `AddAsync`, no `ReplaceAsync` and no `AppendAsync`, whatever `LastSignInAt` is. Only a cached `Active` entry goes through the `IsSignInDue` path. A locked user's `LastSignInAt` is never updated (D30), so without this rule every request after the sign-in interval would miss the cache and read storage (D33). The cache TTL still bounds how long an unlock takes to be seen.
   - Log messages (`[LoggerMessage]`, user id only): `LogUserProvisioned` (Information, `"User {UserId} provisioned"`), `LogSignInRecorded` (Information), `LogLockedUserDenied` (Warning, `"Access denied for locked user {UserId}"`), `LogSignInWriteConflict` (Debug), and `LogSignInAuditFailed` (Error, with the exception).
   - Add `UserRowMissing` = `"The user row disappeared during the access check."` to `UserAccessMessages`.
3. `Features/Tenancy/ConfiguredGuestAccessPolicy.cs`: `public sealed class ConfiguredGuestAccessPolicy(IOptionsMonitor<TenancyOptions> options) : IGuestAccessPolicy`, which returns `ValueTask.FromResult(options.CurrentValue.AllowGuests)`.
4. `Common/Behaviors/ValidationBehavior.cs`: the skill's `ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>`. If there are no validators, call `next()`. Otherwise validate them all, and if there are failures, `throw new ValidationException(failures)`. Signature (V11): `Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)`.
5. `Features/Users/Dtos/`:
   - `CurrentUserDto(Guid UserId, string DisplayName, bool IsAdmin)`
   - `UserSummaryDto(Guid UserId, string DisplayName, string? Email, string Status, DateTimeOffset LastSignInAt)`
   - `UserPageDto(IReadOnlyList<UserSummaryDto> Users, string? ContinuationToken)`

   All are `public sealed record`.
6. `Features/Users/Queries/GetCurrentUser.cs`: `public sealed record GetCurrentUserQuery : IRequest<Result<CurrentUserDto>>;` and `public sealed class GetCurrentUserQueryHandler(ICurrentUser currentUser)`, which returns `Success(new CurrentUserDto(currentUser.UserId, currentUser.DisplayName, currentUser.IsAdmin))`. No storage call: the gate has already provisioned the row.
7. `Features/Users/Queries/ListUsers.cs`:
   - `public sealed record ListUsersQuery(int PageSize, string? ContinuationToken) : IRequest<Result<UserPageDto>>` with `public const int DefaultPageSize = 50;` and `public const int MaxPageSize = 100;`.
   - `public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>`:
     - `RuleFor(x => x.PageSize).InclusiveBetween(1, ListUsersQuery.MaxPageSize).WithMessage(ListUsersMessages.PageSizeOutOfRange)`
     - `RuleFor(x => x.ContinuationToken).Must(IsCanonicalGuid).When(x => x.ContinuationToken is not null).WithMessage(ListUsersMessages.InvalidContinuationToken)`, where `IsCanonicalGuid` means length 36 and `Guid.TryParseExact(value, "D", out _)`.
     - The messages are constants that **never** include the attempted value. FluentValidation's default `InclusiveBetween` message echoes the value, so `.WithMessage` is required.
     - `ListUsersMessages` is an internal static class in the same file. This is allowed: the file is the colocated feature file.
   - `public sealed class ListUsersQueryHandler(IUserStore users)`: `var after = request.ContinuationToken is null ? (Guid?)null : Guid.ParseExact(request.ContinuationToken, "D");`, then `var page = await users.ListAsync(request.PageSize, after, cancellationToken);`. Map with `page.Users.Adapt<List<UserSummaryDto>>()` and return `Success(new UserPageDto(items, page.NextAfterUserId?.ToString("D")))`.
8. `Mapping/UserMappingRegister.cs`: `public sealed class UserMappingRegister : IRegister`. `config.NewConfig<User, UserSummaryDto>().Map(d => d.UserId, s => s.Id).Map(d => d.Status, s => s.Status.ToString());`
9. `DependencyInjection.cs`: after the existing registrations, add:
   - `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));`
   - `services.AddSingleton<IUserAccessGate, UserAccessGate>();`
   - `services.AddSingleton<IGuestAccessPolicy, ConfiguredGuestAccessPolicy>();`
   - `services.AddSingleton<IValidateOptions<UserAccessOptions>, UserAccessOptionsValidator>();`

   Do not bind configuration here; the Api does that (T6).

**Acceptance**

- The build is clean.
- No handler or gate code reads `IHostEnvironment`, `HttpContext` or any `Microsoft.AspNetCore` type.

---

### T5. Infrastructure: Users and AuditLog stores, and the access cache (after T2, parallel with T4)

1. `Storage/TableNames.cs`: add `public const string Users = "Users";` and `public const string AuditLog = "AuditLog";`, and set `All` to `[Users, AuditLog]`.
2. `Users/UserKeys.cs` (public static): `PartitionKey = "user"` and `public static string RowKey(Guid userId) => userId.ToString("D");` (lowercase canonical form).
3. `Users/UserStatusText.cs` (internal static): `public static UserStatus Parse(string? value)` returns `Active` only when the value equals `"Active"` (Ordinal). **Every other value, including null, returns `Locked`** (fail closed).
4. `Users/UserTableEntity.cs`: `internal sealed class UserTableEntity : ITableEntity` with `PartitionKey`, `RowKey`, `DateTimeOffset? Timestamp`, `ETag ETag`, `string DisplayName`, `string? Email`, `string Status`, `DateTimeOffset CreatedAt` and `DateTimeOffset LastSignInAt`. Strings default to `string.Empty`.
5. `Users/UserStorageMappingRegister.cs`: `public sealed class UserStorageMappingRegister : IRegister`:
   - `config.NewConfig<User, UserTableEntity>()`:
     - `.Map(d => d.PartitionKey, _ => UserKeys.PartitionKey)`
     - `.Map(d => d.RowKey, s => UserKeys.RowKey(s.Id))`
     - `.Map(d => d.Status, s => s.Status.ToString())`
     - `.Ignore(d => d.ETag).Ignore(d => d.Timestamp)`
   - `config.NewConfig<UserTableEntity, User>()`:
     - `.Map(d => d.Id, s => Guid.ParseExact(s.RowKey, "D"))`
     - `.Map(d => d.Status, s => UserStatusText.Parse(s.Status))`
6. `Users/TableUserStore.cs`: `public sealed class TableUserStore(TableServiceClient tableServiceClient) : IUserStore` with the field `private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.Users);`.
   - `GetAsync`: `await _table.GetEntityIfExistsAsync<UserTableEntity>(UserKeys.PartitionKey, UserKeys.RowKey(userId), cancellationToken: cancellationToken)`. If `HasValue` is false, return `null`. Otherwise return `new Versioned<User>(value.Adapt<User>(), value.ETag.ToString())`. This is a point read, one storage call.
   - `AddAsync`: `AddEntityAsync(user.Adapt<UserTableEntity>(), cancellationToken)` returns `Written`. Catch `RequestFailedException` with `Status == 409` and return `AlreadyExists`.
   - `ReplaceAsync`: `UpdateEntityAsync(entity, new ETag(etag), TableUpdateMode.Replace, cancellationToken)` returns `Written`. Status 412 returns `Conflict` and status 404 returns `NotFound`. Never `ETag.All`.
   - `ListAsync`: the filter is built **only** with `TableClient.CreateQueryFilter`:
     - without a cursor: `$"PartitionKey eq {UserKeys.PartitionKey}"`;
     - with a cursor: `$"PartitionKey eq {UserKeys.PartitionKey} and RowKey gt {UserKeys.RowKey(after)}"`.

     Then `_table.QueryAsync<UserTableEntity>(filter, maxPerPage: pageSize, cancellationToken: cancellationToken).AsPages()` and read **only the first page** (one storage call). `NextAfterUserId`:
     - `null` when the page has no continuation token;
     - the last item's id when it has a continuation token and items;
     - the incoming `afterUserId` when it has a continuation token but no items.
7. `Audit/AuditKeys.cs` (public static):
   - `public static string PartitionKey(DateTimeOffset occurredAt) => occurredAt.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture);`
   - `public static string RowKey(DateTimeOffset occurredAt, Guid id) => (DateTime.MaxValue.Ticks - occurredAt.UtcTicks).ToString("D19", CultureInfo.InvariantCulture) + "_" + id.ToString("N");`
8. `Audit/AuditLogTableEntity.cs`: `internal sealed class` implementing `ITableEntity`, with `string EventType`, `string ActorId`, `string? TargetId`, `DateTimeOffset OccurredAt`, `string? IpAddress` and `string? Reason`.
9. `Audit/AuditStorageMappingRegister.cs`: `AuditEntry` to `AuditLogTableEntity`:
   - PartitionKey and RowKey from `AuditKeys`
   - `EventType` from `s.EventType.ToString()`
   - `ActorId` from `s.ActorId.ToString("D")`
   - ETag and Timestamp ignored
10. `Audit/TableAuditLog.cs`: `public sealed class TableAuditLog(TableServiceClient tableServiceClient) : IAuditLog`. `AppendAsync` calls only `AddEntityAsync`. Exceptions propagate, including 409. The file must not contain `UpdateEntity`, `UpsertEntity`, `DeleteEntity`, `SubmitTransaction` or `ETag.All` (a source scan test checks this).
11. `Users/UserAccessCache.cs`: `public sealed class UserAccessCache : IUserAccessCache, IDisposable`.
    - Constructor `(IOptions<UserAccessOptions> options, TimeProvider timeProvider)` stores `_ttl = options.Value.CacheTtl` and creates `new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.CacheMaxEntries })`.
    - It stores a private `sealed record Entry(CachedUserAccess Access, DateTimeOffset ExpiresAt)`.
    - `Set`: when `_ttl <= TimeSpan.Zero`, return without storing. Otherwise `_cache.Set(userId, new Entry(access, now + _ttl), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = _ttl })`.
    - `TryGet`: returns false when there is no entry, or when `entry.ExpiresAt <= timeProvider.GetUtcNow()`. Validity uses the `TimeProvider`, so tests can drive it with `FakeTimeProvider`. The MemoryCache expiration is only a memory backstop.
    - `Invalidate`: `_cache.Remove(userId)`.
    - `internal int Count => _cache.Count;` for tests.
    - `Dispose` disposes the cache.
12. `DependencyInjection.cs` (`AddInfrastructure`), additions:
    - `TypeAdapterConfig.GlobalSettings.Scan(typeof(DependencyInjection).Assembly);`
    - `builder.Services.AddSingleton<IUserStore, TableUserStore>();`
    - `builder.Services.AddSingleton<IAuditLog, TableAuditLog>();`
    - `builder.Services.AddSingleton<IUserAccessCache, UserAccessCache>();`

**Acceptance**

- The build is clean.
- No store code reads `IHostEnvironment`.
- Every query has a `PartitionKey` condition.
- Every update uses an ETag, never `ETag.All`.

---

### T6. Api authentication and authorization (after T3, T4 and T5)

1. `Security/Policies.cs`: `public const string User = "SharpVaultUser";` and `public const string Admin = "SharpVaultAdmin";`.
2. `Security/ApiScopes.cs`: `public const string AccessAsUser = "access_as_user";`.
3. `Security/AccessTokenValidation.cs`: `public static partial class AccessTokenValidation`.
   - `public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);`
   - `private const string RejectionMessage = "The access token is not accepted.";` and `private const string LogCategory = "SharpVault.Api.Security.AccessTokenValidation";`.
   - `public static Task ValidateAsync(TokenValidatedContext context, EntraApplicationSettings settings)`:
     1. `var result = EntraTokenChecks.CheckAccessToken(context.Principal, settings, ApiScopes.AccessAsUser);`
     2. If `Valid`, return `Task.CompletedTask`.
     3. Otherwise get the logger with `context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory)` and call `LogTokenRejected(logger, result)`. `[LoggerMessage(Level = LogLevel.Warning, Message = "Access token rejected: {Reason}")]`. Only the enum value is logged, no claim values.
     4. `context.Fail(RejectionMessage);`
4. `Security/ApiAuthenticationExtensions.cs`: replace the class body. The new signature is `public static IHostApplicationBuilder AddApiAuthentication(this IHostApplicationBuilder builder)`:

   ```csharp
   var section = builder.Configuration.GetSection(EntraApplicationSettings.SectionName);
   var entra = EntraApplicationSettings.Read(section);
   builder.Services.AddSingleton(entra);
   builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
       .AddMicrosoftIdentityWebApi(jwt => ConfigureJwtBearer(jwt, entra), identity => section.Bind(identity));
   builder.Services.AddOptions<UserAccessOptions>()
       .Bind(builder.Configuration.GetSection(UserAccessOptions.SectionName)).ValidateOnStart();
   builder.Services.AddOptions<TenancyOptions>()
       .Bind(builder.Configuration.GetSection(TenancyOptions.SectionName)).ValidateOnStart();
   builder.Services.AddHttpContextAccessor();
   builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
   builder.Services.AddSingleton<IAuthorizationHandler, SharpVaultAccessHandler>();
   var userPolicy = BuildPolicy(AppRoles.User, AppRoles.Admin);
   var adminPolicy = BuildPolicy(AppRoles.Admin);
   builder.Services.AddAuthorizationBuilder()
       .AddPolicy(Policies.User, userPolicy)
       .AddPolicy(Policies.Admin, adminPolicy)
       .SetDefaultPolicy(userPolicy)
       .SetFallbackPolicy(userPolicy);
   return builder;
   ```

   - `private static AuthorizationPolicy BuildPolicy(params string[] roles) => new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().AddRequirements(new SharpVaultAccessRequirement(roles)).Build();`
   - `private static void ConfigureJwtBearer(JwtBearerOptions jwt, EntraApplicationSettings entra)`:
     - `jwt.MapInboundClaims = false;`
     - `jwt.IncludeErrorDetails = false;`, so the challenge is a bare `Bearer` with no `error_description`.
     - `jwt.TokenValidationParameters.NameClaimType = EntraClaimTypes.Name;` and `RoleClaimType = EntraClaimTypes.Roles;`
     - `jwt.TokenValidationParameters.ValidIssuers = [entra.V2Issuer];` This is required, otherwise there is a network metadata fetch (V2). Only v2 tokens are accepted.
     - `jwt.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];`
     - `jwt.TokenValidationParameters.ClockSkew = AccessTokenValidation.ClockSkew;`
     - `jwt.Events ??= new JwtBearerEvents();` then `jwt.Events.OnTokenValidated = context => AccessTokenValidation.ValidateAsync(context, entra);`
     - Never set `IdentityModelEventSource.ShowPII`, and never enable Microsoft.Identity.Web PII logging.
5. `Security/SignedInIdentity.cs`: `public sealed record SignedInIdentity(Guid UserId, string? DisplayName, string? Email, AccountKind AccountKind, bool IsAdmin);`
6. `Security/SignedInIdentityReader.cs` (static): `public static bool TryRead(ClaimsPrincipal principal, EntraApplicationSettings settings, [NotNullWhen(true)] out SignedInIdentity? identity)`.
   - It returns false unless there is exactly one `oid` claim that parses with `TryParseExact("D")` and is not empty.
   - `DisplayName` is the first `name` claim value.
   - `Email` is the `email` claim if present, otherwise `preferred_username`. Normalization happens later.
   - `AccountKind` is `AccountClassifier.Classify(principal, settings)`.
   - `IsAdmin` is `principal.IsInRole(AppRoles.Admin)`.
7. `Security/SharpVaultAccessRequirement.cs`: `public sealed class SharpVaultAccessRequirement(IReadOnlyList<string> acceptedRoles) : IAuthorizationRequirement { public IReadOnlyList<string> AcceptedRoles { get; } = acceptedRoles; }`
8. `Security/AccessDenialReason.cs`: `public enum AccessDenialReason { InvalidIdentity, Guest, MissingRole, Locked }`.
9. `Security/SharpVaultAccessHandler.cs`: `public sealed partial class SharpVaultAccessHandler(IUserAccessGate gate, IGuestAccessPolicy guestPolicy, EntraApplicationSettings entra, ILogger<SharpVaultAccessHandler> logger) : AuthorizationHandler<SharpVaultAccessRequirement>`.

   `HandleRequirementAsync` runs these steps in order:
   1. If `context.User.Identity?.IsAuthenticated != true`, return without succeeding or failing. `DenyAnonymousAuthorizationRequirement` then produces 401.
   2. If `!SignedInIdentityReader.TryRead(...)`, deny with `InvalidIdentity`.
   3. `var httpContext = context.Resource as HttpContext;` and `var cancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;`.
   4. If `identity.AccountKind != AccountKind.Member` and `!await guestPolicy.AreGuestsAllowedAsync(cancellationToken)`, deny with `Guest`.
   5. If none of `requirement.AcceptedRoles` satisfies `context.User.IsInRole(role)`, deny with `MissingRole`.
   6. `var decision = await GetDecisionAsync(identity, httpContext, cancellationToken);`. If `Denied`, call `context.Fail(new AuthorizationFailureReason(this, nameof(AccessDenialReason.Locked)))`. The gate logs the lock.
   7. `context.Succeed(requirement);`

   Supporting members:
   - "Deny" means `context.Fail(new AuthorizationFailureReason(this, reason.ToString()))` plus `[LoggerMessage(Level = LogLevel.Information, Message = "Access denied for user {UserId}: {Reason}")]`. The user id is null for `InvalidIdentity`.
   - `GetDecisionAsync` memoizes the decision per request in `httpContext.Items` under `private static readonly object DecisionKey = new();`, so the gate runs once per request even if an endpoint combines policies. It builds `new SignedInUser(identity.UserId, identity.DisplayName, identity.Email, httpContext?.Connection.RemoteIpAddress?.ToString())`. The IP is the connection address only; forwarded headers are ignored (D25).
10. `Security/HttpContextCurrentUser.cs`: `public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor, EntraApplicationSettings entra) : ICurrentUser`.
    - It lazily reads the identity with `SignedInIdentityReader`. If there is no authenticated principal or the read fails, it throws `InvalidOperationException("No signed-in user is available.")` (a constant).
    - `UserId` is `identity.UserId`.
    - `DisplayName` is `UserProfileText.NormalizeDisplayName(identity.DisplayName)`.
    - `IsAdmin` is `identity.IsAdmin`.
11. `Errors/ValidationExceptionHandler.cs`: `public sealed class ValidationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler`.
    - For any exception other than `FluentValidation.ValidationException`, return `false`.
    - Otherwise set status 400 and write a `ValidationProblemDetails` whose `Errors` groups `ErrorMessage` by `PropertyName`, with title `"The request is not valid."`, through `problemDetails.TryWriteAsync`. Return `true`.
    - Never include `AttemptedValue`.
12. `Errors/ResultHttpExtensions.cs`: `public static IResult ToHttpResult<T>(this Result<T> result)` returns `TypedResults.Ok(result.Value)` on success. On failure it returns `TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request could not be completed.")`, without the error text.
13. `Program.cs`:
    - Replace `builder.Services.AddApiAuthentication();` with `builder.AddApiAuthentication();`.
    - Add `builder.Services.AddExceptionHandler<ValidationExceptionHandler>();` after `AddProblemDetails()`.
    - The middleware order is unchanged. The lock check is an authorization requirement, not middleware (D7).
14. `appsettings.json`: add `"AzureAd": { "Instance": "https://login.microsoftonline.com/" }`. Nothing else.

**Acceptance**

- The build is clean.
- With `AzureAd:TenantId` and `ClientId` missing, the API fails at startup with `TenantIdInvalid` or `ClientIdInvalid`.
- No class derives from `AuthenticationHandler<>`.

---

### T7. Api endpoints (after T6)

1. `Endpoints/MeEndpoints.cs`:
   - `MapMeEndpoints(this IEndpointRouteBuilder app)` creates `var group = app.MapGroup("/api/me").RequireAuthorization(Policies.User).WithTags("Me");`.
   - `group.MapGet("", GetCurrentUserAsync).WithName("Me_Get").WithSummary("Get the signed-in user.").WithDescription("Returns the signed-in user's id, display name and whether they are an admin.");`
   - The handler is `private static async Task<IResult> GetCurrentUserAsync(IMediator mediator, CancellationToken cancellationToken) => (await mediator.Send(new GetCurrentUserQuery(), cancellationToken)).ToHttpResult();`.
2. `Endpoints/AdminUserEndpoints.cs`:
   - `MapAdminUserEndpoints` creates the group `/api/admin/users` with `.RequireAuthorization(Policies.Admin).WithTags("Admin users")`.
   - `MapGet("", ListUsersAsync).WithName("AdminUsers_List")`, with a summary and a description that say only directory data, status and last sign-in are returned.
   - The handler takes `int? pageSize, string? continuationToken, IMediator mediator, CancellationToken cancellationToken` and sends `new ListUsersQuery(pageSize ?? ListUsersQuery.DefaultPageSize, continuationToken)`.
   - It must **not** also apply `Policies.User`. One policy per endpoint.
3. `Program.cs`: `app.MapMeEndpoints();` and `app.MapAdminUserEndpoints();` go after `app.MapApiDocumentation();` and before `app.MapDefaultEndpoints();`.

**Acceptance**

- The build is clean.
- `/openapi/v1.json` in Development lists both operations.

---

### T8. UI.Shared: account UI and API client contract (after T1, parallel with T2 to T7)

The UI.Shared project must not reference `Microsoft.AspNetCore.Http*`, `Hosting*` or `Mvc*`, nor any server project. Every interactive element has the `id` listed here. No inline styles or scripts.

1. `Models/CurrentUserProfile.cs`: `public sealed record CurrentUserProfile(Guid UserId, string DisplayName, bool IsAdmin);`. It mirrors the API `CurrentUserDto` JSON, and a contract test checks that they match (D23).
2. `Services/ApiCallStatus.cs`: `public enum ApiCallStatus { Failed = 0, Success, SignInRequired, AccessDenied }`.
3. `Services/ApiResponse.cs`: `public sealed record ApiResponse<T>(ApiCallStatus Status, T? Value)` with static factories `Success(T value)`, `SignInRequired()`, `AccessDenied()` and `Failed()`.
4. `Services/ISharpVaultApiClient.cs`: `Task<ApiResponse<CurrentUserProfile>> GetCurrentUserAsync(CancellationToken cancellationToken);`
5. `Navigation/AccountRoutes.cs` (static, `const string`):
   - `SignIn = "/account/sign-in"`, `SignOut = "/account/sign-out"`
   - `Account = "/account"`, `AccessDenied = "/account/access-denied"`
   - `SignedOut = "/account/signed-out"`, `SignInError = "/account/sign-in-error"`
   - `ReturnUrlParameter = "returnUrl"`
6. `_Imports.razor`: add `@using Microsoft.AspNetCore.Authorization`, `@using Microsoft.AspNetCore.Components.Authorization`, `@using Microsoft.AspNetCore.Components.Forms`, `@using SharpVault.UI.Shared.Models`, `@using SharpVault.UI.Shared.Services` and `@using SharpVault.UI.Shared.Navigation`.
7. `Layout/AccountMenu.razor` (+ `.razor.css`): `<nav id="account-menu">` with `<AuthorizeView>`.
   - `Authorized`: `<span id="account-name">@context.User.Identity?.Name</span>`, `<a id="account-link" href="account">Account</a>`, and `<form id="sign-out-form" method="post" action="@AccountRoutes.SignOut"><AntiforgeryToken /><button id="sign-out-button" type="submit">Sign out</button></form>`.
   - `NotAuthorized`: `<a id="sign-in-link" href="@AccountRoutes.SignIn">Sign in</a>`.
8. `Layout/MainLayout.razor`: add `<header id="app-header" class="app-header"><AccountMenu /></header>` above the `app-shell` div. Keep the error UI.
9. `Pages/Home.razor`:
   - Add `@attribute [AllowAnonymous]`.
   - Inside the section, add `<AuthorizeView><Authorized><p id="home-greeting">Signed in as @context.User.Identity?.Name</p></Authorized><NotAuthorized><a id="home-sign-in" href="@AccountRoutes.SignIn">Sign in with your work account</a></NotAuthorized></AuthorizeView>`.
   - The existing ids stay.
10. `Pages/NotFound.razor`: add `@attribute [AllowAnonymous]`.
11. `Pages/Account.razor`: `@page "/account"`, `@attribute [Authorize]`, injects `ISharpVaultApiClient` and `NavigationManager`. In `OnInitializedAsync` it calls `GetCurrentUserAsync(CancellationToken.None)`:
    - `SignInRequired`: `NavigateTo($"{AccountRoutes.SignIn}?{AccountRoutes.ReturnUrlParameter}={Uri.EscapeDataString(AccountRoutes.Account)}", forceLoad: true)`.
    - `AccessDenied`: `NavigateTo(AccountRoutes.AccessDenied, forceLoad: true)`.
    - `Failed`: render `<p id="account-error">Your account details could not be loaded. Try again later.</p>`.
    - `Success`: render `<h1 id="account-heading">Your account</h1>` and a `<dl>` with `<dd id="account-display-name">`, `<dd id="account-user-id">`, and `<span id="account-admin-badge">Administrator</span>` only when `IsAdmin`.

    With `BlazorDisableThrowNavigationException` (already set in the Web project), navigation during static SSR becomes a redirect response.
12. `Pages/AccessDenied.razor`: `@page "/account/access-denied"`, `[AllowAnonymous]`. Constant text only: `<h1 id="access-denied-heading">You do not have access to SharpVault</h1>`, `<p id="access-denied-message">Ask your administrator to give you access.</p>` and `<a id="access-denied-home-link" href="">Home</a>`. It never says why access was denied.
13. `Pages/SignedOut.razor`: `@page "/account/signed-out"`, `[AllowAnonymous]`, with `#signed-out-heading` and `<a id="signed-out-sign-in-link" href="@AccountRoutes.SignIn">`.
14. `Pages/SignInError.razor`: `@page "/account/sign-in-error"`, `[AllowAnonymous]`, with `#sign-in-error-heading`, `#sign-in-error-message` (constant text) and `#sign-in-error-retry-link`. It has **no** `[SupplyParameterFromQuery]` and never renders query values.

**Acceptance**

- The build is clean.
- The layering tests for UI.Shared pass.
- No `style=`, `<script>` or `on*=` attribute appears in any new `.razor` file.

---

### T9. Web host: OIDC, cookie, token acquisition and API client (after T3 and T8, parallel with T6 and T7)

1. `Security/WebCredentialMessages.cs` (internal static), constants without values:
   - `ClientSecretNotAllowed` = `"Outside the Development environment the web app must authenticate to Entra ID with a managed identity federated credential or a certificate. Client secrets are not allowed."`
   - `CredentialRequired` = `"Outside the Development environment AzureAd:ClientCredentials must contain at least one managed identity or certificate credential."`
2. `Security/WebCredentialGuard.cs` (public static): `public static void EnsureAllowed(IConfiguration azureAdSection, IHostEnvironment environment)`.
   - In Development, return.
   - Otherwise throw `InvalidOperationException(ClientSecretNotAllowed)` if `azureAdSection["ClientSecret"]` is not null or whitespace, or if any child of `ClientCredentials` has `SourceType` equal to `"ClientSecret"` (OrdinalIgnoreCase) or a non-empty `ClientSecret` value.
   - Throw `CredentialRequired` unless at least one child has `SourceType` in `SignedAssertionFromManagedIdentity`, `KeyVault`, `StoreWithThumbprint` or `StoreWithDistinguishedName` (OrdinalIgnoreCase).
3. `Security/ReturnUrls.cs` (public static): `public const int MaxLength = 2048;` and `public static string Sanitize(string? returnUrl)`. It returns the input only when **all** of these hold, otherwise `"/"`:
   - not null or empty, and length at most `MaxLength`;
   - starts with `'/'`;
   - the second character, if any, is neither `'/'` nor `'\\'`;
   - no `'\\'` anywhere;
   - no `char.IsControl` character;
   - `Uri.TryCreate(value, UriKind.Relative, out _)`.
4. `Security/WebTokenValidation.cs` (public static partial):
   - `public static Task ValidateIdTokenAsync(TokenValidatedContext context, EntraApplicationSettings settings)`: if `EntraTokenChecks.CheckIdToken(context.Principal, settings) != Valid`, log the enum value only (Warning, category `"SharpVault.Web.Security.WebTokenValidation"`) and call `context.Fail("The sign-in is not accepted.")`.
   - `public static Task RedirectRemoteFailureAsync(RemoteFailureContext context)`: `context.Response.Redirect(AccountRoutes.SignInError); context.HandleResponse(); return Task.CompletedTask;`. Never log `context.Failure` details at a level above Debug, and never include query values.
5. `Security/WebAuthenticationExtensions.cs`: `public static IHostApplicationBuilder AddWebAuthentication(this IHostApplicationBuilder builder)`:

   ```csharp
   var section = builder.Configuration.GetSection(EntraApplicationSettings.SectionName);
   var entra = EntraApplicationSettings.Read(section);
   WebCredentialGuard.EnsureAllowed(section, builder.Environment);
   var scopes = builder.Configuration.GetSection(SharpVaultApiOptions.SectionName)
       .GetSection(nameof(SharpVaultApiOptions.Scopes)).Get<string[]>() ?? [];
   builder.Services.AddSingleton(entra);
   builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
       .AddMicrosoftIdentityWebApp(section)
       .EnableTokenAcquisitionToCallDownstreamApi(scopes)
       .AddDistributedTokenCaches();
   builder.Services.AddDistributedMemoryCache();
   builder.Services.Configure<MsalDistributedTokenCacheAdapterOptions>(options => options.Encrypt = true);
   builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
       options => ConfigureOpenIdConnect(options, entra));
   builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, ConfigureCookie);
   builder.Services.AddAuthorizationBuilder()
       .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
   builder.Services.AddCascadingAuthenticationState();
   return builder;
   ```

   - `ConfigureOpenIdConnect(OpenIdConnectOptions options, EntraApplicationSettings entra)`:
     - `options.MapInboundClaims = false;`
     - `options.TokenValidationParameters.NameClaimType = EntraClaimTypes.Name;` and `RoleClaimType = EntraClaimTypes.Roles;`
     - `options.TokenValidationParameters.ValidIssuers = [entra.V2Issuer];`
     - `options.ResponseType = OpenIdConnectResponseType.Code;`, `options.UsePkce = true;` and `options.SaveTokens = false;` (all already defaults, set explicitly so a test can pin them)
     - Chain the validated handler: `var previousValidated = options.Events.OnTokenValidated; options.Events.OnTokenValidated = async context => { await previousValidated(context); if (context.Result is null) { await WebTokenValidation.ValidateIdTokenAsync(context, entra); } };`
     - Chain the failure handler: `var previousFailure = options.Events.OnRemoteFailure; options.Events.OnRemoteFailure = async context => { await previousFailure(context); if (context.Result?.Handled != true) { await WebTokenValidation.RedirectRemoteFailureAsync(context); } };` (V8)
   - `ConfigureCookie(CookieAuthenticationOptions options)`:
     - `options.Cookie.Name = "__Host-SharpVault";`
     - `options.Cookie.SecurePolicy = CookieSecurePolicy.Always;`
     - `options.Cookie.HttpOnly = true;`
     - `options.Cookie.SameSite = SameSiteMode.Lax;`
     - `options.Cookie.Path = "/";`
     - `options.ExpireTimeSpan = TimeSpan.FromHours(8);`
     - `options.SlidingExpiration = false;`
     - `options.AccessDeniedPath = AccountRoutes.AccessDenied;`

     Keep the values in `private const` or `static readonly` fields.
6. `Api/SharpVaultApiOptions.cs`: `public sealed class SharpVaultApiOptions { public const string SectionName = "SharpVaultApi"; public Uri? BaseAddress { get; set; } public string[] Scopes { get; set; } = []; }`
7. `Api/SharpVaultApiOptionsValidator.cs`: an `IValidateOptions<SharpVaultApiOptions>` that requires an absolute `BaseAddress` and at least one non-whitespace scope. Messages are constants.
8. `Api/SharpVaultApiClient.cs`: `public sealed partial class SharpVaultApiClient(HttpClient httpClient, ITokenAcquisition tokenAcquisition, AuthenticationStateProvider authenticationStateProvider, IOptions<SharpVaultApiOptions> options, ILogger<SharpVaultApiClient> logger) : ISharpVaultApiClient`.
   - The token is attached **inside the typed client**, not in a `DelegatingHandler`. Handlers run in the HttpClientFactory handler scope, which is not the circuit or request scope, so they would see a different `AuthenticationStateProvider` (D13).
   - `private const string CurrentUserPath = "api/me";`. The path is relative, so the token only ever goes to the configured `BaseAddress`.
   - `GetCurrentUserAsync` runs these steps:
     1. `var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;`. If it is not authenticated, return `SignInRequired()` with no token request and no HTTP call.
     2. `string token; try { token = await tokenAcquisition.GetAccessTokenForUserAsync(options.Value.Scopes, authenticationScheme: null, tenantId: null, userFlow: null, user: principal, tokenAcquisitionOptions: null); } catch (MicrosoftIdentityWebChallengeUserException) { LogSignInRequired(logger); return SignInRequired(); }`
     3. `using var request = new HttpRequestMessage(HttpMethod.Get, CurrentUserPath);` with `request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);`
     4. `using var response = await httpClient.SendAsync(request, cancellationToken);`
     5. 200: `Success(await response.Content.ReadFromJsonAsync<CurrentUserProfile>(cancellationToken))`. If the body is null, return `Failed()`.
     6. 401: `SignInRequired()`. 403: `AccessDenied()`. Any other status: `LogApiCallFailed(logger, (int)response.StatusCode)` and `Failed()`.
   - Logs never include the token, the principal's claims or the response body.
9. `Api/SharpVaultApiRegistration.cs`: `public static IHostApplicationBuilder AddSharpVaultApiClient(this IHostApplicationBuilder builder)`:
   - `builder.Services.AddOptions<SharpVaultApiOptions>().Bind(builder.Configuration.GetSection(SharpVaultApiOptions.SectionName)).ValidateOnStart();`
   - `builder.Services.AddSingleton<IValidateOptions<SharpVaultApiOptions>, SharpVaultApiOptionsValidator>();`
   - `builder.Services.AddHttpClient<ISharpVaultApiClient, SharpVaultApiClient>((services, client) => client.BaseAddress = services.GetRequiredService<IOptions<SharpVaultApiOptions>>().Value.BaseAddress);`
10. `Account/AccountEndpoints.cs`: `public static WebApplication MapAccountEndpoints(this WebApplication app)`:
    - `GET /account/sign-in`, `.AllowAnonymous()`, name `Account_SignIn`. The handler `(string? returnUrl)` returns `Results.Challenge(new AuthenticationProperties { RedirectUri = ReturnUrls.Sanitize(returnUrl) }, [OpenIdConnectDefaults.AuthenticationScheme])`.
    - `POST /account/sign-out`, `.RequireAuthorization()`, name `Account_SignOut`. The handler `(HttpContext context, IAntiforgery antiforgery)`:
      - if `!await antiforgery.IsRequestValidAsync(context)`, return `Results.Problem(statusCode: 400, title: "The request is not valid.")`;
      - otherwise return `Results.SignOut(new AuthenticationProperties { RedirectUri = AccountRoutes.SignedOut }, [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme])`.

      The redirect target is fixed; no user input reaches it.
11. `Program.cs`:

    ```csharp
    builder.AddServiceDefaults();
    builder.AddWebAuthentication();
    builder.AddSharpVaultApiClient();
    builder.Services.AddRazorComponents().AddInteractiveServerComponents();

    var app = builder.Build();
    var entra = app.Services.GetRequiredService<EntraApplicationSettings>();

    app.UseSecurityHeaders(SecurityHeadersPolicy.ForWeb(entra.IdentityProviderOrigin));
    app.UseExceptionHandler(WebRoutes.Error, createScopeForErrors: true);
    app.UseStatusCodePagesWithReExecute(WebRoutes.NotFound, createScopeForStatusCodePages: true);
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();
    app.UseOutputCache();

    app.MapStaticAssets().AllowAnonymous();
    app.MapAccountEndpoints();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .AddAdditionalAssemblies(typeof(MainLayout).Assembly);
    app.MapDefaultEndpoints();
    ```

12. `Components/Routes.razor`: replace `RouteView` with `<AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(MainLayout)"><NotAuthorized><p id="not-authorized-message">You need to sign in to see this page.</p></NotAuthorized></AuthorizeRouteView>`.
13. `Components/_Imports.razor`: add `@using Microsoft.AspNetCore.Authorization` and `@using Microsoft.AspNetCore.Components.Authorization`.
14. `Components/Pages/Error.razor`: add `@attribute [AllowAnonymous]`.
15. `appsettings.json`: add `"AzureAd": { "Instance": "https://login.microsoftonline.com/", "CallbackPath": "/signin-oidc", "SignedOutCallbackPath": "/signout-callback-oidc" }` and `"SharpVaultApi": { "BaseAddress": "https+http://api" }`.

**Acceptance**

- The build is clean.
- `Web_ReferencedAssemblies_ContainNoApiInfrastructureApplicationOrDomainProject` passes.
- No `AuthenticationHandler<>` subclass.
- No client id, tenant id, scope or secret in any `appsettings*.json` or `launchSettings.json`.

---

### T10. Test infrastructure (test-writer, after T7 and T9)

Namespace `SharpVault.IntegrationTests...`. Classes are sealed unless they are a base class. No comments.

1. `Authentication/TestTenant.cs`: keep `Id`, and add `OtherId = new("5f0c1d2e-4b7a-4c3d-9e8f-00000000a002")`.
2. `Authentication/TestApi.cs`: `ClientId = new Guid("5f0c1d2e-4b7a-4c3d-9e8f-00000000c001")`, `Scope = "access_as_user"`, and `public static string ScopeUri => $"api://{ClientId:D}/{Scope}";`.
3. `Authentication/TestWebApp.cs`: `ClientId = new Guid("5f0c1d2e-4b7a-4c3d-9e8f-00000000c002")`.
4. `Authentication/TestSigningKeys.cs`:
   - `public const string KeyId = "sharpvault-test-signing-key";`
   - `public static RsaSecurityKey Primary { get; }`: RSA 2048 created once per process with `KeyId`.
   - `public static RsaSecurityKey OtherWithSameKeyId { get; }`: a second RSA 2048 key with the **same** `KeyId`.
5. `Authentication/TestIdentityProvider.cs`:
   - `public const string Instance = "https://login.microsoftonline.com/";` and `public static Uri InstanceUri`.
   - `public static string Issuer(Guid tenantId) => $"https://login.microsoftonline.com/{tenantId:D}/v2.0";`
   - `public static void ConfigureApi(IServiceCollection services)`: `services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => { var configuration = new OpenIdConnectConfiguration { Issuer = Issuer(TestTenant.Id) }; configuration.SigningKeys.Add(TestSigningKeys.Primary); options.Configuration = configuration; options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration); });` (V1).
   - `public static void ConfigureWeb(IServiceCollection services)`: the same for `OpenIdConnectOptions` under `OpenIdConnectDefaults.AuthenticationScheme`. The configuration has `Issuer`, `AuthorizationEndpoint` (`.../{tid}/oauth2/v2.0/authorize`), `TokenEndpoint` (`.../oauth2/v2.0/token`), `EndSessionEndpoint` (`.../oauth2/v2.0/logout`) and the signing key (V6).
   - `public static void ApplyApiSettings(IWebHostBuilder builder)` sets `AzureAd:Instance`, `AzureAd:TenantId` = `TestTenant.Id` and `AzureAd:ClientId` = `TestApi.ClientId` with `UseSetting`.
   - `public static void ApplyWebSettings(IWebHostBuilder builder)` sets the same with `TestWebApp.ClientId`, plus `SharpVaultApi:BaseAddress` = `https://localhost/` and `SharpVaultApi:Scopes:0` = `TestApi.ScopeUri`.
6. `Authentication/TestUser.cs`: extend to `public sealed record TestUser(Guid ObjectId, string DisplayName, Guid TenantId, IReadOnlyList<string> Roles, bool IsGuest = false, string? Email = null);`.
7. `Authentication/TestUsers.cs`: keep `AuthenticatedWithoutRole` (b001) and add the following. All GUIDs share the prefix `5f0c1d2e-4b7a-4c3d-9e8f-00000000`.

   | Name | GUID suffix | Tenant | Roles | Guest | Email |
   |---|---|---|---|---|---|
   | `User` | b002 | `TestTenant.Id` | `SharpVault.User` | no | `user@sharpvault.test` |
   | `OtherUser` | b003 | `TestTenant.Id` | `SharpVault.User` | no | `other@sharpvault.test` |
   | `Admin` | b004 | `TestTenant.Id` | `SharpVault.Admin` | no | `admin@sharpvault.test` |
   | `LockedUser` | b005 | `TestTenant.Id` | `SharpVault.User` | no | `locked@sharpvault.test` |
   | `LockedAdmin` | b006 | `TestTenant.Id` | `SharpVault.Admin` | no | `locked-admin@sharpvault.test` |
   | `Guest` | b007 | `TestTenant.Id` | `SharpVault.User` | **yes** | `guest@partner.test` |
   | `OtherTenantUser` | b008 | `TestTenant.OtherId` | `SharpVault.User` | no | `user@other.test` |

   Also add `public static IReadOnlyList<TestUser> All`.
8. `Authentication/TestTokens.cs` and `Authentication/TestTokenBuilder.cs`:
   - `TestTokens.Create(TestUser user)` returns `For(user).Build()`.
   - `TestTokens.For(TestUser user)` returns a `TestTokenBuilder` with the claims of an Entra v2 access token:
     - `iss` = `Issuer(user.TenantId)`, `aud` = `TestApi.ClientId.ToString("D")`, `tid`, `oid`, `name`, `preferred_username` and `email` (when `Email` is set)
     - `roles`: a string array, omitted when empty
     - `scp` = `"access_as_user"`, `ver` = `"2.0"`
     - `acct` = `user.IsGuest ? 1 : 0`, and for guests `idp` = `$"https://sts.windows.net/{TestTenant.OtherId:D}/"`
     - `iat` = now, `nbf` = now - 1 minute, `exp` = now + 1 hour (Unix seconds)
   - Builder members: `WithClaim(string type, object value)`, `WithoutClaim(string type)`, `ExpiresAt(DateTimeOffset)`, `NotBefore(DateTimeOffset)`, `SignedWith(SecurityKey key, string algorithm)` and `Build()`. `Build()` uses `new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor { Claims = claims, SigningCredentials = new SigningCredentials(key, algorithm) })`. The default key is `TestSigningKeys.Primary` with `SecurityAlgorithms.RsaSha256`. This shape was verified (V1, V5).
   - `TestTokens.ForObjectId(Guid oid)` looks up `TestUsers.All` and throws `InvalidOperationException` for an unknown id. The fake token acquisition uses it.
9. Delete `Authentication/TestAuthenticationHandler.cs` (D12). Add `Authentication/SpoofedTestHeaders.cs` with the former header names (`X-Test-Oid`, `X-Test-Name`, `X-Test-Tid`, `X-Test-Roles`) and `public static void Add(HttpRequestHeaders headers, TestUser user)`. The spoofing tests use it to prove that the headers do nothing.
10. `Authentication/CallerKind.cs`: add `OtherTenantUser`, `Guest`, `User`, `Admin`, `LockedUser` and `LockedAdmin`, after the existing four values.
11. `Authentication/CallerClients.cs`: `CreateFor(WebApplicationFactory<Program> factory, CallerKind caller)` sets `Authorization: Bearer TestTokens.Create(user)` for every user kind (`AuthenticatedWithoutRole` included), and keeps the forged tokens for `AlgNoneToken` and `UnknownKeyToken`. Add `public static TestUser? UserFor(CallerKind caller)`.
12. `Storage/UserRows.cs`: `UserColumns` constants (`DisplayName`, `Email`, `Status`, `CreatedAt`, `LastSignInAt`) and these methods, all through `TableClient` with `TableEntity`:
    - `InsertAsync(TableClient table, TestUser user, string status, DateTimeOffset lastSignInAt, CancellationToken cancellationToken)`
    - `InsertManyAsync(TableClient table, int count, CancellationToken cancellationToken)`: `SubmitTransactionAsync` batches of 100, all in partition `user`
    - `GetAsync(TableClient table, Guid oid, CancellationToken cancellationToken)`
    - `CountAsync(TableClient table, CancellationToken cancellationToken)`
13. `Storage/AuditRows.cs`: `ListAsync(TableClient table, CancellationToken cancellationToken)` returns every `TableEntity` in the current and previous `yyyyMM` partitions (two partition queries).
14. `Factories/ApiTestFactory.cs`:
    - Remove the test handler registration.
    - Call `TestIdentityProvider.ApplyApiSettings(builder)` and `builder.UseSetting("UserAccess:CacheTtl", "00:00:00")`. The cache is off by default in tests, so call counts are deterministic and no test can see another test's cached state (D8).
    - In `ConfigureTestServices`, call `TestIdentityProvider.ConfigureApi(services)`.
    - `CreateAuthenticatedClient(TestUser user)` sets the bearer header from `TestTokens.Create(user)`.
    - Add `CreateClientWithToken(string token)`.
    - Add `SeedUserAsync(TestUser user, string status, DateTimeOffset lastSignInAt, CancellationToken cancellationToken)` through `StorageAdminClient`.
15. `Factories/ProductionApiTestFactory.cs`: call `TestIdentityProvider.ApplyApiSettings(builder)`, and `TestIdentityProvider.ConfigureApi(services)` in `ConfigureTestServices`. Keep the initializer removal. Tests here never reach the access gate (D28).
16. `Factories/WebTestFactory.cs`:
    - Call `TestIdentityProvider.ApplyWebSettings(builder)`.
    - In `ConfigureTestServices`: `TestIdentityProvider.ConfigureWeb(services)`; `services.AddSingleton<IStartupFilter, TestCookieSignInStartupFilter>()`; and `services.AddSingleton(FakeTokenAcquisition.Create())` registered as `ITokenAcquisition`. `FakeTokenAcquisition.Create()` is an NSubstitute substitute whose six-parameter `GetAccessTokenForUserAsync` returns `TestTokens.Create(TestUsers.All.Single(u => u.ObjectId == oid from call.ArgAt<ClaimsPrincipal?>(4)))`.
    - Add `public void UseApi(ApiTestFactory api)`, which stores the API factory. When it is set, `ConfigureTestServices` adds `services.AddHttpClient<ISharpVaultApiClient, SharpVaultApiClient>().ConfigurePrimaryHttpMessageHandler(() => api.Server.CreateHandler());`.
    - Add `public async Task<HttpClient> CreateSignedInClientAsync(TestUser user, CancellationToken cancellationToken)`. It creates a web client (cookies on, which is the default) and calls `GET /test/sign-in/{oid}`.
17. `Support/TestCookieSignInStartupFilter.cs`: an `IStartupFilter` whose `Configure` maps the branch **before** `next(app)`, so the request runs before authentication and authorization: `app.Map("/test/sign-in", branch => branch.Run(SignInAsync)); next(app);`. `SignInAsync` parses the last path segment as a GUID, finds the `TestUsers` entry (404 if unknown), builds `new ClaimsIdentity(claims, "TestCookie", "name", "roles")` with the `oid`, `tid`, `name` and `preferred_username` claims, calls `context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity))`, and returns 204. This exercises the **real** cookie scheme; only the OIDC round trip is skipped (D12).
18. `Support/InterleavingUserStore.cs`: an `IUserStore` decorator around a real `TableUserStore`. Constructor `(IUserStore inner, Guid targetUserId, Func<CancellationToken, Task> afterFirstGet)`. After the first `GetAsync` for the target it awaits `afterFirstGet` once, then delegates everything.
19. `Support/ThrowingUserStore.cs`: every method throws `RequestFailedException(503, "Service unavailable")`.
20. `Factories/WebApiTestGroup.cs`: `[CollectionDefinition(nameof(WebApiTestGroup))] public sealed class WebApiTestGroup : ICollectionFixture<ApiTestFactory>, ICollectionFixture<WebTestFactory>;`
21. `ApiTests/BaseIntegrationTest.cs`:
    - Add `ArrangeCallerAsync(CallerKind caller)`. It seeds `Locked` rows (with `LastSignInAt` = now) for `LockedUser` and `LockedAdmin`, then returns `CallerClients.CreateFor(Factory, caller)`.
    - Add `GetUsersTable()` and `GetAuditTable()`.
    - Add `AssertNoQueryWithoutPartitionKey()`, which asserts `Assert.Empty(StorageRequests.QueriesWithoutPartitionKey())`.

**Acceptance**

- `TestInfrastructureTests` (7.4) pass.
- No class in `src/` references the test project.

---

### T11. Update the foundation tests (test-writer, after T10)

The fallback policy now requires a role and runs the lock check (D4), and the header handler is gone. Change exactly these tests and keep every other foundation test unchanged:

| File | Test | Change |
|---|---|---|
| `AuthorizationTests` | `UnknownRoute_AuthenticatedCaller_Returns404` | Replace with `UnknownRoute_UserRoleCaller_Returns404` (same theory data, caller `TestUsers.User`). Expect 404 or 405. Every recorded storage request targets `/Users` or `/AuditLog` only, and `QueriesWithoutPartitionKey` is empty |
| `AuthorizationTests` | new | `UnknownRoute_NoRoleCaller_Returns403WithoutStorageCalls` (same theory data, `AuthenticatedWithoutRole`, exactly 403 and 0 storage calls) |
| `AuthorizationTests` | `UnknownRoute_AuthenticatedCaller_Returns404Exactly` | Caller `TestUsers.User` |
| `AuthorizationTests` | `UnknownRoute_AnonymousCallerWithTestHeadersOfNobody_Returns401` | Use `SpoofedTestHeaders` |
| `AuthorizationTests` | `Endpoints_EveryRoute_IsAllowListedAnonymousOrHasExplicitPolicy` | A non-anonymous endpoint must carry `IAuthorizeData` whose `Policy` is `Policies.User` or `Policies.Admin` |
| `AuthorizationTests` | `FallbackPolicy_Configured_RequiresAuthenticatedUser`, `DefaultPolicy_Configured_RequiresAuthenticatedUser` | Also assert a `SharpVaultAccessRequirement` with `AcceptedRoles` exactly `[SharpVault.User, SharpVault.Admin]` |
| `ErrorHandlingTests` | `UnknownRoute_AuthenticatedCaller_ReturnsProblemDetailsBodyWithoutRequestedPath`, `UnknownRoute_AuthenticatedCaller_NotFoundResponseHasSecurityHeaders` | Caller `TestUsers.User` |
| `LoggingTests` | `UnhandledException_TestUserHeaders_AreNotLogged` | Replace with `ApiRequest_SignedInUserNameAndEmail_AreNotLogged` (7.5) |
| `HealthEndpointTests` | `HealthEndpoint_UnsupportedMethodWhenAuthenticated_Returns405WithoutStorageCall` | Split into `HealthEndpoint_UnsupportedMethodNoRoleCaller_Returns403WithoutStorageCall` (no storage calls) and `HealthEndpoint_UnsupportedMethodUserCaller_Returns405WithoutHealthStorageCall` (405, and recorded requests target `/Users` or `/AuditLog` only) |
| `TestInfrastructureTests` | the three `TestAuthenticationHandler_*` tests | Delete them. The replacements are in 7.4 |
| `ProductionHostingTests` | `UnknownRoute_ProductionTestAuthenticationHeaders_Returns401` | Use `SpoofedTestHeaders` |
| `HealthPerformanceTests` | `Startup_DefaultTableSet_CompletesUnder5SecondsWithNoStorageCalls` | Rename to `Startup_DefaultTableSet_CompletesUnder5SecondsWithOneCallPerTable`. The precondition becomes `Assert.Equal([TableNames.Users, TableNames.AuditLog], TableNames.All)`, with exactly 2 storage calls |
| `SecurityHeaderAssertions.AssertWeb` | | Expected CSP is `SecurityHeadersPolicy.ForWeb(TestIdentityProvider.InstanceUri).ContentSecurityPolicy` |
| `SecurityHeadersPolicyTests`, `SecurityHeadersMiddlewareTests` | every `ForWeb()` call | Use `ForWeb(new Uri("https://login.microsoftonline.com/"))`. The exact-string test expects `... form-action 'self' https://login.microsoftonline.com; upgrade-insecure-requests` |
| `WebHostTests` | `UnknownPage_Get_Returns404WithNotFoundPage`, `UnknownPage_Get_HasWebSecurityHeaders`, `WebHost_ApiDocumentationRoutes_AreNotServed` | Use a signed-in client (`CreateSignedInClientAsync(TestUsers.User)`). Add the anonymous variants from 7.7 |
| `WebHostTests` | `WebHost_SecretInQueryString_IsNotLogged` | Anonymous status is now 302. Also assert that the `Location` header does not contain the secret in plain text |
| `WebHostTests` | `UnhandledException_Web_*` | Use a signed-in client, because anonymous requests to `/test/throw` are now challenged |
| `WebHostTests` | `WebHealthEndpoints_PostRequest_IsNotServed` | Assert the status is not 200 and the body is not `Healthy` |

---

### T12. Unit tests (test-writer, after T4, T5, T6 and T9)

Write every test in 7.2 and 7.3.

### T13. API integration and security tests (test-writer, after T10)

Write every test in 7.5 and 7.6.

### T14. Web tests (test-writer, after T10)

Write every test in 7.7.

### T15. Performance tests (test-writer, after T10)

Write every test in 7.8.

### T16. Documentation (docs-writer, after all)

- `docs/ENTRA-SETUP.md` (new):
  - **API registration**:
    - expose the scope `access_as_user`;
    - app roles `SharpVault.User` and `SharpVault.Admin` with `allowedMemberTypes` `["User"]` only, so applications can never hold them;
    - `api.requestedAccessTokenVersion` = 2;
    - optional access token claims `acct` (**required**: without it everyone gets 403, D5), `idtyp` and `email`;
    - Enterprise application "Assignment required" = Yes, and assign roles to users or groups.
  - **Web registration**: redirect URIs `https://localhost:7190/signin-oidc` and the deployed host, the front-channel logout URL, the delegated permission `access_as_user` with admin consent, and the credential (a managed identity federated credential in Azure; locally a client secret in user-secrets only).
  - **Local setup**: the `dotnet user-secrets set` commands for `AzureAd:TenantId` and `AzureAd:ClientId` on `src/SharpVault.Api`, and for `AzureAd:TenantId`, `AzureAd:ClientId`, `AzureAd:ClientCredentials:0:SourceType` = `ClientSecret`, `AzureAd:ClientCredentials:0:ClientSecret` and `SharpVaultApi:Scopes:0` on `src/SharpVault.Web`.
  - **Optional settings**: `Tenancy:AllowGuests` and the `UserAccess:*` keys with their bounds.
- `docs/STORAGE.md`: document the `Users` table (PK `user`, RK oid, the columns, not encrypted per D9) and the `AuditLog` table (PK `yyyyMM` UTC, RK inverted ticks `_` id, columns, insert only). Remove "(none yet)".
- `docs/TESTING.md`: describe `TestTokens`, `TestIdentityProvider` (static metadata, no network), `TestSigningKeys`, the new `TestUsers` and `CallerKind` values, `ArrangeCallerAsync`, the removal of the header handler (D12), `TestCookieSignInStartupFilter`, the `FakeTokenAcquisition`, `WebApiTestGroup`, the test default `UserAccess:CacheTtl=0`, and the margin rule for token lifetimes (D29).
- `docs/LOCAL-DEV.md`: Entra setup is required before the AppHost can run the Api and the Web (link ENTRA-SETUP.md). Use the https profile for sign-in.
- `README.md`: an authentication section with the link, and the prerequisites in Getting Started.
- `docs/PROGRESS.md`:
  - Move entra-sign-in to Done, with requirement ids.
  - Add these follow-ups:
    - user-lock calls `IUserAccessCache.Invalidate` and audits denied attempts by locked users (D30);
    - LCK-3 session ending in the Web (D31);
    - admin-settings replaces `ConfiguredGuestAccessPolicy` (D5);
    - deployment: Data Protection keys, a distributed token cache, forwarded headers and the managed identity federated credential (D24, D25);
    - after encryption-core merges, add gate and token-validation benchmarks (D27);
    - maui-app replaces the sign-out form with an MSAL sign-out (D14).
  - Record that foundation follow-ups D4 and D21 are resolved.

## 6. Threats

Every threat has at least one named test in section 7. Assume each one exists until its test passes. Surfaces:

- API: `/api/me`, `/api/admin/users`, unknown routes, the health and documentation endpoints with tokens, the JwtBearer pipeline and the access gate.
- Web: `/account/sign-in`, `/account/sign-out`, `/signin-oidc`, `/signout-callback-oidc`, the account pages, unknown routes, static assets and `/_blazor`.
- Storage: the Users and AuditLog stores.
- Configuration: the Api and Web startup guards.

### 6.1 API authentication (401)

| # | Threat | Mitigation | Proving tests |
|---|---|---|---|
| E1 | Forged token with alg `none` | RS256 only, signed tokens required | `ApiRequest_AlgNoneToken_Returns401`, `UnknownRoute_ProductionAlgNoneToken_Returns401` |
| E2 | Token signed with an unknown key, including a known `kid` on another key | Only the metadata keys | `ApiRequest_TokenSignedWithUnknownKey_Returns401`, `ApiRequest_TokenWithKnownKeyIdButOtherKey_Returns401`, `UnknownRoute_ProductionTokenSignedWithUnknownKey_Returns401` |
| E3 | Key confusion: HS256 signed with the RSA public key bytes | `ValidAlgorithms = [RS256]` | `ApiRequest_HmacTokenSignedWithPublicKeyBytes_Returns401` |
| E4 | Expired token, not-yet-valid token or no `exp` | Lifetime validation with a 2-minute skew | `ApiRequest_ExpiredToken_Returns401`, `ApiRequest_NotYetValidToken_Returns401`, `ApiRequest_TokenWithoutExpiry_Returns401` |
| E5 | Wrong audience: another API, Microsoft Graph, or the Web's ID token replayed | Microsoft.Identity.Web audience validation (bare client id, v2) | `ApiRequest_WrongAudience_Returns401` |
| E6 | Wrong issuer: another tenant, the v1 issuer of our tenant, `common`, a lookalike host | `ValidIssuers = [tenant v2 issuer]` | `ApiRequest_WrongIssuer_Returns401` |
| E7 | Wrong tenant through `tid`: our issuer with a foreign `tid` (accepted by the library, V3), a missing `tid`, two `tid` claims, a non-GUID `tid` | Explicit `tid` check in `OnTokenValidated` (TEN-2) | `ApiRequest_TenantClaimMismatch_Returns401`, matrix row `OtherTenantUser` |
| E8 | App-only token (client credentials) carrying an app role; a token without `access_as_user`; a token with only another scope | `scp` must contain `access_as_user`; `idtyp=app` rejected; roles allowed for users only (ENTRA-SETUP) | `ApiRequest_AppOnlyToken_Returns401`, `ApiRequest_TokenWithoutAccessAsUserScope_Returns401` |
| E9 | `oid` missing, malformed, empty GUID or duplicated | Exactly one canonical non-empty GUID | `ApiRequest_InvalidObjectId_Returns401` |
| E10 | Token sent in a query string or cookie | Header only (JwtBearer default) | `ApiRequest_TokenInQueryStringOrCookie_Returns401` |
| E11 | Validation details (IDX codes, key ids, issuer) leak in the challenge or body | `IncludeErrorDetails = false`; status-only ProblemDetails | `ApiRequest_RejectedToken_ChallengeIsBareBearerAndBodyHasNoDetails`, `UnknownRoute_ProductionUnauthorizedResponse_DoesNotLeakValidationDetails` |
| E12 | Token, display name or email in logs | Only enum reasons and user ids are logged; no PII logging | `ApiRequest_ValidToken_TokenNotLogged`, `ApiRequest_RejectedTokens_TokenNotLogged`, `ApiRequest_SignedInUserNameAndEmail_AreNotLogged` |
| E13 | Misconfiguration: multi-tenant `common`, a domain name, an http instance, a non-GUID client id | Startup guard with constant messages (TEN-1) | `Read_InvalidSettings_ThrowsWithoutEchoingValue` (unit), `Startup_InvalidEntraSettings_FailsWithoutEchoingValue` (Production) |
| E14 | Test authentication headers spoofed in Development or Production | No header handler exists (D12); architecture test forbids `AuthenticationHandler<>` in `src` | `ApiRequest_TestHeadersWithoutToken_Returns401`, `ApiRequest_TestRoleHeaderWithNoRoleToken_Returns403`, `UnknownRoute_ProductionTestAuthenticationHeaders_Returns401`, `ProductionAssemblies_Types_DeclareNoAuthenticationHandler` |

### 6.2 API authorization (403) and the lock check

| # | Threat | Mitigation | Proving tests |
|---|---|---|---|
| A1 | B2B guest uses the app (`acct=1`, a foreign `idp`, or `acct` missing) | `AccountClassifier`; default deny; the `IGuestAccessPolicy` hook (TEN-3) | `ApiRequest_GuestToken_Returns403`, `ApiRequest_GuestTokenWhenGuestsAllowed_Returns200`, `ApiRequest_GuestTokenWhenGuestsAllowedButNoRole_Returns403` |
| A2 | Tenant member without a SharpVault role (TEN-4) | `SharpVaultAccessRequirement` in every policy, including the fallback | matrix row `AuthenticatedWithoutRole`, `UnknownRoute_NoRoleCaller_Returns403WithoutStorageCalls` |
| A3 | Role claim injection through other claim types or forms: `role`, `wids`, `groups`, `scp`, the URI role claim, wrong case, padded value, comma-joined string | `MapInboundClaims=false`; `RoleClaimType="roles"`; ordinal `IsInRole` (V4) | `AdminEndpoint_RoleInjectedThroughOtherClaim_Returns403`, `UserEndpoint_RoleInjectedThroughOtherClaim_Returns403` |
| A4 | A User calls admin endpoints | `Policies.Admin` | matrix row `User` on `/api/admin/users` |
| A5 | A locked user or admin keeps using a valid token (LCK-1) | The gate runs on every authorized request, including unknown routes | matrix rows `LockedUser` and `LockedAdmin`, `LockedUser_ExistingValidToken_RejectedOnNextRequest` |
| A6 | Lock race: a lock lands between the gate read and the sign-in write, and the sign-in overwrites it | Replace with the ETag; on conflict, re-read and decide; the audit is written only by the winner | `SignInRecord_LockWrittenBetweenReadAndReplace_KeepsLockAndReturns403`, `CheckAsync_ReplaceConflictThenLocked_ReturnsDenied` |
| A7 | A stale cache keeps a locked user in | TTL at most 30 s (default 5 s); validity by `TimeProvider`; `Invalidate` hook | `LockedUser_CachedActiveState_DeniedOnceTtlExpires`, `CheckAsync_LockedAfterCaching_DeniedOnceTtlExpires`, `CheckAsync_Invalidated_ReadsFreshStatus` |
| A8 | Cache poisoning or exhaustion | The key is only the validated `oid`; values only from storage or our own writes; nothing cached after exceptions; size limit | `CheckAsync_StoreThrows_DoesNotCacheAndPropagates`, `CheckAsync_TwoUsers_DoNotShareCacheEntries`, `UserAccessCache_MoreEntriesThanLimit_StaysBounded`, `UserAccessCache_ZeroTtl_StoresNothing` |
| A9 | A storage outage grants access | Exceptions propagate to the exception handler: 500 with no details, never success | `ApiRequest_UsersTableUnavailable_FailsClosedWithoutDetails` |
| A10 | Reason or existence leak: different 403 bodies for locked, no role and guest; an admin list cursor used as an oracle by non-admins | One ProblemDetails shape; authorization runs before validation | `ForbiddenResponses_LockedNoRoleAndGuest_AreIdentical`, `AdminUsers_NonAdminWithAnyCursor_Returns403BeforeValidation` |
| A11 | Email or UPN spoofing: two accounts with the same email, or an email changed to impersonate | Identity is `oid` only; `ICurrentUser` has no email | `ApiRequest_TwoUsersWithSameEmail_AreSeparateUsers`, `ApiRequest_EmailChangedForSameObjectId_UpdatesSameRow`, `TryRead_EmailClaim_NeverAffectsUserId` |
| A12 | An admin sees vault data of others (VIS-2) | The list DTO holds only directory data, status and last sign-in | `AdminUsers_Response_ContainsOnlyDirectoryAndStatusFields` |
| A13 | A future endpoint forgets a policy | Fallback and default are the User policy; the inventory test requires an explicit SharpVault policy | `Endpoints_EveryRoute_IsAllowListedAnonymousOrHasExplicitPolicy`, `FallbackPolicy_Configured_RequiresSharpVaultUserAccess` |
| A14 | The gate runs twice per request (storage amplification) | Decision memoized in `HttpContext.Items` | `GetMe_CacheDisabled_MakesExactlyOneUsersReadPerRequest`, `HandleRequirementAsync_TwoRequirementsSameRequest_CallsGateOnce` |
| A15 | Locked account storage-read amplification: a locked user's `LastSignInAt` never advances (D30), so once the sign-in interval has passed every request with a valid token would bypass the cache and point-read `Users` | A cached non-`Active` entry is denied from the cache before `IsSignInDue` is checked, with no storage read and no sign-in write; the cache TTL bounds unlock latency (D33) | `CheckAsync_CachedLockedUserAfterSignInInterval_DeniesWithoutStorageCalls` |

### 6.3 Input, denial of service and audit

| # | Threat | Mitigation | Proving tests |
|---|---|---|---|
| D1 | Admin list abuse: page size 0, negative, 101, huge or non-numeric; cursor OData injection, a non-GUID or a 10 KB cursor | Validator with constant messages; GUID cursor built into the filter with `CreateQueryFilter` | `AdminUsers_InvalidPageSize_Returns400`, `AdminUsers_InvalidContinuationToken_Returns400WithoutEchoingInput`, `Validate_*` (unit) |
| D2 | Table scans | Point reads and a `PartitionKey` filter only | `AdminUsers_ListPage_QueriesWithPartitionKeyOnly`, and every integration test in 7.5 asserts `QueriesWithoutPartitionKey` is empty |
| D3 | Storage amplification by callers that are denied anyway | 401 and 403 before the gate make 0 storage calls | `DeniedCallers_AnyEndpoint_MakeNoStorageCalls`, `DeniedRequests_EachUnder50MillisecondsWithoutStorageCalls` |
| D4 | Write amplification from bursts or parallel first requests | Throttle by `SignInRecordInterval`; insert and ETag conflicts pick one winner | `TwentyRequestsWithinInterval_WriteOneSignInAudit`, `TwentyParallelFirstRequests_CreateOneUserRowAndOneSignInAudit` |
| D5 | Oversized or hostile claim text: a 10,000-character name, control characters, a lone surrogate | `UserProfileText` truncates and strips | `ApiRequest_OversizedDisplayName_StoredTruncated`, `NormalizeDisplayName_*` (unit) |
| U1 | Sign-in not audited (AUD-2) | The gate appends `SignIn` after the winning write | `FirstRequest_NewUser_WritesOneSignInAuditEntry`, `RequestAfterSignInInterval_WritesNewSignInAuditEntry` |
| U2 | The audit entry holds a token, name, email or secret (AUD-3) | Ids, event type, time and IP only | `SignInAuditEntry_Content_HasOnlyIdsTimeEventAndIp` |
| U3 | Audit tampering through the app (AUD-1) | Interface with one append method; insert only; source scan | `AuditLog_Interface_ExposesOnlyAppend`, `TableAuditLog_Source_UsesNoUpdateUpsertOrDelete`, `AppendAsync_DuplicateKeys_FailsInsteadOfOverwriting` |
| U4 | IP spoofing with `X-Forwarded-For` | Connection address only (D25) | `SignInAudit_XForwardedForHeader_IsNotRecorded` |
| U5 | A locked user's attempts update the last sign-in or write sign-in entries | The locked branch writes nothing | `LockedUser_Request_DoesNotRecordSignInOrUpdateRow` |
| C1 | A stale ETag overwrites newer data | Replace with `ifMatch` | `ReplaceAsync_StaleETag_ReturnsConflictAndChangesNothing` |
| C2 | A duplicate insert overwrites a row | `AddEntity`, 409 returns `AlreadyExists` | `AddAsync_ExistingUser_ReturnsAlreadyExistsAndKeepsRow` |
| C3 | An unknown status value in storage grants access | `UserStatusText.Parse` fails closed | `Parse_UnknownOrMissingStatus_ReturnsLocked`, `GetMe_RowWithUnknownStatus_Returns403` |

### 6.4 Web

| # | Threat | Mitigation | Proving tests |
|---|---|---|---|
| W1 | Anonymous access to protected pages or route enumeration | Fallback `RequireAuthenticatedUser`; challenge to the **tenant-specific** authorize endpoint | `AccountPage_Anonymous_RedirectsToTenantAuthorizeEndpoint`, `UnknownPage_Anonymous_RedirectsToSignIn` |
| W2 | OIDC downgrade: implicit flow, no PKCE, no state or nonce | Code flow, PKCE S256, state and nonce (V6) | `SignIn_Challenge_UsesCodeFlowWithPkceStateAndNonce`, `OpenIdConnectOptions_Configured_UseCodeFlowPkceAndNoSavedTokens` |
| W3 | Open redirect through the sign-in `returnUrl` | `ReturnUrls.Sanitize` | `SignIn_ExternalReturnUrl_StoresHomeAsRedirect`, `Sanitize_*` (unit) |
| W4 | Open redirect through sign-out | Fixed target `/account/signed-out` | `SignOut_Valid_RedirectsToEndSessionWithFixedPostLogoutTarget` |
| W5 | Logout CSRF | Explicit antiforgery validation; POST only | `SignOut_WithoutAntiforgeryToken_Returns400AndKeepsSession`, `SignOut_GetRequest_DoesNotSignOut` |
| W6 | Session cookie theft or cross-site use | `__Host-` prefix, Secure, HttpOnly, SameSite=Lax, 8 hours absolute | `SessionCookie_Issued_IsHostPrefixedSecureHttpOnlyLax`, `CookieOptions_Configured_EightHourAbsoluteLifetime` |
| W7 | Access tokens reach the browser | `SaveTokens=false`; MSAL cache server side and encrypted; no WebAssembly | `AccountPage_Rendered_ContainsNoAccessToken`, `TokenCache_Configured_IsEncrypted` |
| W8 | Reflected error text from the identity provider (phishing or XSS) | Constant error page; remote failure redirect (V8) | `SignInCallback_ErrorWithDescription_RedirectsToConstantErrorPage`, `SignInErrorPage_QueryText_IsNotRendered`, `SignInCallback_ForgedState_RedirectsToErrorPage` |
| W9 | An ID token from another tenant is accepted at Web sign-in | `ValidIssuers` plus `CheckIdToken` | `CheckIdToken_*` (unit), `OpenIdConnectOptions_Configured_AcceptOnlyTenantIssuer` |
| W10 | A client secret is used in production | `WebCredentialGuard` | `Startup_WebProductionWithClientSecret_FailsWithoutEchoingSecret`, `Startup_WebProductionWithoutCredential_Fails`, `EnsureAllowed_*` (unit) |
| W11 | The Web sends the token to the wrong host, logs it, or mishandles 401 and 403 | Relative path on the configured base; status mapping; no token in logs | `GetCurrentUserAsync_*` (unit), `AccountPage_LockedUser_RedirectsToAccessDenied` (Web to API) |
| W12 | Form submission or redirect to another origin | CSP `form-action 'self' https://login.microsoftonline.com` | `ForWeb_IdentityProviderOrigin_AllowsOnlySelfAndThatOriginForFormAction`, `HomePage_Response_HasWebSecurityHeaders` |
| W13 | Anonymous pages or assets blocked, or protected data shown on them | Explicit `[AllowAnonymous]` list; assets anonymous | `AnonymousPages_Get_Return200`, `StaticAssets_Anonymous_Return200` |
| W14 | An anonymous interactive circuit | `/_blazor` falls under the fallback policy | `BlazorHub_AnonymousNegotiate_IsChallenged` |
| W15 | Spoofed test headers on the Web | No header handler | `WebRequest_TestHeaders_DoNotAuthenticate` |

## 7. Tests

Conventions:

- Names follow `Scenario_Condition_ExpectedResult`.
- Security tests have `[Trait("Category", "Security")]`, performance tests `[Trait("Category", "Performance")]`.
- Every integration test class has `[Collection(...)]`.
- Assert exact status codes. Use xUnit `Assert` only.
- Pass `TestContext.Current.CancellationToken` to every async call.
- Never use `Task.Delay` or `Thread.Sleep`. Drive time with `FakeTimeProvider` (`new FakeTimeProvider(DateTimeOffset.UtcNow)`, so token lifetimes, which use the system clock, stay valid).
- Token lifetime tests use margins of at least 5 minutes beyond the 2-minute skew (D29).
- Every integration test that touches storage ends with `AssertNoQueryWithoutPartitionKey()`.
- Requirement ids go in the class XML doc comment where helpful, never as inline comments.

### 7.1 Authorization matrix (`ApiTests/AuthorizationMatrixTests.cs`, StorageTestGroup, Security)

`AuthorizationMatrix_EveryEndpointAndCaller_ReturnsExpectedStatus` is a theory over `(method, path, caller, expected)`, built from the table below. It calls `ArrangeCallerAsync(caller)`, sends the request, and asserts the exact status. For 401 and 403 it also asserts that `StorageRequests.Count` is 0.

| Caller | `GET /api/me` | `GET /api/admin/users` | `GET /does-not-exist` | `GET /health`, `GET /alive` | `GET /openapi/v1.json` (Development) |
|---|---|---|---|---|---|
| `Anonymous` | 401 | 401 | 401 | 200 | 200 |
| `AlgNoneToken` | 401 | 401 | 401 | 200 | 200 |
| `UnknownKeyToken` | 401 | 401 | 401 | 200 | 200 |
| `OtherTenantUser` | 401 | 401 | 401 | 200 | 200 |
| `Guest` | 403 | 403 | 403 | 200 | 200 |
| `AuthenticatedWithoutRole` | 403 | 403 | 403 | 200 | 200 |
| `User` | 200 | 403 | 404 | 200 | 200 |
| `Admin` | 200 | 200 | 404 | 200 | 200 |
| `LockedUser` | 403 | 403 | 403 | 200 | 200 |
| `LockedAdmin` | 403 | 403 | 403 | 200 | 200 |

Also in this file:

- `AuthorizationMatrix_ProductionPreStorageCallers_ReturnExpectedStatus` (ProductionTestGroup): the same theory for `Anonymous`, `AlgNoneToken`, `UnknownKeyToken`, `OtherTenantUser` (401) and `Guest`, `AuthenticatedWithoutRole` (403), on `/api/me` and `/api/admin/users`. Production cannot reach storage (D28).
- `HealthEndpoint_ValidUserToken_MakesNoUsersTableCall`: `GET /alive` with a `User` token records no request to `/Users`. Anonymous endpoints skip the gate.
- `DeniedCallers_AnyEndpoint_MakeNoStorageCalls`: a theory over the 401 and 403 rows that are not locked, crossed with the three API paths. 0 storage calls.

### 7.2 Unit tests: Application and Infrastructure (`tests/SharpVault.UnitTests`)

`Application/Users/UserAccessGateTests.cs` uses NSubstitute for `IUserStore` and `IAuditLog`, the **real** `UserAccessCache` with `FakeTimeProvider`, `Options.Create(new UserAccessOptions { CacheTtl = 5 s })` and a `CollectingLogger`. Tests marked (S) are Security.

- `CheckAsync_NewUser_AddsActiveRowThenAppendsSignInAndAllows`: `Received.InOrder` shows `AddAsync` before `AppendAsync`; the row has `Status` `Active` and `CreatedAt` = `LastSignInAt` = now.
- `CheckAsync_NewUserAddRaceLost_ReloadsAndDoesNotAudit` (S): `AddAsync` returns `AlreadyExists`, then the stored row is read; no `AppendAsync`.
- `CheckAsync_NewUserAddRaceLostToLockedRow_ReturnsDenied` (S)
- `CheckAsync_ActiveUserSignInNotDue_AllowsWithoutWrites`
- `CheckAsync_ActiveUserSignInDue_ReplacesWithETagThenAppendsSignIn`: `ReplaceAsync` gets the stored ETag; `Received.InOrder` shows the replace before the append.
- `CheckAsync_SignInDue_RefreshesDisplayNameAndEmailButNotStatus`
- `CheckAsync_ReplaceConflictThenLocked_ReturnsDenied` (S, A6)
- `CheckAsync_ReplaceConflictThenActive_AllowsWithoutAudit`
- `CheckAsync_ReplaceNotFoundAndRowGone_Throws`
- `CheckAsync_LockedUser_DeniesWithoutAnyWrite` (S, U5)
- `CheckAsync_LockedUser_LogsWarningWithUserIdOnly` (S): the log contains the id and neither the name nor the email.
- `CheckAsync_CachedActiveWithinTtl_MakesNoStoreCall`
- `CheckAsync_CachedLockedWithinTtl_DeniesWithoutStoreCall`
- `CheckAsync_LockedAfterCaching_DeniedOnceTtlExpires` (S, A7): advance by TTL + 1 ms.
- `CheckAsync_Invalidated_ReadsFreshStatus` (S, A7)
- `CheckAsync_CachedButSignInDue_ReadsAndRecords`
- `CheckAsync_CachedLockedUserAfterSignInInterval_DeniesWithoutStorageCalls` (S, A15): the store returns a `Locked` row whose `LastSignInAt` is now minus 2 hours (older than `SignInRecordInterval`). The first call reads it and caches it. Clear the substitutes' received calls, advance the clock by 1 s (inside the TTL), and call again: the result is `Denied`, and `GetAsync`, `AddAsync`, `ReplaceAsync` and `AppendAsync` each received 0 calls.
- `CheckAsync_StoreThrows_DoesNotCacheAndPropagates` (S, A8)
- `CheckAsync_AuditAppendThrows_LogsErrorAndPropagates`
- `CheckAsync_TwoUsers_DoNotShareCacheEntries` (S)
- `CheckAsync_IpAddress_IsNormalizedIntoAuditEntry` (Theory: `"10.0.0.1"`, `"::1"`, `null`, `"not-an-ip"` becomes null, a 100-character string becomes null)
- `CheckAsync_AuditEntry_HasNoNameOrEmail` (S, U2)
- `CheckAsync_PassesCancellationTokenToEveryStoreCall`

`Application/Users/UserProfileTextTests.cs`:

- `NormalizeDisplayName_NullOrWhitespace_ReturnsEmpty` (Theory)
- `NormalizeDisplayName_ControlCharacters_AreRemoved` (S)
- `NormalizeDisplayName_LongerThanMaximum_IsTruncatedTo256`
- `NormalizeDisplayName_TruncationAtSurrogatePair_DropsLoneHighSurrogate`
- `NormalizeEmail_WithoutAtSign_ReturnsNull`
- `NormalizeEmail_LongerThanMaximum_IsTruncated`
- `NormalizeIpAddress_Values_AreParsedOrNull` (Theory)

`Application/Users/UserAccessOptionsValidatorTests.cs`:

- `Validate_Defaults_Succeeds`
- `Validate_CacheTtlBounds_AcceptsZeroAndThirtySeconds`
- `Validate_CacheTtlOutOfRange_Fails` (Theory: -1 s, 31 s)
- `Validate_CacheMaxEntriesOutOfRange_Fails` (Theory: 99, 1,000,001)
- `Validate_SignInRecordIntervalOutOfRange_Fails` (Theory: 59 s, 24 h + 1 s)

`Application/Users/ListUsersTests.cs`:

- `Validate_PageSizeInRange_Passes` (Theory: 1, 50, 100)
- `Validate_PageSizeOutOfRange_FailsWithConstantMessage` (S; Theory: 0, -1, 101, `int.MaxValue`): the message does not contain the value.
- `Validate_ContinuationTokenNull_Passes`
- `Validate_CanonicalGuidContinuationToken_Passes`
- `Validate_InvalidContinuationToken_FailsWithoutEchoingInput` (S; Theory: `""`, `" "`, `"' or PartitionKey ne '"`, `"{guid}"` with braces, a GUID in `N` format, 10,000 characters, `"\u0000"`, `"../x"`)
- `Handle_FirstPage_PassesNullCursorAndMapsUsers`
- `Handle_WithCursor_PassesParsedGuid`
- `Handle_PageWithNextCursor_ReturnsCanonicalContinuationToken`
- `Handle_LastPage_ReturnsNullContinuationToken`

Other Application tests:

- `Application/Users/GetCurrentUserTests.cs`: `Handle_SignedInUser_ReturnsIdNameAndAdminFlagFromCurrentUser`.
- `Application/Behaviors/ValidationBehaviorTests.cs`: `Handle_NoValidators_CallsNext`, `Handle_ValidRequest_CallsNext`, `Handle_InvalidRequest_ThrowsValidationExceptionWithoutCallingNext`.
- `Application/Tenancy/ConfiguredGuestAccessPolicyTests.cs`: `AreGuestsAllowedAsync_Default_ReturnsFalse`, `AreGuestsAllowedAsync_Configured_ReturnsTrue`.
- `Application/Mapping/UserMappingRegisterTests.cs`: `Adapt_User_MapsToSummaryWithStatusName`, and `Compile_ApplicationMappings_Succeeds`, where `TypeAdapterConfig.Compile()` on a scanned config does not throw.

`Infrastructure/Users/UserAccessCacheTests.cs` (`FakeTimeProvider`):

- `TryGet_Empty_ReturnsFalse`
- `SetThenTryGet_WithinTtl_ReturnsEntry`
- `TryGet_AtTtl_ReturnsFalse`
- `UserAccessCache_ZeroTtl_StoresNothing` (S)
- `Invalidate_ExistingEntry_RemovesIt`
- `UserAccessCache_MoreEntriesThanLimit_StaysBounded` (S): with `CacheMaxEntries = 100`, set 1,000 entries and assert `Count <= 100`.

`Infrastructure/Users/UserStorageMappingTests.cs` (`new TypeAdapterConfig()` scanning the Infrastructure assembly):

- `Adapt_UserToEntity_SetsPartitionAndCanonicalRowKey`
- `Adapt_EntityToUser_RoundTripsEveryField`
- `Adapt_UserToEntity_StoresStatusAsName`
- `Parse_UnknownOrMissingStatus_ReturnsLocked` (S; Theory: `null`, `""`, `"active"`, `"Admin"`, `"1"`)
- `Compile_InfrastructureMappings_Succeeds`

`Infrastructure/Audit/AuditKeysTests.cs`:

- `PartitionKey_UsesUtcYearAndMonth`: an offset time just after midnight local is still in the previous UTC month.
- `RowKey_IsNineteenDigitsUnderscoreAndThirtyTwoHex`
- `RowKey_NewerEvent_SortsBeforeOlderEvent`
- `RowKey_SameInstantDifferentIds_AreDistinct`

Source and contract tests:

- `Infrastructure/Audit/AuditLogContractTests.cs` (Security): `AuditLog_Interface_ExposesOnlyAppend` (reflection: `IAuditLog` declares exactly one method, `AppendAsync`) and `TableAuditLog_Source_UsesNoUpdateUpsertOrDelete`, which scans `src/SharpVault.Infrastructure/Audit/TableAuditLog.cs` for `UpdateEntity`, `UpsertEntity`, `DeleteEntity`, `SubmitTransaction` and `ETag.All` using `RepositoryRoot`.
- `Infrastructure/Users/TableUserStoreSourceTests.cs` (Security): `TableUserStore_Source_NeverUsesETagAllOrUpsert`.

### 7.3 Unit tests: identity, Api and Web (`tests/SharpVault.UnitTests`)

`ServiceDefaults/Identity/EntraApplicationSettingsTests.cs` (Security):

- `Read_ValidSettings_ReturnsTenantClientAndIssuers`
- `Read_InvalidSettings_ThrowsWithoutEchoingValue` (Theory over field and value: TenantId `common`, `organizations`, `consumers`, `contoso.onmicrosoft.com`, `""`, `00000000-0000-0000-0000-000000000000`, and a GUID with braces; ClientId `abc`; Instance `http://login.microsoftonline.com/`, `https://login.microsoftonline.com/tenant/`, `https://user:pw@login.microsoftonline.com/`, `https://login.microsoftonline.com/?x=1`, a relative URI): the message is the matching constant and does not contain the value.
- `V2Issuer_Instance_UsesTenantSpecificV2Path`

`ServiceDefaults/Identity/EntraTokenChecksTests.cs` (Security), principals built from claim lists:

- `CheckAccessToken_ValidClaims_ReturnsValid`
- `CheckAccessToken_TenantMismatchMissingOrDuplicated_ReturnsWrongTenant` (Theory)
- `CheckAccessToken_InvalidObjectId_ReturnsInvalidObjectId` (Theory: missing, `"x"`, empty GUID, two claims, braces)
- `CheckAccessToken_AppIdentityType_ReturnsAppOnlyToken`
- `CheckAccessToken_ScopeMissingOtherOrPrefixOnly_ReturnsMissingScope` (Theory: no `scp`, `"User.Read"`, `"access_as_user_admin"`, `"ACCESS_AS_USER"`, two `scp` claims)
- `CheckAccessToken_ScopeAmongOthers_ReturnsValid` (`"User.Read access_as_user"`)
- `CheckAccessToken_NullPrincipal_ReturnsInvalid`
- `CheckIdToken_OtherTenant_ReturnsWrongTenant`
- `CheckIdToken_NoScope_ReturnsValid`
- `TokenCheckResult_Default_IsNotValid`

`ServiceDefaults/Identity/AccountClassifierTests.cs` (Security):

- `Classify_AcctZeroWithoutIdp_ReturnsMember`
- `Classify_AcctZeroWithOwnTenantIdp_ReturnsMember` (Theory: v1 and v2 issuer)
- `Classify_AcctOne_ReturnsGuest`
- `Classify_AcctZeroWithForeignIdp_ReturnsGuest` (Theory: another tenant, `live.com`, `mail`)
- `Classify_AcctMissingOrUnexpected_ReturnsUnknown` (Theory: missing, `"2"`, `""`, two claims)
- `AccountKind_Default_IsUnknown`

`ServiceDefaults/SecurityHeadersPolicyTests.cs` (additions):

- `ForWeb_IdentityProviderOrigin_AllowsOnlySelfAndThatOriginForFormAction`
- `ForWeb_InstanceWithPath_UsesOriginOnly`
- `ForWeb_HttpOrRelativeOrigin_Throws`

`Api/Security/SignedInIdentityReaderTests.cs` (Security):

- `TryRead_ValidPrincipal_ReadsObjectIdNameAndAdminFlag`
- `TryRead_EmailClaim_NeverAffectsUserId`: same oid with different emails gives the same id; different oids with the same email give different ids.
- `TryRead_EmailMissing_FallsBackToPreferredUsername`
- `TryRead_InvalidObjectId_ReturnsFalse` (Theory)
- `TryRead_RoleClaimOtherThanRoles_IsNotAdmin` (Theory: `role`, `wids`, `groups`, the URI role claim)

`Api/Security/SharpVaultAccessHandlerTests.cs` (Security). Build the `AuthorizationHandlerContext` with a `DefaultHttpContext` as the resource and NSubstitute for `IUserAccessGate` and `IGuestAccessPolicy`.

- `HandleRequirementAsync_Anonymous_NeitherSucceedsNorCallsGate`
- `HandleRequirementAsync_Guest_FailsWithoutCallingGate`
- `HandleRequirementAsync_GuestAllowedWithRole_CallsGate`
- `HandleRequirementAsync_MissingRole_FailsWithoutCallingGate`
- `HandleRequirementAsync_AdminRoleForUserPolicy_Succeeds`
- `HandleRequirementAsync_UserRoleForAdminPolicy_Fails`
- `HandleRequirementAsync_GateDenied_Fails`
- `HandleRequirementAsync_GateAllowed_Succeeds`
- `HandleRequirementAsync_TwoRequirementsSameRequest_CallsGateOnce` (A14)
- `HandleRequirementAsync_ForwardedForHeader_PassesConnectionAddressOnly` (U4)
- `HandleRequirementAsync_PassesRequestAbortedToken`

`Api/Security/HttpContextCurrentUserTests.cs`:

- `UserId_SignedIn_ReturnsObjectId`
- `UserId_NoUser_ThrowsInvalidOperationException`
- `DisplayName_ControlCharacters_AreNormalized`

`Api/Errors/ValidationExceptionHandlerTests.cs`:

- `TryHandleAsync_ValidationException_Writes400WithMessagesOnly`
- `TryHandleAsync_OtherException_ReturnsFalse`

Web unit tests (`extern alias WebApp`):

- `Web/ReturnUrlsTests.cs` (Security):
  - `Sanitize_LocalPath_IsKept` (Theory: `/`, `/account`, `/account?x=1`, `/account#a`)
  - `Sanitize_UnsafeValue_ReturnsRoot` (Theory: null, `""`, `https://evil.example`, `//evil.example`, `/\evil.example`, `\\evil.example`, `/a\b`, `javascript:alert(1)`, `account`, `/a\u0000b`, `/a\r\nLocation: x`, 2,049 characters, `http:/evil.example`)
- `Web/WebCredentialGuardTests.cs` (Security):
  - `EnsureAllowed_DevelopmentWithClientSecret_DoesNotThrow`
  - `EnsureAllowed_ProductionWithManagedIdentityCredential_DoesNotThrow`
  - `EnsureAllowed_ProductionWithClientSecret_ThrowsWithoutEchoingSecret` (Theory: legacy `ClientSecret` key; a credential with `SourceType` `ClientSecret`; with `clientsecret` casing; a `ClientSecret` value on a managed identity entry)
  - `EnsureAllowed_ProductionWithoutCredential_Throws`
- `Web/SharpVaultApiOptionsValidatorTests.cs`:
  - `Validate_Valid_Succeeds`
  - `Validate_MissingOrRelativeBaseAddress_Fails`
  - `Validate_NoScopes_Fails`
  - `Validate_WhitespaceScope_Fails`
- `Web/SharpVaultApiClientTests.cs` (Security). Use NSubstitute `ITokenAcquisition`, a stub `HttpMessageHandler` that records requests, a stub `AuthenticationStateProvider`, and `CollectingLogger`:
  - `GetCurrentUserAsync_SignedIn_SendsBearerTokenToConfiguredBaseAddress`: the request URI equals `BaseAddress` + `api/me`.
  - `GetCurrentUserAsync_SignedIn_RequestsConfiguredScopesForThatPrincipal`
  - `GetCurrentUserAsync_Anonymous_ReturnsSignInRequiredWithoutTokenOrHttpCall`
  - `GetCurrentUserAsync_ChallengeException_ReturnsSignInRequiredWithoutHttpCall`: throw `new MicrosoftIdentityWebChallengeUserException(new MsalUiRequiredException("code", "message"), ["scope"])`.
  - `GetCurrentUserAsync_Api200_ReturnsProfile`
  - `GetCurrentUserAsync_Api200NullBody_ReturnsFailed`
  - `GetCurrentUserAsync_Api401_ReturnsSignInRequired`
  - `GetCurrentUserAsync_Api403_ReturnsAccessDenied`
  - `GetCurrentUserAsync_Api500_ReturnsFailedAndLogsStatusOnly`
  - `GetCurrentUserAsync_AnyOutcome_TokenIsNotLogged`
  - `ApiCallStatus_Default_IsFailed`

`Architecture/LayeringTests.cs` (additions):

- `Application_ReferencedAssemblies_ContainNoIdentityWeb`: no `Microsoft.Identity.*`.
- `UiShared_ReferencedAssemblies_ContainNoIdentityWeb`
- `Domain_Types_HaveNoMethodsBeyondProperties`: `User` and `AuditEntry` declare only property accessors and constructors.

`Architecture/RepositoryConfigurationTests.cs`: add the forbidden fragments `"client_secret"` and `"BEGIN PRIVATE KEY"`.

### 7.4 Test infrastructure self-tests (`ApiTests/TestInfrastructureTests.cs`, StorageTestGroup)

- `TestTokens_ValidUser_IsAcceptedByApi`: `GET /api/me` with a `User` token returns 200, which proves the static metadata works with no network.
- `TestTokens_DefaultClaims_MatchEntraV2AccessTokenShape`: decode the payload and check `ver` 2.0, `aud` is the client id GUID, `iss` is the tenant v2 issuer, `scp`, `acct`, `roles` as an array, and `nbf` < `exp`.
- `TestTokens_Guest_HasAcctOneAndForeignIdp`
- `UserRows_SeededRow_IsReadByApi`: seed a `Locked` row and expect 403. This catches column name drift between `UserColumns` and `UserTableEntity`.
- `SpoofedTestHeaders_Added_DoNotAuthenticate`: headers only gives 401.
- Keep `ForgedTokens_AlgNone_HasThreeSegmentsAndEmptySignature` and `ForgedTokens_SignedWithUnknownKey_DiffersBetweenCalls`.

### 7.5 API integration and security tests (StorageTestGroup unless noted)

`ApiTests/TokenValidationTests.cs` (Security). Every test asserts exactly 401 and 0 storage calls.

- `ApiRequest_AlgNoneToken_Returns401`
- `ApiRequest_TokenSignedWithUnknownKey_Returns401`
- `ApiRequest_TokenWithKnownKeyIdButOtherKey_Returns401` (`TestSigningKeys.OtherWithSameKeyId`)
- `ApiRequest_HmacTokenSignedWithPublicKeyBytes_Returns401`: HS256 keyed with `TestSigningKeys.Primary`'s exported public key, `kid` set.
- `ApiRequest_ExpiredToken_Returns401`: `exp` = now - 10 min, `nbf` = now - 2 h.
- `ApiRequest_NotYetValidToken_Returns401`: `nbf` = now + 10 min.
- `ApiRequest_TokenWithoutExpiry_Returns401`
- `ApiRequest_WrongAudience_Returns401` (Theory: a random GUID, `TestWebApp.ClientId`, `00000003-0000-0000-c000-000000000000`, `api://{TestApi.ClientId}`)
- `ApiRequest_WrongIssuer_Returns401` (Theory: `Issuer(TestTenant.OtherId)`, `https://sts.windows.net/{tid}/`, `https://login.microsoftonline.com/common/v2.0`, `https://login.microsoftonline.com.evil.example/{tid}/v2.0`, `https://login.microsoftonline.com/{tid}/v2.0/`)
- `ApiRequest_TenantClaimMismatch_Returns401` (Theory: `tid` = other tenant with our issuer; `tid` removed; `tid` as a two-element array; `tid` = `"not-a-guid"`)
- `ApiRequest_AppOnlyToken_Returns401`: `scp` removed, `idtyp` = `app`, `roles` = `[SharpVault.Admin]`.
- `ApiRequest_TokenWithoutAccessAsUserScope_Returns401` (Theory: `scp` removed with roles kept, `User.Read`, `access_as_user_admin`)
- `ApiRequest_InvalidObjectId_Returns401` (Theory: removed, `"abc"`, empty GUID, two-element array)
- `ApiRequest_TokenInQueryStringOrCookie_Returns401` (Theory: `?access_token=`, `Cookie: access_token=`)
- `ApiRequest_RejectedToken_ChallengeIsBareBearerAndBodyHasNoDetails`: `WWW-Authenticate` equals `Bearer` exactly; the body has no `IDX`, `kid`, issuer or key id; ProblemDetails content type.
- `ApiRequest_RejectedTokens_TokenNotLogged`: for each rejected token above, no token segment is in `Logs`.
- `ApiRequest_ValidToken_TokenNotLogged`

`ApiTests/AccessControlTests.cs` (Security):

- `ApiRequest_GuestToken_Returns403` (Theory: `acct` = 1; `acct` = 0 with a foreign `idp`; `acct` removed)
- `ApiRequest_GuestTokenWhenGuestsAllowed_Returns200` (a derived factory with `Tenancy:AllowGuests=true`)
- `ApiRequest_GuestTokenWhenGuestsAllowedButNoRole_Returns403`
- `AdminEndpoint_RoleInjectedThroughOtherClaim_Returns403`: a theory over tokens for the no-role user with `role` = `SharpVault.Admin`, `wids` = `[62e90394-69f5-4237-9190-012177145e10]`, `groups` = `[SharpVault.Admin]`, `scp` = `access_as_user SharpVault.Admin`, the URI role claim, `roles` = `[sharpvault.admin]`, `roles` = `[" SharpVault.Admin"]` and `roles` = `"SharpVault.User,SharpVault.Admin"`. Each gives 403 on `/api/admin/users`.
- `UserEndpoint_RoleInjectedThroughOtherClaim_Returns403`: the same tokens with the User role value give 403 on `/api/me`.
- `ApiRequest_TestHeadersWithoutToken_Returns401`
- `ApiRequest_TestRoleHeaderWithNoRoleToken_Returns403`
- `LockedUser_ExistingValidToken_RejectedOnNextRequest` (LCK-1): with a `User` token, `/api/me` returns 200; then set `Status` = `Locked` through `StorageAdminClient` (Replace); the next request returns 403.
- `LockedUser_Request_DoesNotRecordSignInOrUpdateRow`: seed a locked row with `LastSignInAt` 2 h ago; the request returns 403; the row's ETag and `LastSignInAt` are unchanged and there are no audit rows.
- `LockedUser_CachedActiveState_DeniedOnceTtlExpires` (A7): a derived factory with `UserAccess:CacheTtl=00:00:05` and a `FakeTimeProvider`.
  1. The first request returns 200 and the row is cached.
  2. Lock the row in storage.
  3. A request now still returns 200. This documents the staleness budget.
  4. `fakeTime.Advance(5 s + 1 ms)`.
  5. The next request returns 403.
- `SignInRecord_LockWrittenBetweenReadAndReplace_KeepsLockAndReturns403` (A6): a derived factory registers `InterleavingUserStore` around the real store for `TestUsers.User`. Seed an `Active` row with `LastSignInAt` 2 h ago. The callback locks the row through `StorageAdminClient`. Expect 403, the row still `Locked`, and no audit rows.
- `ForbiddenResponses_LockedNoRoleAndGuest_AreIdentical` (A10): for `/api/me`, the three responses have the same status, content type, header names, and the same JSON `type`, `title` and `status`. Only `traceId` differs.
- `AdminUsers_NonAdminWithAnyCursor_Returns403BeforeValidation`: a theory with `continuationToken` values that are a valid GUID, garbage, and an OData injection. Each gives 403, never 400.
- `ApiRequest_UsersTableUnavailable_FailsClosedWithoutDetails` (A9): a derived factory with `ThrowingUserStore`. Expect 500, a ProblemDetails body without `RequestFailedException`, `Users` or a stack trace, and never 200.
- `GetMe_RowWithUnknownStatus_Returns403` (C3): seed `Status` = `"Disabled"`.
- `ApiRequest_TwoUsersWithSameEmail_AreSeparateUsers` (A11): two oids with the same email claim give two rows, and each `/api/me` returns its own id.
- `ApiRequest_EmailChangedForSameObjectId_UpdatesSameRow`: with `FakeTimeProvider` past the interval, the second token has a new email; the row count stays 1 and `Email` is updated.
- `ApiRequest_SignedInUserNameAndEmail_AreNotLogged` (E12): unique name and email values; `/api/me` returns 200; neither value is in `Logs`.
- `ApiRequest_OversizedDisplayName_StoredTruncated` (D5): a 10,000-character name with `\u0007`. Expect 200, a stored `DisplayName` of length 256 without control characters, and `/api/me` returning the normalized name.

`ApiTests/UserProvisioningTests.cs`:

- `FirstRequest_NewUser_CreatesActiveRowWithClaimsData` (TEN-5): the row has the oid row key, name, email, `Active`, and `CreatedAt` = `LastSignInAt`.
- `FirstRequest_NewUser_WritesOneSignInAuditEntry` (AUD-2): one audit row with `EventType` `SignIn`, `ActorId` = oid, PK = the current `yyyyMM`, a valid RK format, and no `TargetId`.
- `SignInAuditEntry_Content_HasOnlyIdsTimeEventAndIp` (Security, U2): the property names are exactly `EventType`, `ActorId`, `OccurredAt`, plus the null-allowed `IpAddress`, `TargetId` and `Reason`. No value contains the token, name or email.
- `TwentyRequestsWithinInterval_WriteOneSignInAudit` (Security, D4)
- `RequestAfterSignInInterval_WritesNewSignInAuditEntry`: with `FakeTimeProvider`, advance 61 min. Expect 2 audit rows, and `LastSignInAt` updated with a new ETag.
- `TwentyParallelFirstRequests_CreateOneUserRowAndOneSignInAudit` (Security, D4): `Task.WhenAll` of 20 `/api/me` calls; all return 200; exactly 1 user row and exactly 1 audit row.
- `SignInAudit_XForwardedForHeader_IsNotRecorded` (Security, U4): `X-Forwarded-For: 203.0.113.9`; the audit `IpAddress` is not `203.0.113.9`.
- `GetMe_CacheDisabled_MakesExactlyOneUsersReadPerRequest` (A14): for an existing user, 5 requests make exactly 5 recorded requests, all point reads on `/Users`.
- `ApiContract_GetMe_DeserializesIntoUiSharedProfile`: `ReadFromJsonAsync<CurrentUserProfile>` matches `CurrentUserDto` field for field (D23).

`ApiTests/AdminUsersTests.cs`:

- `AdminUsers_FirstPage_ReturnsUsersAndContinuationToken`: seed 120 users; with `pageSize=50`, expect 50 items and a non-null token.
- `AdminUsers_FollowingTokens_ReturnsEveryUserExactlyOnce`: walk every page; every seeded user appears once; the last token is null.
- `AdminUsers_DefaultPageSize_IsFifty`
- `AdminUsers_ListPage_QueriesWithPartitionKeyOnly` (Security, D2): exactly one entity query whose filter contains `PartitionKey eq 'user'`, and `QueriesWithoutPartitionKey` is empty.
- `AdminUsers_InvalidPageSize_Returns400` (Security; Theory: `0`, `-1`, `101`, `2147483648`, `abc`)
- `AdminUsers_InvalidContinuationToken_Returns400WithoutEchoingInput` (Security; Theory as in the unit test): the body does not contain the input.
- `AdminUsers_Response_ContainsOnlyDirectoryAndStatusFields` (Security, A12): each item's JSON property set is exactly `userId`, `displayName`, `email`, `status`, `lastSignInAt`.
- `AdminUsers_LockedUserInList_ShowsLockedStatus` (VIS-3)
- `AdminUsers_Response_SendsNoStore`

`ApiTests/UserStoreTests.cs` (`TableUserStore` and `TableAuditLog` resolved from `Factory.Services`):

- `GetAsync_MissingUser_ReturnsNullWithOnePointRead`
- `AddAsync_ExistingUser_ReturnsAlreadyExistsAndKeepsRow` (C2)
- `ReplaceAsync_CurrentETag_Writes`
- `ReplaceAsync_StaleETag_ReturnsConflictAndChangesNothing` (C1)
- `ReplaceAsync_MissingRow_ReturnsNotFound`
- `ListAsync_CursorAtLastUser_ReturnsEmptyPageAndNullCursor`
- `AppendAsync_DuplicateKeys_FailsInsteadOfOverwriting` (U3): appending the same entry twice throws `RequestFailedException` 409, and the stored row is unchanged.

`ApiTests/ProductionHostingTests.cs` (ProductionTestGroup, additions):

- `Startup_InvalidEntraSettings_FailsWithoutEchoingValue` (Theory: TenantId `common`, `contoso.onmicrosoft.com`; ClientId `abc`; Instance `http://login.microsoftonline.com/`): `Record.Exception(() => derived.CreateClient())` is an `InvalidOperationException` without the value, and the value is not in `Logs`.
- `Startup_MissingEntraSettings_Fails`
- `ApiRequest_ProductionGuestOrNoRoleToken_Returns403` (Theory)
- `ApiRequest_ProductionOtherTenantToken_Returns401`
- `ApiRequest_ProductionValidTokenRejectedByTenantCheck_LogsReasonOnly`: the Warning contains `WrongTenant` and no claim values.

### 7.6 End-to-end in process: Web to API (`WebTests/WebApiTests.cs`, WebApiTestGroup)

The constructor calls `webFactory.UseApi(apiFactory)`. Disposal resets API storage.

- `AccountPage_SignedInUser_RendersProfileFromApi`: 200, with `id="account-display-name"` holding the name and `id="account-user-id"` holding the oid. Exactly one user row exists in the API storage afterwards.
- `AccountPage_SignedInAdmin_RendersAdminBadge`
- `AccountPage_SignedInUser_HasNoAdminBadge`
- `AccountPage_LockedUser_RedirectsToAccessDenied` (W11): seed a locked row in the API storage; expect 302 to `/account/access-denied`.
- `AccountPage_NoRoleUser_RedirectsToAccessDenied`
- `AccountPage_Rendered_ContainsNoAccessToken` (Security, W7): the HTML contains no `eyJ` and no segment of the token the fake acquisition issued for that user.
- `AccountPage_ApiCall_TokenNotLoggedByWebOrApi` (Security)

### 7.7 Web tests (`WebTests/WebAuthenticationTests.cs`, WebTestGroup, Security unless noted)

- `AccountPage_Anonymous_RedirectsToTenantAuthorizeEndpoint` (W1): 302; the `Location` host is `login.microsoftonline.com`, the path starts with `/{TestTenant.Id}/oauth2/v2.0/authorize`, and it never contains `/common/` or `/organizations/`.
- `UnknownPage_Anonymous_RedirectsToSignIn`
- `UnknownPage_SignedIn_Returns404WithNotFoundPage`
- `SignIn_Challenge_UsesCodeFlowWithPkceStateAndNonce` (W2): the query has `response_type=code`, `code_challenge_method=S256`, a non-empty `code_challenge`, `state` and `nonce`, `client_id` = `TestWebApp.ClientId`, `redirect_uri=https://localhost/signin-oidc`, and the scope includes `TestApi.ScopeUri`.
- `SignIn_Challenge_SetsSecureHttpOnlyNonceAndCorrelationCookies`: `secure`, `httponly`, `samesite=none` and path `/signin-oidc`.
- `SignIn_ExternalReturnUrl_StoresHomeAsRedirect` (W3): a theory over the unsafe values from `ReturnUrlsTests`. Unprotect the `state` query value with `IOptionsMonitor<OpenIdConnectOptions>.Get("OpenIdConnect").StateDataFormat.Unprotect(state)`; `RedirectUri` must equal `/`.
- `SignIn_LocalReturnUrl_StoresItAsRedirect`
- `SignInCallback_ForgedState_RedirectsToErrorPage` (W8): `POST /signin-oidc` with `code=x&state=garbage` returns 302 to `/account/sign-in-error`.
- `SignInCallback_ErrorWithDescription_RedirectsToConstantErrorPage`: `error=access_denied&error_description=<script>alert(1)</script>` returns 302 to `/account/sign-in-error` with no query.
- `SignInErrorPage_QueryText_IsNotRendered`: `GET /account/sign-in-error?error_description=<b>pwned</b>` returns 200, and the HTML does not contain `pwned`.
- `SessionCookie_Issued_IsHostPrefixedSecureHttpOnlyLax` (W6): the `/test/sign-in` response `Set-Cookie` starts with `__Host-SharpVault=` and has `path=/`, `secure`, `httponly`, `samesite=lax` and no `domain`.
- `CookieOptions_Configured_EightHourAbsoluteLifetime`: `ExpireTimeSpan` is 8 h, `SlidingExpiration` is false, and `AccessDeniedPath` is `/account/access-denied`.
- `OpenIdConnectOptions_Configured_UseCodeFlowPkceAndNoSavedTokens` (W2, W7): `ResponseType` `code`, `UsePkce` true, `SaveTokens` false, `MapInboundClaims` false, `NameClaimType` `name`, `RoleClaimType` `roles`.
- `OpenIdConnectOptions_Configured_AcceptOnlyTenantIssuer` (W9): `ValidIssuers` is exactly `[Issuer(TestTenant.Id)]`.
- `TokenCache_Configured_IsEncrypted`: `MsalDistributedTokenCacheAdapterOptions.Encrypt` is true.
- `SignOut_Valid_RedirectsToEndSessionWithFixedPostLogoutTarget` (W4):
  1. Sign in.
  2. `GET /` and parse `__RequestVerificationToken` from `#sign-out-form`.
  3. `POST /account/sign-out` with the token.
  4. Expect 302 to the static `EndSessionEndpoint`, with `post_logout_redirect_uri=https://localhost/signout-callback-oidc`.
  5. The session cookie is expired in `Set-Cookie`.
- `SignOut_WithoutAntiforgeryToken_Returns400AndKeepsSession` (W5): after the 400, `GET /account` still is not challenged.
- `SignOut_GetRequest_DoesNotSignOut`: `GET /account/sign-out` signed in is not 302 to the end session endpoint, and the session stays.
- `SignOut_Anonymous_IsChallenged`
- `AnonymousPages_Get_Return200` (W13; Theory: `/`, `/not-found`, `/account/access-denied`, `/account/signed-out`, `/account/sign-in-error`, `/health`, `/alive`)
- `StaticAssets_Anonymous_Return200` (Theory: `/_framework/blazor.web.js` and the UI.Shared `app.css` path from the rendered home page)
- `HomePage_Anonymous_ShowsSignInLinkAndNoAccountMenu` (not Security): `id="home-sign-in"` and `id="sign-in-link"` are present; `id="sign-out-form"` is absent.
- `HomePage_SignedIn_ShowsGreetingAndSignOutForm` (not Security)
- `HomePage_SignedIn_ContainsNoInlineScriptOrStyle`: the foundation regexes also hold for the signed-in markup.
- `BlazorHub_AnonymousNegotiate_IsChallenged` (W14): `POST /_blazor/negotiate?negotiateVersion=1` anonymous is not 200.
- `WebRequest_TestHeaders_DoNotAuthenticate` (W15): `GET /account` with `SpoofedTestHeaders` is still 302 to the authorize endpoint.
- `Startup_WebProductionWithClientSecret_FailsWithoutEchoingSecret` (W10): a derived factory in Production with `AzureAd:ClientCredentials:0:SourceType=ClientSecret` and `AzureAd:ClientCredentials:0:ClientSecret=<random>`. Startup throws `InvalidOperationException` without the secret, and the secret is not in `Logs`.
- `Startup_WebProductionWithoutCredential_Fails`
- `Startup_WebCommonTenant_Fails`

### 7.8 Performance tests (`ApiTests/AuthPerformanceTests.cs`, PerformanceTestGroup, Performance)

Each test sends 5 warm-up requests on its measuring client, clears the recorder, and then measures each request with `Stopwatch.GetTimestamp()`. Budgets are `private static readonly TimeSpan` fields. Failure messages include every timing.

- `GetMe_CachedAccess_FiftyRequestsEachUnder50MillisecondsWithoutStorageCalls`: a derived factory with `UserAccess:CacheTtl=00:00:30`. 0 storage calls in total.
- `GetMe_CacheDisabled_FiftyRequestsEachUnder200MillisecondsWithOnePointReadEach`: exactly 50 recorded requests, all point reads.
- `FirstRequest_NewUser_CompletesUnder500MillisecondsWithThreeStorageCalls`: warm up with a different user. The 3 calls are a point read (404), the user insert and the audit insert, in that order.
- `SignInDue_ExistingUser_CompletesUnder500MillisecondsWithThreeStorageCalls`: `FakeTimeProvider`. The 3 calls are a point read, the replace and the audit insert.
- `AdminUsers_PageOfFiftyFromFiveThousand_CompletesUnder500MillisecondsWithOnePartitionQuery`: seed 5,000 rows (50 transactions); cache on (30 s) and warm, so the gate makes 0 calls; exactly 1 entity query with the partition key.
- `DeniedRequests_EachUnder50MillisecondsWithoutStorageCalls` (Theory: anonymous, alg none, other tenant, no role, guest; 50 requests each)
- `LockedUser_CachedDenial_FiftyRequestsEachUnder50MillisecondsWithoutStorageCalls`: cache 30 s.
- The renamed `Startup_DefaultTableSet_CompletesUnder5SecondsWithOneCallPerTable` (T11).

### 7.9 Commands the reviewer runs

```bash
docker info
dotnet build SharpVault.slnx
dotnet test SharpVault.slnx --filter "Category!=Performance"
dotnet test SharpVault.slnx --filter "Category=Security"
dotnet test SharpVault.slnx --filter "Category=Performance"
dotnet test SharpVault.slnx -c Release --filter "Category!=Performance" --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults
dotnet tool restore
dotnet reportgenerator "-reports:TestResults/**/coverage.cobertura.xml" "-targetdir:TestResults/coverage" "-reporttypes:Cobertura"
dotnet run eng/CoverageGate.cs -- TestResults/coverage/Cobertura.xml SharpVault.Application=80 SharpVault.Infrastructure=80
dotnet list SharpVault.slnx package --vulnerable --include-transitive
git grep -n "TestAuthenticationHandler" -- src tests
```

The last command must find nothing.

## 8. Performance budgets

Measured against Azurite in `PerformanceTestGroup` after 5 warm-up requests. Storage calls are counted with `StorageRequestRecorder`.

| Operation | Budget | Storage calls |
|---|---|---|
| Any request denied before the gate (anonymous, forged, wrong tenant or audience, app-only, no role, guest) | under 50 ms | **0** |
| Authorized request, cache hit, Active, sign-in not due | under 50 ms | **0** |
| Authorized request, cache hit, Locked (whether or not a sign-in is due) | under 50 ms | **0**: denied from the cache, no read and no sign-in write (D33) |
| Authorized request, cache miss or disabled, sign-in not due | under 200 ms | **1** point read on `Users` |
| First request of a new user | under 500 ms | **3**: point read (404), `Users` insert, `AuditLog` insert |
| Sign-in due (once per `SignInRecordInterval`, default 1 h) | under 500 ms | **3**: point read, `Users` replace with ETag, `AuditLog` insert |
| Lost race (insert 409 or replace 412) | n/a | **+1** re-read, no audit insert |
| `GET /api/me` | as above (gate only) | gate only; the handler makes 0 calls |
| `GET /api/admin/users`, page of 50 from 5,000 users | under 500 ms | gate plus **1** query with `PartitionKey eq 'user'` (and `RowKey gt` cursor) |
| Health and documentation endpoints with any token | unchanged from the foundation | 0 to `Users` |
| API startup | under 5 s | **2** (one `CreateTableIfNotExists` per table) |

- **Lock check read rate**: at most one point read per active user per instance per `CacheTtl`. The default is 5 s, so 1,000 active users per instance give at most about 200 reads per second. That is well under the single-partition target of about 2,000 entities per second (D8).
- **Queries without a partition key**: none are allowed. The gate uses point reads only, and the list uses the `user` partition.
- **Hot paths and benchmarks**: JWT validation, `SignedInIdentityReader` and the gate's cache-hit path run on every request. There is no Benchmarks project on this branch (D27); the performance tests above assert the budgets. After encryption-core merges, add `AccessGateBenchmarks` and `SignedInIdentityReaderBenchmarks` (PROGRESS follow-up).

## 9. Risks and decisions

| # | Topic | Decision and reason |
|---|---|---|
| D1 | Library | **Microsoft.Identity.Web 4.16.1** for both hosts, as the entra-auth skill says. It brings Entra-aware audience and issuer handling, OIDC and MSAL token acquisition, and the token cache. Restoring it pulls vulnerable `System.Security.Cryptography.Pkcs` and `.Xml` 10.0.10, so both are pinned to 10.0.14 through central transitive pinning (verified clean). `Microsoft.AspNetCore.Authentication.OpenIdConnect` is pinned to 10.0.14 so it matches JwtBearer. |
| D2 | Token validation parameters | Explicit `ValidIssuers` = the tenant's v2 issuer only. It is required to avoid a network metadata fetch (V2), and it rejects v1 tokens, so the API registration must request v2 tokens (ENTRA-SETUP). `ValidAlgorithms` = RS256 blocks key-confusion and `none`. `ClockSkew` is 2 minutes, because the default 5 accepted a token expired 3 minutes ago (V5). Entra hosts are NTP-synced, so 2 minutes is safe. `IncludeErrorDetails=false` keeps validation details out of `WWW-Authenticate`. `MapInboundClaims=false`, so claim types are exactly Entra's. |
| D3 | Where the tenant and scope checks run | In `OnTokenValidated`, returning **401**. The issuer validator ignores `tid` (V3), and TEN-2 demands a `tid` check. Also there: exactly one canonical `oid`, `scp` containing `access_as_user`, and rejection of `idtyp=app`. App-only tokens could otherwise carry an app role if an admin assigned one to an application. A token that fails these checks is not a usable user token, so the status is 401, matching the skill's "token from another tenant: 401". |
| D4 | Fallback policy (resolves foundation D4) | **The fallback and the default policy are the User policy** (authenticated, role, guest and lock). Deny by default now also means "role required and lock checked" for any forgotten endpoint and for unknown routes (LCK-1 says every request). Consequence, accepted: an authenticated caller without a role gets 403 instead of 404 on unknown routes, and a caller with a role costs one lock check before a 404. Foundation tests are updated (T11). |
| D5 | Guest detection (TEN-3) | **`acct` is required** as an optional access token claim (0 member, 1 guest). It is the documented Entra signal and covers guests whose `tid` is our tenant (all B2B guests). Defense in depth: an `idp` claim that is present and is not our tenant's issuer also means guest. **A missing or unexpected `acct` is treated as not a member** (fail closed). A deployment that forgets the optional claim sees everyone get 403 at once, which is loud and documented in ENTRA-SETUP. The ADM-1 hook is `IGuestAccessPolicy`, backed by `Tenancy:AllowGuests` (default false) until `feature/admin-settings` stores the setting. Allowed guests still need a role and are lock checked. |
| D6 | Status codes and response shape | 401 for every token problem; 403 for guest, missing role and locked. Every 403 is the same ProblemDetails shape (only `traceId` differs). The reason is logged server-side as an enum with the user id, never returned. The Web shows one generic access denied page. |
| D7 | Lock check placement | An **authorization requirement** (`SharpVaultAccessHandler` calling `IUserAccessGate`), not the middleware after `UseAuthorization` that the foundation sketched. It runs only for endpoints that need a user (health and documentation skip it), runs before the endpoint, produces the standard 403, and lets cheap claim checks short-circuit before any storage call. It is memoized per request, so combined policies cannot double the storage calls. |
| D8 | Lock cache and staleness budget (LCK-1) | **In-memory per instance, TTL 5 s by default (0 to 30 s; 0 disables), at most 10,000 entries**, keyed by the validated `oid`, with both Active and Locked states cached. Validity is checked against `TimeProvider`, so it is testable. Why not "no cache": every authorized request would point-read the single `user` partition, whose target is about 2,000 entities per second. A burst would throttle (503) and fail closed for **everyone**. A page load issues several API calls, and 5 s collapses them to one read. Staleness budget: a lock takes effect on other instances within the TTL plus one request. On the instance that writes the lock it is immediate, through `IUserAccessCache.Invalidate`, which user-lock must call. Strict deployments can set 0. Tests default to 0 so call counts are deterministic and no state leaks between tests; cache tests use derived factories. |
| D9 | Users table design and encryption | PK `user`, RK `oid` (canonical lowercase "D"), as in the table-storage skill. Columns: `DisplayName`, `Email`, `Status`, `CreatedAt`, `LastSignInAt`. **Not encrypted**: display name and email are tenant directory data that every member can already read through Entra and Graph `User.ReadBasic.All`, not vault data (SEC-2 covers vault contents). Encrypting them would add a Key Vault unwrap to every lock check and admin list, and would create a dependency on `feature/encryption-core`. Storage-level encryption at rest and managed identity access apply. No roles, tokens, IP addresses or group memberships are stored in the row. Lock details (reason, who, when) come with user-lock. |
| D10 | What "sign-in" means and the write order (TEN-5, AUD-2) | The API has no sign-in event (bearer tokens are refreshed silently), and the Web cannot write storage (layering). **A sign-in is the first authorized API request of a user after `SignInRecordInterval`** (default 1 h) since the stored `LastSignInAt`. This is server-enforced and works the same for the Web, MAUI and any client. Order: **write the user row first** (insert, or replace with the ETag), then **only the winner** appends the `SignIn` audit entry. That gives exactly one entry per interval under concurrency, never overwrites a concurrent lock, and records nothing for a locked user. Residual risk, accepted: a storage failure after the row write and before the audit insert loses that one entry. It is logged at Error and the request fails. The alternative (audit first) duplicates entries in every race. |
| D11 | Audit store now or later | **Introduce the minimal append-only store now**: the `AuditLog` table (PK `yyyyMM` UTC, RK inverted ticks `_` id, as in the skill), `IAuditLog.AppendAsync` only, insert only, and a source scan test. Sign-in is the first audited event, and every feature before `feature/audit-log` (reveals, shares, links, locks) needs to write entries. `feature/audit-log` adds reading, search, export and AUD-5. Event types are stored as strings. |
| D12 | Test authentication | **Locally signed JWTs through the real JwtBearer and Microsoft.Identity.Web pipeline**, using a per-process RSA key and `StaticConfigurationManager` metadata (verified, no network). **The header-based `TestAuthenticationHandler` is removed**: it bypasses `OnTokenValidated`, where TEN-2 and the scope check live, so tests passing with it would prove nothing about production. Web tests sign in through a test-only startup filter that calls the **real** cookie scheme's `SignInAsync`, skipping only the Entra round trip. Full OIDC code redemption needs a live tenant, so it is a documented manual smoke test, not automated. |
| D13 | Web architecture | Backend for frontend. OIDC code flow with PKCE, tokens kept server-side in the MSAL distributed token cache (in-memory distributed cache, `Encrypt=true`), and `SaveTokens=false`, so tokens never reach the browser. The **typed `HttpClient` gets the token itself** through `ITokenAcquisition` with the principal from `AuthenticationStateProvider`, because a `DelegatingHandler` resolves scoped services from the HttpClientFactory handler scope, not the request or circuit scope. Static SSR is the default render mode: the server `AuthenticationStateProvider` comes from the cookie, and redirects work during SSR. Interactive Server circuits capture the auth state at circuit start (`AddCascadingAuthenticationState`) and use the same client. When a token cannot be acquired silently (`MicrosoftIdentityWebChallengeUserException`), the client returns `SignInRequired` and the page navigates to `/account/sign-in` with `forceLoad`. No WebAssembly. `RejectSessionCookieWhenAccountNotInCacheEvents` is not a library type (V9); the empty-cache case after a restart is handled by the same `SignInRequired` path. |
| D14 | Roles in the Web | The Web's ID token does not carry the **API's** app roles: they are defined on the API registration, as the entra-auth skill says. Rather than duplicate role definitions on the Web registration, or call the API inside OIDC events (fragile and impossible to test without Entra), **the UI learns `IsAdmin` from `GET /api/me`**. `AuthorizeView` is used for signed-in versus anonymous. This deviates from the blazor-ui skill's "AuthorizeView with the roles" wording, deliberately: the API is the boundary, and the UI shows what the API says. The UI.Shared sign-out form is web-specific. `feature/maui-app` replaces it with an MSAL sign-out behind a platform service. |
| D15 | Web deny by default | Fallback policy `RequireAuthenticatedUser` (roles are the API's job). Explicitly anonymous: `/`, `/not-found`, `/error`, the three account status pages, `/account/sign-in`, static assets and health. Anonymous unknown routes now redirect to sign-in instead of returning 404 (no route enumeration). `/_blazor` requires authentication, so there are no anonymous circuits. |
| D16 | Sign-out CSRF | `POST` only, validated **explicitly** with `IAntiforgery.IsRequestValidAsync`. Endpoint metadata alone did not validate a form-less endpoint (V10). SameSite=Lax on the session cookie is a second layer. Sign-in stays a GET link: login CSRF is covered by OIDC state, nonce and the correlation cookie. |
| D17 | CSP `form-action` (resolves foundation D21) | `form-action 'self' https://login.microsoftonline.com` (the configured instance origin). The sign-out POST redirects to Entra's logout endpoint, and Chromium applies `form-action` to that redirect. `ForWeb` now takes the origin. |
| D18 | Web credentials | No client secrets outside Development: `WebCredentialGuard` fails startup with a constant message. Azure uses a managed identity federated credential (`SignedAssertionFromManagedIdentity`), configured by the deployment feature. Locally, a client secret in the Web's user-secrets. `RepositoryConfigurationTests` already forbids `ClientSecret` in the repository's JSON. |
| D19 | Local development without Entra | **No fake-auth mode in production code.** A development bypass would be an `AuthenticationHandler` in `src` (forbidden by the architecture test) and a standing escalation risk. Contributors need app registrations in a dev tenant (ENTRA-SETUP). Ids go into Api and Web **user-secrets** (new `UserSecretsId`s), not AppHost parameters. That keeps the foundation AppHost tests, which forbid parameters, and avoids colliding with encryption-core's AppHost parameter change. |
| D20 | `Result<T>` and the validation pipeline | Introduced now, because this is the first feature with a validated request (foundation D13). `ValidationBehavior` throws `ValidationException` (skill), and the Api `ValidationExceptionHandler` maps it to a 400 ProblemDetails with constant messages. FluentValidation's default messages echo the attempted value, so every rule sets `.WithMessage`. |
| D21 | Mapping table entities | Mapster `IRegister` classes in Infrastructure, as the table-storage skill says, scanned in `AddInfrastructure`. Table entities are `internal`, and `InternalsVisibleTo` makes them visible to unit tests. Unknown status strings map to `Locked` (fail closed). |
| D22 | Shared identity helpers | `EntraApplicationSettings`, `EntraTokenChecks`, `AccountClassifier`, `EntraClaimTypes` and `AppRoles` live in ServiceDefaults, which both hosts already reference and which keeps no SharpVault project references. A new project was not justified. |
| D23 | API contract in UI.Shared | UI.Shared cannot reference Application (layering), so `CurrentUserProfile` mirrors `CurrentUserDto`, and `ApiContract_GetMe_DeserializesIntoUiSharedProfile` catches drift. A shared Dtos project can be introduced later if the contract grows. |
| D24 | Token cache and Data Protection | Single instance for now: in-memory distributed cache and the default Data Protection key ring. After a restart, users sign in again (`SignInRequired` path). The deployment feature must add a shared distributed cache and persisted, Key Vault-protected Data Protection keys before scaling out. This is a PROGRESS follow-up. |
| D25 | Client IP in audit | Connection remote address only. `X-Forwarded-For` is ignored until the deployment feature configures `ForwardedHeaders` with known proxies, because trusting the header now would let any caller forge audit IPs. |
| D26 | Merge with encryption-core | The branches touch the same files **additively**: `Directory.Packages.props`, `Infrastructure/DependencyInjection.cs`, `ApiTestFactory`, `ProductionApiTestFactory`, `LayeringTests`, `RepositoryConfigurationTests`, `PROGRESS.md`, `TESTING.md`, `STORAGE.md` and `README.md`. Whichever merges second rebases and keeps both sides' lines. The two sets of Application files do not overlap (`Common/Interfaces/IDataKeySession.cs` versus the files here). This branch does not touch the AppHost. |
| D27 | Benchmarks | Not on this branch: `tests/SharpVault.Benchmarks` exists only on encryption-core. The performance tests assert the per-request budgets, and the benchmarks are a follow-up. |
| D28 | Production factory scope | `ProductionApiTestFactory` points at a fake https storage account and must never reach storage (foundation D27). Its tests cover startup guards and every outcome decided before the gate (401, and 403 for guest and no role). The full matrix including storage runs in Development against Azurite. The authentication and authorization code has no environment branches, so the outcomes are the same. |
| D29 | Clock in tests | JwtBearer lifetime validation uses the system clock, not `TimeProvider`. Token lifetime tests use margins of 5 minutes or more beyond the 2-minute skew. The gate and cache use `TimeProvider` and are driven with `FakeTimeProvider` started at the real UTC now. |
| D30 | Locked users and audit | A locked user's request writes nothing (no `LastSignInAt` and no `SignIn` entry). The denial is logged at Warning with the user id. Auditing denied attempts is a sensible addition for `feature/user-lock` (PROGRESS follow-up). AUD-2 does not list it. |
| D31 | LCK-3 "ends sessions" | Out of scope. With this feature, a locked user's Web cookie stays valid, but every page that needs data gets 403 from the API and shows access denied. `feature/user-lock` should add session revocation (Graph `revokeSignInSessions` and a Web-side check). This is a PROGRESS follow-up. |
| D32 | Email semantics | `Email` is the `email` claim, otherwise `preferred_username` when it contains `@`. It is for display and search only. It can be unverified or changed by a tenant admin, so it never identifies a user, never authorizes, and `ICurrentUser` does not expose it (A11). |
| D33 | Cached locked users and the sign-in interval | **In `UserAccessGate`, a cached entry that is not `Active` is denied directly from the cache, with no storage read and no sign-in write, regardless of whether a sign-in is due. Only cached `Active` entries go through the `IsSignInDue` path.** Reason: a locked user's `LastSignInAt` is never updated (D30), so if the sign-in check ran first, a locked user would cause one storage read per request after the sign-in interval (read amplification on the single `user` partition, A15). The cache TTL (default 5 s, at most 30 s) still bounds unlock latency. |

## 10. Definition of done for this branch

- Tasks T1 to T16 are complete. `dotnet build SharpVault.slnx` reports 0 warnings and 0 errors.
- Every test in section 7 exists and passes, including the Security and Performance categories. Every threat in section 6 has a passing test.
- The full authorization matrix (7.1) passes for every endpoint and caller.
- The coverage gate passes (Application and Infrastructure at 80% or more).
- `dotnet list SharpVault.slnx package --vulnerable --include-transitive` reports no vulnerable packages.
- `git grep -n "TestAuthenticationHandler" -- src tests` finds nothing.
- No query without a partition key, and no update without an ETag.
- No inline comments, commented-out code or TODOs. The AGPL header is on every new `.cs` file.
- No tenant ids, client ids, scopes or secrets in any `appsettings*.json` or `launchSettings.json`.
- The reviewer has approved.
- The pull request title is `feat: Entra ID sign-in, app roles, users table and lock check`. Its body lists TEN-1 to TEN-5, LCK-1 and AUD-2, and mentions the stacked base `feature/solution-foundation`.
