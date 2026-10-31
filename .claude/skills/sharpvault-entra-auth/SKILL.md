---
name: sharpvault-entra-auth
description: SharpVault authentication and authorization with Microsoft Entra ID. Use when working on sign-in, tokens, app roles, authorization policies, the current user, the MAUI MSAL login, or managed identity access to Azure resources. Use this instead of dotnet-auth in this repository; SharpVault does not use ASP.NET Identity or its own JWTs.
---

# Microsoft Entra ID in SharpVault

SharpVault does not store passwords for its own users and does not issue its own tokens. All sign-in goes through Microsoft Entra ID.

## App registrations

- **SharpVault API**: exposes the scope `access_as_user` and defines the app roles `SharpVault.Admin` and `SharpVault.User`.
- **SharpVault Web**: signs users in with OpenID Connect and calls the API with the `access_as_user` scope.
- **SharpVault MAUI**: public client using MSAL.NET with the system browser and the same scope.
- Roles are assigned to users or groups in Entra under Enterprise applications. Steps live in `docs/ENTRA-SETUP.md`.

## API

- `builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))`.
- Policies in a `Policies` constants class: `Policies.Admin` requires role `SharpVault.Admin`; `Policies.User` requires `SharpVault.User` or `SharpVault.Admin`.
- Every endpoint group calls `.RequireAuthorization(...)` with one of these policies. Admin endpoints (transfer, audit, user list) use `Policies.Admin`.
- Authorization of individual secrets is done in handlers: the current user must own the secret or have a share with the right permission.

## Current user

- `ICurrentUser` in `SharpVault.Application/Common/Interfaces` exposes `UserId` (Guid from the `oid` claim), `DisplayName`, `IsAdmin`.
- Users are always identified by `oid`, never by email or UPN, because those can change.
- On first sign-in, upsert the user into the `Users` table so admins can find them for transfers.

## Web

- `AddMicrosoftIdentityWebApp` with `EnableTokenAcquisitionToCallDownstreamApi` and a distributed token cache.
- Call the API through a typed `HttpClient` that attaches the user's access token.
- Use `AuthorizeView` and `[Authorize(Policy = ...)]` in components, but the API is always the real security boundary.

## MAUI

- MSAL `PublicClientApplication` with the system browser, tokens cached in `SecureStorage`.
- Same API client and scope as the web app.

## Azure resources

- The API reaches Table Storage and Key Vault with `DefaultAzureCredential` (managed identity in Azure, developer credentials locally).
- Required roles: "Storage Table Data Contributor" on the storage account and "Key Vault Crypto User" on the Key Vault.
- No client secrets, storage keys or connection strings with keys in configuration.

## Tests

- Integration tests replace Entra with a test authentication handler that issues `oid`, `name` and role claims. See `sharpvault-integration-testing`.
