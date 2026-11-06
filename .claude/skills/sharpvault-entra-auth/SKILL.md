---
name: sharpvault-entra-auth
description: SharpVault authentication and authorization. Microsoft Entra ID in Entra mode, the Development-only local mode with four accounts, app roles, sessions and revocation, the current user and managed identity access to Azure. Use when working on sign-in, tokens, app roles, authorization policies, sessions, the current user, the mode selection or managed identity. Use this instead of dotnet-auth in this repository; SharpVault does not use ASP.NET Identity.
---

# Authentication in SharpVault

SharpVault has two modes. The mode is chosen when a host starts, and there is no feature flag.

- **Entra mode.** Users sign in with Microsoft Entra ID. SharpVault stores no passwords and issues no tokens. This is the only mode outside Development.
- **Local mode (Development only).** A picker signs in one of four local accounts: Admin One, Admin Two, User One and User Two. The Web keeps a cookie session for 8 hours. The API validates HS256 access tokens that last 5 minutes. The Web and the API share one signing key file. Local mode exists to run without a tenant. It is not a production path.

## Mode selection

`SharpVault.Authentication` (`Mode/AuthenticationModeResolver`) reads `AzureAd:TenantId` and `AzureAd:ClientId` when the host starts:

| Settings | Environment | Result |
|---|---|---|
| Both set | any | Entra mode |
| Neither set | Development | Local mode |
| Neither set | any other | The host stops with a configuration error |
| Only one set | any | The host stops with a configuration error |

The Web and the API both derive the mode this way. AppHost forwards the `AzureAd` settings to both hosts in run mode, and forwards nothing in publish mode. The integration factories set the settings with `UseSetting` (see `sharpvault-integration-testing`).

## App registrations (Entra mode)

- **SharpVault API:** exposes the scope `access_as_user` and defines the app roles `SharpVault.Admin` and `SharpVault.User`.
- **SharpVault Web:** signs users in with OpenID Connect and calls the API with the `access_as_user` scope.
- **SharpVault MAUI:** planned for `feature/maui-app`. It will use MSAL.NET with the system browser and the same scope. Not built.
- Roles are assigned to users or groups under Enterprise applications. The steps are in `docs/ENTRA-SETUP.md`.

## API

- `AddAuthentication(JwtBearer).AddMicrosoftIdentityWebApi(...)` validates v2 access tokens. The issuer must match exactly, the algorithm is RS256 only, and the `tid` must match the configured tenant (`AccessTokenValidation`).
- Two policies are defined in `SharpVault.Api/Security/Policies.cs`. `Policies.User` requires the `SharpVault.User` or `SharpVault.Admin` role. `Policies.Admin` requires `SharpVault.Admin`. `Policies.User` is also the default and fallback policy, so an endpoint with no explicit policy needs at least the User role.
- Both policies run `SharpVaultAccessHandler`, which checks the caller's row in the `Users` table. A locked user is refused on the next authorized request. The lock check has a short cache (`UserAccess:CacheTtl`).
- Endpoints so far: `GET /api/me` requires `Policies.User`, and `GET /api/admin/users` requires `Policies.Admin`.
- Access to an individual secret is decided in its handler, not in a policy. The vault features are not built yet.
- Local mode uses the same policies and the same access checks, behind a JwtBearer scheme that validates HS256 tokens.

## Current user

- `ICurrentUser` (`SharpVault.Application/Common/Interfaces`) exposes `UserId` (a Guid from the Entra `oid` claim), `DisplayName` and `IsAdmin`. The API implements it as `HttpContextCurrentUser`.
- Users are identified by `oid`, never by email or UPN, because those can change.
- The `Users` table gets a row on first use and is updated on sign-in, with ETag concurrency.
- The four local accounts are inserted when the API starts. An existing row is never changed, so a lock survives a restart.

## Web

- OpenID Connect with the code flow and PKCE. `SaveTokens` is off. The id token is checked by `WebTokenValidation`.
- The API token is acquired inside the Web circuit with `ITokenAcquisition` (`EntraApiAccessTokenProvider`). `MicrosoftIdentityWebChallengeUserException` and `MsalUiRequiredException` both mean sign-in required. Other exceptions are not swallowed.
- The MSAL token cache is encrypted. It sits in the distributed cache, which is `AddDistributedMemoryCache` today, so it is per process (see Sessions below).
- The session cookie is `__Host-SharpVault`: Secure, HttpOnly, SameSite Lax, path `/`, lifetime 8 hours, no sliding expiration.
- `ApiClientBase` attaches the access token itself, not through a handler. That way the token is read from the circuit's own authentication state.

## Sessions and revocation

- At sign-in, `SessionClaimsIssuer` adds a session id claim and an expiry claim (8 hours). Any session claims the caller sent are replaced when the claims are issued.
- Sign-out in either mode records the session id in `ISessionRevocationStore` (`DistributedSessionRevocationStore`). The record lasts for the session lifetime.
- `SessionValidator.IsActiveAsync` returns false when the principal has no session id, when the session is past its expiry, or when the session is revoked. If the store throws, the exception propagates and the call does not return active.
- `RevalidatingSessionAuthenticationStateProvider` re-checks each circuit every minute, so an open circuit ends within about one minute of a sign-out elsewhere. `ApiClientBase` checks before every API call.
- The revocation store is an `IDistributedCache` backed by `AddDistributedMemoryCache`, so it is per process. Two instances do not share revocations until a real distributed cache is configured. This is an open item.
- Tests: `Circuit_AfterSignOutElsewhere_ApiCallReturnsSignInRequired`, `Circuit_PastSessionExpiry_ApiCallReturnsSignInRequired`, `SignOut_Valid_RevokesTheSessionSoOtherCircuitsEnd`.

## Local mode sign-in

- The picker is a static page with one POST form per account, and each form has an antiforgery token. The picker returns 404 unless the request comes from loopback with no public host header and no forwarded headers (`Picker_NonLoopbackRemote_Returns404`, `Picker_ForwardedHeader_Returns404`).
- A local cookie is anonymous in Entra mode, because the authentication scheme check rejects it. No written rule states this. `EntraModeWeb_CookieMintedInLocalMode_IsAnonymous` checks the outcome only (open item).

## Known gaps (not verified)

- Real Entra sign-in and token acquisition from a circuit are not verified, because there is no tenant. The integration tests replace the OpenID Connect metadata with a test identity provider, so the Entra sign-in redirect and the Entra session claims are not exercised.
- Whether MSAL raises `MsalUiRequiredException` from a circuit with no `HttpContext` is unverified (see `docs/PROGRESS.md`).

## Azure resources

- The API and the Web reach Table Storage and Key Vault with `DefaultAzureCredential`. That is managed identity in Azure and developer credentials locally.
- Required roles: "Storage Table Data Contributor" on the storage account, and "Key Vault Crypto User" on the Key Vault key.
- Production accepts only an https service URI for Table Storage. Connection strings, account keys and SAS are accepted only in Development, enforced at startup (`TableStorageSettingsGuardTests`). No client secrets in configuration.

## Tests

- Integration tests run the real JwtBearer pipeline with a test signing key and a static OpenID configuration (`TestIdentityProvider`, `TestSigningKeys`, `TestTokens`, `TestUsers`). Nothing calls Entra.
- Local mode tests use `LocalApiTestFactory` and `LocalWebTestFactory`, with the same callers.
- See `sharpvault-integration-testing` for the factories and the mode assertion.
