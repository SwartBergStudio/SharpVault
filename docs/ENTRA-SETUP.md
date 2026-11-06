# Microsoft Entra ID setup

SharpVault signs users in with Microsoft Entra ID. It supports one tenant per deployment (TEN-1). Only members of that tenant can sign in, and guests are rejected unless `Tenancy:AllowGuests` is set (TEN-3).

Entra ID is optional for local development. With no Entra ID settings in Development, the web app shows a local account picker instead. See [LOCAL-DEV.md](LOCAL-DEV.md#local-mode-no-entra-id). This document covers the Entra ID mode, which starts when both `AzureAd:TenantId` and `AzureAd:ClientId` are set.

This repository ships placeholders only. No tenant id, client id, client secret or scope is committed, and `appsettings*.json` contains only the Entra authority (`https://login.microsoftonline.com/`) and the callback paths. Sign-in cannot be exercised until you register the two applications below in a real tenant and set the values from sections 5 and 6.

The test suite does not need Entra ID. Tests sign tokens with locally generated keys and validate them with the real JwtBearer and Microsoft.Identity.Web pipeline. No test calls Entra ID.

## 1. What you register

| Registration | Used by | Purpose |
|---|---|---|
| SharpVault API | `SharpVault.Api` | Exposes the `access_as_user` scope and the app roles. Validates access tokens. It needs no client secret, because it does not call other services. |
| SharpVault Web | `SharpVault.Web` | Signs users in with the authorization code flow and PKCE, and acquires an access token for the API. |

Create both in the tenant that will own the deployment. Choose "Accounts in this organizational directory only" (single tenant) for both.

## 2. Register the API

1. In the Entra admin center, go to **App registrations** and select **New registration**. Name it, for example `SharpVault API`, and choose single tenant. Leave the redirect URI empty.
2. Copy the **Application (client) ID** and the **Directory (tenant) ID** from the overview page. The client id is the API's `AzureAd:ClientId`. The tenant id is `AzureAd:TenantId`.
3. Go to **Expose an API**.
   1. Set the Application ID URI. The default `api://{client-id}` is what the Web requests.
   2. Select **Add a scope**. Set the scope name to `access_as_user`, who can consent to admins and users (or admins only), and keep the scope enabled.
4. Go to **App roles** and select **Create app role** twice:

   | Display name | Value | Allowed member types |
   |---|---|---|
   | SharpVault User | `SharpVault.User` | Users/Groups |
   | SharpVault Admin | `SharpVault.Admin` | Users/Groups |

   The value must match exactly. The API reads the `roles` claim and compares it with these values.
5. Go to **Manifest** and set the top-level property `"accessTokenAcceptedVersion": 2`. The API accepts only v2 access tokens from the v2 issuer of the configured tenant. A v1 token is rejected.
6. Go to **Token configuration** and select **Add optional claim**. Add `acct` for the **Access** token type, and add `acct` for the **ID** token type as well.

   The `acct` claim is `0` for members and `1` for guests. Without it, the API cannot tell a member from a guest, so it treats every caller as a guest and returns 403 unless `Tenancy:AllowGuests` is `true`.

## 3. Register the Web

1. **New registration**. Name it, for example `SharpVault Web`, and choose single tenant.
2. Set the platform to **Web** and add these redirect URIs. Use the https address of the web app:
   - `https://{web-host}/signin-oidc`
   - `https://{web-host}/signout-callback-oidc`

   The sign-out callback is the post-logout redirect. Entra accepts it only when it is registered here. For local runs, `{web-host}` is `localhost:7190` when you start the `SharpVault.Web` profile. Under the AppHost, Aspire chooses the web port, so register the exact https URL shown in the dashboard.
3. Keep the implicit grant options unticked. The Web uses the authorization code flow with PKCE.
4. Copy the **Application (client) ID**. It is the Web's `AzureAd:ClientId`.
5. Go to **API permissions**, select **Add a permission**, then **My APIs**, then **SharpVault API**, then **Delegated permissions**, and tick `access_as_user`. Grant admin consent if your tenant requires it.
6. Go to **Token configuration** and add the `acct` optional claim for the **ID** and **Access** token types, as in section 2.
7. Set the credentials for the Web. Keep secrets out of configuration files.
   - **Production (and any environment other than Development)**: use a managed identity federated credential or a certificate. The Web refuses to start outside Development if it has no allowed credential, and it refuses any client secret. Allowed `SourceType` values are `SignedAssertionFromManagedIdentity`, `KeyVault`, `StoreWithThumbprint` and `StoreWithDistinguishedName`.
   - **Development only**: create a client secret under **Certificates & secrets** and store it with the AppHost settings (section 6) or `dotnet user-secrets`.

## 4. Assign users to roles

1. Go to **Enterprise applications**, select **SharpVault API**, then **Users and groups**.
2. Select **Add user/group**, pick a user, and choose the role `SharpVault.User` or `SharpVault.Admin`.

Every API request needs one of the two roles (TEN-4). A signed-in user with no role gets 403 from the API, and the account page then sends the user to the access-denied page. The Web also checks the session once a minute, and it ends the session when the API answers 403. The user therefore has to sign in again, and gets the access-denied page again until a role is assigned. Admin rights never grant access to other users' data, even with `SharpVault.Admin`.

The Web does not authorize on roles. The account page calls `GET /api/me`, and the API answers with `IsAdmin` from the roles in the access token. The Administrator tag comes from that answer. The app roles are defined on the API registration, so the Web's sign-in token is not expected to carry them. This behaviour is not verified without a tenant.

The API rejects a locked user with 403 on every request, once the lock status cache has expired (`UserAccess:CacheTtl`, 5 seconds by default). The Web checks the session once a minute. When the API refuses the user, the Web ends that session, so an open page shows "Sign in required" within about a minute, and the revealed values on it leave the page. Admins lock and unlock users from the Users page. Scheduled locks are not built.

### Directory search for sharing

Sharing in Entra mode finds colleagues through Microsoft Graph (SHR-1). The API calls `users?$search` with a token for `https://graph.microsoft.com/.default`, so the API's identity needs the Microsoft Graph application permission `User.ReadBasic.All`, with admin consent. No user consent and no client secret is involved. Assign the permission to the identity the API runs as (its managed identity in deployment). This has not been verified against a real tenant. Local mode does not call Graph. See [SHARING.md](SHARING.md#finding-people).

## 5. Configuration keys

| Key | Set in | Value | Notes |
|---|---|---|---|
| `AzureAd:Instance` | `appsettings.json` (both hosts) | `https://login.microsoftonline.com/` | Absolute https URI with path `/`, no query or user info. |
| `AzureAd:TenantId` | AppHost settings, user secrets or environment (both hosts) | Directory (tenant) id, GUID | Set together with `AzureAd:ClientId` to use Entra ID. Multi-tenant values such as `common` are rejected. |
| `AzureAd:ClientId` | AppHost settings, user secrets or environment | API client id on `SharpVault.Api`, Web client id on `SharpVault.Web`, GUID | Set together with `AzureAd:TenantId`. |
| `AzureAd:CallbackPath` | `appsettings.json` (Web) | `/signin-oidc` | Must match the registered redirect URI. |
| `AzureAd:SignedOutCallbackPath` | `appsettings.json` (Web) | `/signout-callback-oidc` | Must match the registered redirect URI. |
| `AzureAd:ClientCredentials:0:SourceType` | user secrets (Development) or environment (other environments) | `ClientSecret` in Development. Elsewhere `SignedAssertionFromManagedIdentity`, `KeyVault`, `StoreWithThumbprint` or `StoreWithDistinguishedName` | Section 3 describes the choice. The AppHost sets it for you (section 6). |
| `AzureAd:ClientCredentials:0:ClientSecret` | AppHost secret, or user secrets | The Web client secret | Development only. Never in a file in the repository. |
| `SharpVaultApi:BaseAddress` | `appsettings.json` (Web) | `https+http://api` | Aspire service discovery. |
| `SharpVaultApi:Scopes:0` | AppHost (derived), user secrets or environment (Web) | `api://{api-client-id}/access_as_user` | At least one scope is required in Entra ID mode. |
| `Tenancy:AllowGuests` | user secrets or environment (API) | `false` by default | Guests stay rejected unless this is `true`. Admin settings will manage it later (ADM-1). |
| `UserAccess:CacheTtl` | optional (API) | `00:00:05` by default | Between 0 and 30 seconds. `0` disables the lock-status cache. |
| `UserAccess:CacheMaxEntries` | optional (API) | `10000` by default | Between 100 and 1,000,000. |
| `UserAccess:SignInRecordInterval` | optional (API) | `01:00:00` by default | Between 1 minute and 24 hours. A sign-in is recorded at most once per interval. |

Both hosts choose the mode when they start. With both `AzureAd:TenantId` and `AzureAd:ClientId` set, they use Entra ID and validate the values strictly. If only one of them is set, or both are empty outside Development, the host stops with an error. A malformed value also stops it. The API also needs `AzureAd:ClientId` set to its own client id, so the token audience check passes.

## 6. Settings for Entra ID mode

Set the values once in the AppHost, and it passes them to the API and the Web. Option A is the route for the AppHost. Option B sets them per host and is still supported.

### Option A: four AppHost settings

Run these from the repository root, with your own values. The AppHost reads its settings from its user secrets, environment variables or the command line. Empty values are ignored.

```bash
dotnet user-secrets set "AzureAd:TenantId" "<tenant-guid>" --project src/SharpVault.AppHost
dotnet user-secrets set "AzureAd:Api:ClientId" "<api-client-guid>" --project src/SharpVault.AppHost
dotnet user-secrets set "AzureAd:Web:ClientId" "<web-client-guid>" --project src/SharpVault.AppHost
dotnet user-secrets set "AzureAd:Web:ClientSecret" "<web-client-secret>" --project src/SharpVault.AppHost
dotnet run --project src/SharpVault.AppHost
```

The AppHost forwards each setting as follows:

| AppHost setting | Environment variable form | Sent to | As |
|---|---|---|---|
| `AzureAd:TenantId` | `AzureAd__TenantId` | API and Web | `AzureAd__TenantId` |
| `AzureAd:Instance` | `AzureAd__Instance` | API and Web | `AzureAd__Instance`. Optional. The default in `appsettings.json` applies when it is not set. |
| `AzureAd:Api:ClientId` | `AzureAd__Api__ClientId` | API | `AzureAd__ClientId` |
| `AzureAd:Api:ClientId` | `AzureAd__Api__ClientId` | Web | `SharpVaultApi__Scopes__0` = `api://{value}/access_as_user` |
| `AzureAd:Web:ClientId` | `AzureAd__Web__ClientId` | Web | `AzureAd__ClientId` |
| `AzureAd:Web:ClientSecret` | `AzureAd__Web__ClientSecret` | Web only | A secret parameter, passed as `AzureAd__ClientCredentials__0__SourceType=ClientSecret` and `AzureAd__ClientCredentials__0__ClientSecret` |

Notes:

- Only run mode (`dotnet run`) forwards these values. Publish mode forwards nothing.
- A setting that is not set is not forwarded, so each host keeps reading its own user secrets (Option B). A partial set, for example only the tenant, is forwarded as it is, and the hosts stop at startup.
- The API never receives a secret. The Web client secret is a secret parameter in the AppHost, and the dashboard masks it.
- The forwarded values are environment variables of each host, so they take precedence over the same keys in that host's user secrets.
- A client secret is accepted only in Development (section 3).

### Option B: user secrets for each host

Use this when you run the API or the Web outside the AppHost, or when a host needs a value that differs from the AppHost. Run these from the repository root, with your own values:

```bash
dotnet user-secrets set "AzureAd:TenantId" "<tenant-guid>" --project src/SharpVault.Api
dotnet user-secrets set "AzureAd:ClientId" "<api-client-guid>" --project src/SharpVault.Api

dotnet user-secrets set "AzureAd:TenantId" "<tenant-guid>" --project src/SharpVault.Web
dotnet user-secrets set "AzureAd:ClientId" "<web-client-guid>" --project src/SharpVault.Web
dotnet user-secrets set "AzureAd:ClientCredentials:0:SourceType" "ClientSecret" --project src/SharpVault.Web
dotnet user-secrets set "AzureAd:ClientCredentials:0:ClientSecret" "<web-client-secret>" --project src/SharpVault.Web
dotnet user-secrets set "SharpVaultApi:Scopes:0" "api://<api-client-guid>/access_as_user" --project src/SharpVault.Web
```

Optional, for the API:

```bash
dotnet user-secrets set "Tenancy:AllowGuests" "false" --project src/SharpVault.Api
```

To list or remove a value, use `dotnet user-secrets list --project <project>` and `dotnet user-secrets remove "<key>" --project <project>`.

## 7. Verify sign-in

Sign-in needs a real tenant, so it is verified by hand in a deployment or a development tenant.

1. Set the AppHost settings (Option A), start the AppHost with `dotnet run --project src/SharpVault.AppHost`, and open the web URL from the dashboard.
2. Select **Sign in**. You should be redirected to Entra ID, and after sign-in the home page shows your name.
3. Open the account page. It shows your object id and an Administrator tag only when the API reports `IsAdmin` for you (you hold `SharpVault.Admin`).
4. Call `GET /api/me` on the API with a token for `access_as_user`. It returns 200 with your user id when you hold a role. Without a role it returns 403, and the account page sends you to the access-denied page.
5. Select **Sign out**. The signed-out page opens after Entra redirects back through `/signout-callback-oidc`.

If the sign-in page shows the sign-in error page, check the redirect URIs and the client secret first. The error page does not show the cause.

If the account page says that signing in again did not help, the API still refuses the token after a fresh sign-in. Check the role assignment (section 4) and that `AzureAd:ClientId` on the API is its own client id.

Known limits, none of them verified without a tenant:

- Token acquisition from the interactive page (the web app calls the API from its Blazor circuit) is unverified.
- The web app maps both Microsoft Identity's challenge exception and MSAL's `MsalUiRequiredException` to a sign-in redirect (unit tests with thrown exceptions only). Whether MSAL raises either of them from a circuit with no `HttpContext` is not verified without a tenant.
- Real Microsoft Graph directory search and the `User.ReadBasic.All` consent are not verified without a tenant.

## 8. Running more than one instance

The repository does not run more than one Web or API instance today. This section lists what a deployment with several instances needs, and what each choice costs.

| Concern | What the code does | What a deployment needs |
|---|---|---|
| Blazor circuits | A circuit lives on the instance that opened it | Session affinity (sticky sessions) on the Web |
| Sign-in cookie protection | `AddDataProtection` is not configured, so each instance uses its own keys | A shared Data Protection key ring. Without one, a failover means one more sign-in. It fails closed, not open |
| MSAL token cache | Per process, encrypted (`Encrypt = true`). Refresh tokens stay in memory | Nothing for correctness. A cache miss gives one sign-in redirect |
| Sign-out across instances | The API keeps the revocation ledger (`SessionRevocations`). Every Web instance asks the API about its sessions once a minute | Nothing extra. A signed-out session ends on every instance within about a minute, and a restart does not forget it |
| Lock across instances | Every API instance refuses a locked user once its own lock cache expires. Every Web instance then ends that user's sessions at its next check | Nothing extra. The lock is seen everywhere within the cache time plus about a minute |
| Web's local revocation store | Per process (`AddDistributedMemoryCache`). It is a fast local copy, and the API is the reference | Nothing extra |
| Lock status cache | Per API process, `UserAccess:CacheTtl` (5 seconds by default) | Nothing extra. Another instance may see a lock up to the TTL later |
| Rate limits | Per process. N instances allow N times the limit | A distributed limiter, if the limit has to hold across instances |
| Idle tracking | Per process (`SessionActivityMap`), per session | Affinity keeps a circuit on one instance |

The sensitive material stays in process memory. The ledger row holds only a user id, a random session id and a date.
