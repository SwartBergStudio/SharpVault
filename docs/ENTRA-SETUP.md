# Microsoft Entra ID setup

SharpVault signs users in with Microsoft Entra ID. It supports one tenant per deployment (TEN-1). Only members of that tenant can sign in, and guests are rejected unless `Tenancy:AllowGuests` is set (TEN-3).

This repository ships placeholders only. No tenant id, client id, client secret or scope is committed, and `appsettings*.json` contains only the Entra authority (`https://login.microsoftonline.com/`) and the callback paths. Sign-in cannot be exercised until you register the two applications below in a real tenant and set the values from section 5.

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
   - **Development only**: create a client secret under **Certificates & secrets** and store it with `dotnet user-secrets` (section 5).

## 4. Assign users to roles

1. Go to **Enterprise applications**, select **SharpVault API**, then **Users and groups**.
2. Select **Add user/group**, pick a user, and choose the role `SharpVault.User` or `SharpVault.Admin`.

Every API request needs one of the two roles (TEN-4). A signed-in user with no role gets 403 from the API, and the account page then sends the user to the access-denied page. Admin rights never grant access to other users' data, even with `SharpVault.Admin`.

The API rejects a locked user with 403 on every request. Locking is a later feature (`feature/user-lock`).

## 5. Configuration keys

| Key | Set in | Value | Notes |
|---|---|---|---|
| `AzureAd:Instance` | `appsettings.json` (both hosts) | `https://login.microsoftonline.com/` | Absolute https URI with path `/`, no query or user info. |
| `AzureAd:TenantId` | user secrets or environment (both hosts) | Directory (tenant) id, GUID | Required. Multi-tenant values such as `common` are rejected. |
| `AzureAd:ClientId` | user secrets or environment | API client id on `SharpVault.Api`, Web client id on `SharpVault.Web` | Required GUID. |
| `AzureAd:CallbackPath` | `appsettings.json` (Web) | `/signin-oidc` | Must match the registered redirect URI. |
| `AzureAd:SignedOutCallbackPath` | `appsettings.json` (Web) | `/signout-callback-oidc` | Must match the registered redirect URI. |
| `AzureAd:ClientCredentials:0:SourceType` | user secrets (Development) or environment (other environments) | `ClientSecret` in Development. Elsewhere `SignedAssertionFromManagedIdentity`, `KeyVault`, `StoreWithThumbprint` or `StoreWithDistinguishedName` | Section 3 describes the choice. |
| `AzureAd:ClientCredentials:0:ClientSecret` | user secrets only | The Web client secret | Development only. Never in a file in the repository. |
| `SharpVaultApi:BaseAddress` | `appsettings.json` (Web) | `https+http://api` | Aspire service discovery. |
| `SharpVaultApi:Scopes:0` | user secrets or environment (Web) | `api://{api-client-id}/access_as_user` | At least one scope is required. |
| `Tenancy:AllowGuests` | user secrets or environment (API) | `false` by default | Guests stay rejected unless this is `true`. Admin settings will manage it later (ADM-1). |
| `UserAccess:CacheTtl` | optional (API) | `00:00:05` by default | Between 0 and 30 seconds. `0` disables the lock-status cache. |
| `UserAccess:CacheMaxEntries` | optional (API) | `10000` by default | Between 100 and 1,000,000. |
| `UserAccess:SignInRecordInterval` | optional (API) | `01:00:00` by default | Between 1 minute and 24 hours. A sign-in is recorded at most once per interval. |

Both hosts check `AzureAd:TenantId` and `AzureAd:ClientId` when they start, and they stop with an error if a value is missing or malformed. The API also needs `AzureAd:ClientId` set to its own client id, so the token audience check passes.

## 6. Local settings with user secrets

The API and the Web each have a `UserSecretsId`. Run these commands from the repository root, with your own values. The AppHost starts both projects, and each project reads its own user secrets in Development.

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

1. Start the AppHost with `dotnet run --project src/SharpVault.AppHost`, and open the web URL from the dashboard.
2. Select **Sign in**. You should be redirected to Entra ID, and after sign-in the home page shows your name.
3. Open the account page. It shows your object id and an Administrator tag only when you hold `SharpVault.Admin`.
4. Call `GET /api/me` on the API with a token for `access_as_user`. It returns 200 with your user id when you hold a role. Without a role it returns 403, and the account page sends you to the access-denied page.
5. Select **Sign out**. The signed-out page opens after Entra redirects back through `/signout-callback-oidc`.

If the sign-in page shows the sign-in error page, check the redirect URIs and the client secret first. The error page does not show the cause.
