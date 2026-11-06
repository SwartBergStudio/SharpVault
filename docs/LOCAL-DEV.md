# Local development

## Prerequisites

- .NET 10 SDK. `global.json` pins 10.0.100 with `rollForward` set to `latestFeature`, and pins the Aspire SDK to 13.6.4.
- Docker, running. The AppHost runs Azurite in a container, and the integration tests start their own Azurite container.
- Optional: the HTTPS profile needs a trusted development certificate. If it fails with a certificate error, run `dotnet dev-certs https --trust`.
- Optional: Entra ID settings, only if you want to sign in with a real Entra ID tenant. Without them the app runs in local mode (see below). The Entra steps are in [ENTRA-SETUP.md](ENTRA-SETUP.md).

Check Docker before you start:

```bash
docker info
```

## Run the AppHost

```bash
dotnet run --project src/SharpVault.AppHost
```

This starts the Aspire AppHost with these resources:

- `storage`: Azurite, run as an emulator container with a persistent lifetime and a named data volume.
- `tables`: the Table Storage endpoint on that emulator. The API references it.
- `api`: `SharpVault.Api`, which waits for `tables` and is checked on `/health`.
- `web`: `SharpVault.Web`, which waits for `api` and is exposed as an external endpoint.

The launch profiles in `src/SharpVault.AppHost/Properties/launchSettings.json` set `ASPNETCORE_ENVIRONMENT` to `Development`, so the API serves OpenAPI at `/openapi/v1.json` and Scalar at `/scalar`.

With no Entra ID settings, `api` and `web` start in local mode. The AppHost does not need any setting for this.

## Local mode (no Entra ID)

The authentication mode is decided from configuration when each host starts, by the same rule in both hosts:

| `AzureAd:TenantId` | `AzureAd:ClientId` | Environment | Result |
|---|---|---|---|
| set | set | any | Entra ID mode |
| not set | not set | Development | Local mode |
| not set | not set | any other | The host stops at startup. Entra ID settings are required outside Development. |
| only one set | | any | The host stops at startup, naming the missing key. |

Local mode is a development convenience. It runs only in Development, and it never calls Entra ID.

### Sign in

1. Open the `web` URL from the Aspire dashboard.
2. Select **Sign in**. The web app sends you to the account picker at `/account/local-sign-in`.
3. Choose one of the four accounts:

| Account | Roles |
|---|---|
| Admin One | `SharpVault.Admin`, `SharpVault.User` |
| Admin Two | `SharpVault.Admin`, `SharpVault.User` |
| User One | `SharpVault.User` |
| User Two | `SharpVault.User` |

The web app keeps a cookie session for 8 hours. For each API call it signs a short-lived token (5 minutes) with the local key, and the API checks that token with the standard JWT bearer pipeline. The account ids are fixed, and the names and roles come from one code catalogue, so the picker, the token and the API always agree.

The API inserts the four accounts into the `Users` table when it starts. A row that already exists is never changed, so a lock or a display name edit survives restarts. The first request by a seeded account records a sign-in, as it does for an Entra ID user.

Admin One and Admin Two see **Users** in the side navigation. The page at `/admin/users` lists the four accounts, and "Never" marks an account that has not signed in. User One and User Two see "Administrators only" on that page, and the page does not call the admin list API for them (it still reads `GET /api/me`). The admin link and the page both follow `IsAdmin` from `GET /api/me`, and the API still checks every call.

Each row on that page has a **Counts** button, which is also available for the admin's own row. It shows two numbers for that user: **Secrets** (active secrets, not the recycle bin and not old versions) and **Shares on their items** (share grants the user has given on their items). The numbers load only when the button is clicked, the list itself makes no count calls, and reading them writes no audit row. No names or values are shown. A very large vault shows a warning that the real numbers are higher.

Signed-in users who open `/` go to the vault. The side navigation has no Home link, and the brand link at the top leads to `/`.

Signed-in pages run in Interactive Server mode, so they hold a connection to the web app. Signed-out pages are plain server-rendered HTML.

### Using the vault

Each local account has its own vault. Open **Vault** in the side navigation to add secrets, folders, and recycle bin items. Admin One and Admin Two see only their own vault, as User One and User Two do. The admin role does not let an admin see another user's secrets.

Secrets, folders and the recycle bin survive restarts. The data stays in the persistent Azurite volume, and the development key stays in the AppHost user secrets (see [Development encryption key](#development-encryption-key)). Stopping the AppHost, or restarting it, keeps the data.

### Sharing with the local accounts

Sharing works between the four accounts. Each account signs in from its own browser profile, or from a private window, because the local sign-in is a cookie in that profile. Signing out and signing in as the next account also works.

1. Sign in as User One and open **Vault**. Create a folder and a login.
2. Open the login and use its share panel. Type at least three letters of User Two's name or email, pick User Two, choose View, Edit or Manage, and add the share.
3. Sign in as User Two and open **Shared with me**. The login appears under User One. Reveal the value (the reveal is audited), or file the item into one of User Two's folders.
4. Sign in as User One and revoke the share from the share panel. User Two loses the item at the next request.

The directory search in local mode reads the local accounts table, so only the four accounts can be found. See [SHARING.md](SHARING.md).

### Transfers with the four local accounts

A transfer needs one admin to request it and a different admin to approve it. The request is for User One's vault, with User Two as the receiver. Admin One requests and Admin Two approves. Use separate browser profiles or private windows for each account (see above).

1. Sign in as User One and create a few folders, including a subfolder, and some logins. Sign out.
2. Sign in as Admin One and open **Users**. Lock User One from its row (the lock button has the id `user-lock-{id}`). Choose the category **Dismissed** and write a reason of at least 20 characters. A locked user's API calls are refused once the lock status cache expires (5 seconds by default), so User One's vault cannot be opened. An open User One page ends within about a minute and shows "Sign in required".
   The transfer form also offers to lock the source user when the category needs it.
3. Still as Admin One, click **Transfer** on User One's row (or open **Transfers** and choose **New transfer**). Check that the leaving user is User One and the receiving user is User Two. Leave **What moves** on all items, keep **Dismissed**, write a reason of at least 20 characters, and choose **Request transfer**. The request is pending.
4. Admin One cannot approve their own request. Sign in as Admin Two, open **Transfers**, open the request and choose **Approve**, then confirm. The approval starts the job at once. The detail page shows the state as the job moves through Staging, Releasing, Publishing and Reassigning, and ends at Completed.
5. Sign in as User Two. The vault shows a notice that a transfer was received, and a folder named `Transferred from User One ({today})` holds User One's folders and logins. Under **Shares you gave** the shares that User One had given on those items now appear for User Two.
6. Check the other side. User One is still locked, so their vault does not open. Sign in as Admin One and unlock User One (the `user-unlock-{id}` button on the row). Sign in as User One: the vault is empty, because everything moved.

To try a rejection instead of an approval, Admin Two can choose **Reject** and give a reason, or Admin One can choose **Withdraw** on their own request. The source user never gets a notice. Admin One, Admin Two, User One and User Two see the Transfers link only when the API says they are admins, and User One and User Two never see it.

The transfer runs inside the API process. The background runner polls every 15 seconds, and approving a transfer wakes it up. Set `Transfers:Enabled` to `false` in the API's configuration to stop the runner. Requests still work, but nothing moves until the runner is enabled again. See [TRANSFERS.md](TRANSFERS.md) for the states, the limits and the known gaps.

### Idle sign-out

The web app signs a user out after a period without input. The default is 15 minutes. Set `IdleLock:Timeout` (for example `00:05:00`) in the web app's configuration to change it. The allowed range is 1 minute to 8 hours. There is no screen to change it yet.

- The browser timer does the sign-out. Activity in any tab resets it, and it is shared through `localStorage`.
- The server also ends the session when there has been no inbound activity for the timeout plus 2 minutes. A blocked script therefore cannot keep a session alive on an interactive page. The browser sends a heartbeat at most once a minute while the user is active, so an active user is not signed out.
- The server limit counts activity on the page's circuit. Static signed-in pages (access denied, sign-in error, signed out and the other static pages) hold no circuit and no vault data, so they rely on the browser timer.
- Closing the tab and opening the app again after the timeout signs the user out. Opening it within the timeout keeps the session. A new sign-in starts a fresh timer.

### Where the picker is reachable

The picker and its form submission return `404` unless all of these are true:

- The app runs in local mode.
- The request comes from the loopback address and the `Host` header is `localhost`, `127.0.0.1` or `[::1]`.
- The request has none of these headers: `Forwarded`, `X-Forwarded-For`, `X-Forwarded-Host`, `X-Forwarded-Proto`, `X-Original-For`, `X-Original-Proto`, `X-Original-Host`, `Via` or `X-Real-IP`. Other `X-Forwarded-*` headers, such as `X-Forwarded-Port`, do not hide the picker. The guard does not check them.
- The host is not running with `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. That mode makes the framework trust any caller's `X-Forwarded-For`, so the picker is refused for every request while it is on.

A reverse proxy in front of the web app therefore hides the picker.

One risk remains. A proxy on the same machine that sends a loopback `Host` header and adds no forwarded headers cannot be told apart from a direct call. Never expose a local-mode host through a proxy.

### The local signing key

The web app and the API share one signing key, stored in a file:

- Windows: `%LOCALAPPDATA%\SharpVault\dev-auth-key`.
- Linux: the local application data folder, normally `~/.local/share/SharpVault/dev-auth-key`.

Each host creates the file on first start if it is missing. Both hosts must read the same file. The web app signs tokens with the key and the API validates them, so if the hosts read different files, the API answers `401` and the web app logs a warning that asks you to check the mode and the key file.

- On Linux and macOS the file is owner-only. If it is readable by other users, the host refuses to start and prints a `chmod 600` hint.
- On Windows the file uses the ACL of your user profile.
- If the file is empty, is not base64, or has the wrong length, the host stops at startup. The file is not overwritten. Delete it and restart both hosts.
- To set a different path, use the `LocalAuth:KeyFilePath` setting in each host. Both hosts must resolve to the same file.

To rotate the key, stop both hosts, delete the file, and start both hosts again. Deleting the file while one host is still running makes that host's tokens fail until both are restarted. Deleting the key does not delete the seeded accounts or their data.

### Switch to Entra ID

Set the four AppHost settings described in [ENTRA-SETUP.md](ENTRA-SETUP.md#6-settings-for-entra-id-mode). When both `AzureAd:TenantId` and `AzureAd:ClientId` reach the hosts, local mode turns off:

- The picker returns `404`.
- Sign-in redirects to Microsoft.
- Local tokens fail in the API, because the issuer, audience and signature differ.

To go back to local mode, remove those settings and restart the AppHost.

## Development encryption key

In run mode the AppHost adds a secret parameter, `development-key-material`, and passes it to `api` as `Encryption__DevelopmentKeyMaterial`. The API then wraps data keys with the development key provider, which only runs in the Development environment. The AppHost generates the value once, with 64 characters, and stores it in the AppHost user secrets under `Parameters:development-key-material`. Later runs reuse it, so data in the persistent Azurite volume stays readable.

Publish mode declares no parameter, and the web app receives no encryption settings. To reset the key, remove it together with the local data, because data encrypted with the old key cannot be decrypted with a new one:

```bash
dotnet user-secrets remove Parameters:development-key-material --project src/SharpVault.AppHost
```

Then wipe the Azurite volume as described in [Wipe local data](#wipe-local-data). The design and the Key Vault setup are in [ENCRYPTION.md](ENCRYPTION.md).

Profiles:

- `https` (the default): dashboard at `https://localhost:17260`.
- `http`: dashboard at `http://localhost:15260`.

Choose a profile with:

```bash
dotnet run --project src/SharpVault.AppHost --launch-profile http
```

The dashboard shows the URLs of `api` and `web`.

### If the local key changes

Vault data is encrypted with the development key. If the key material changes while vault data exists (for example after the `dotnet user-secrets remove` command above, without wiping the volume), the old rows cannot be opened:

- The secret list shows each item as damaged, with no name. A search returns no results.
- Reveal and history restore return 500 with the message "The item could not be read.". The response gives no reason.
- Creating, editing or moving an item fails with a server error, because the vault's metadata key cannot be opened.
- Deleting a secret still works, because it does not open the keys.

The data is not lost while the Azurite volume exists. If you put the old key material back, the rows read again. To start with an empty vault, wipe the volume and the key together, as described above. See [VAULT.md](VAULT.md) for how a damaged item looks in the app.

## Persistent Azurite

The storage emulator uses `ContainerLifetime.Persistent`, so the container keeps running after the AppHost stops, and it keeps its data in a named Docker volume. Your local data survives between debug sessions.

Stop the container without losing data:

```bash
docker ps --filter "ancestor=mcr.microsoft.com/azure-storage/azurite:3.35.0"
docker stop <container-id>
```

## Wipe local data

Remove the container and the volume. Aspire names the volume with the `-storage-data` suffix, so find it first:

```bash
docker ps -a --filter "ancestor=mcr.microsoft.com/azure-storage/azurite:3.35.0"
docker rm -f <container-id>
docker volume ls --filter "name=storage-data"
docker volume rm <volume-name>
```

The next AppHost run creates a fresh container and volume, and the API seeds the four local accounts again on its first start.

## Run the tests

The solution is `SharpVault.slnx`. Maui is not part of the Linux build (it is planned in `feature/maui-app`), so the whole solution builds and tests on Linux today. No test needs an Entra tenant: the tests sign tokens with locally generated keys.

```bash
dotnet build SharpVault.slnx
dotnet test SharpVault.slnx --filter "Category!=Performance&Category!=E2E&Category!=SiteTour"
```

Filters:

```bash
dotnet test SharpVault.slnx --filter "Category=Security"
dotnet test SharpVault.slnx --filter "Category=Performance"
dotnet test SharpVault.slnx --filter "FullyQualifiedName~HealthEndpointTests"
```

- `Category=Security` selects the security tests.
- `Category=Performance` selects the timing tests in `PerformanceTestGroup`. They run only in CI on pushes to `main`.
- `Category!=Performance&Category!=E2E&Category!=SiteTour` is the default for local runs and the test step in CI. The filter has to name all three categories: `SharpVault.slnx` includes the browser project, and the screenshot tour must never run by default.
- `Category=E2E` selects the browser journeys in `tests/SharpVault.E2ETests`. They need Docker, the system Chrome and a built Api and Web. CI runs them in the `e2e` job.
- `Category=SiteTour` selects the screenshot tour. It is never run in CI. See [TESTING.md](TESTING.md#browser-e2e-tests) for the commands.

The test projects, collections and helpers are described in [TESTING.md](TESTING.md).

## Coverage

```bash
dotnet test SharpVault.slnx -c Release --filter "Category!=Performance&Category!=E2E&Category!=SiteTour" --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults
dotnet tool restore
dotnet reportgenerator "-reports:TestResults/**/coverage.cobertura.xml" "-targetdir:TestResults/coverage" "-reporttypes:Cobertura"
dotnet run eng/CoverageGate.cs -- TestResults/coverage/Cobertura.xml SharpVault.Application=80 SharpVault.Infrastructure=80 SharpVault.Authentication=90 SharpVault.Domain.Encryption=95 SharpVault.Infrastructure.Encryption=95 SharpVault.PasswordGeneration=95
```

The thresholds are the same as the `Coverage gate` step in `.github/workflows/ci.yml`: 80% for `SharpVault.Application` and `SharpVault.Infrastructure`, 90% for `SharpVault.Authentication`, and 95% for the encryption namespaces `SharpVault.Domain.Encryption` and `SharpVault.Infrastructure.Encryption` and for `SharpVault.PasswordGeneration`. The Authentication value is 90 in CI. The implementation plan had proposed 85, and that choice is open for the owner (see [PROGRESS.md](PROGRESS.md)). `eng/CoverageGate.cs` fails the command when a namespace prefix is below its threshold. ReportGenerator cannot enforce thresholds itself. The benchmark project is excluded from coverage in `tests/coverage.runsettings`.

## Benchmarks

`tests/SharpVault.Benchmarks` contains BenchmarkDotNet benchmarks for the encryption hot paths: `Encrypt`, `Decrypt` and `ReEncrypt` on 32 bytes, 1 KiB and 32 KiB, and `CreateDataKey`, `OpenDataKey` and `RewrapDataKey` on the development provider. The Key Vault paths are network bound and are covered by call-count tests instead.

Run them in Release, with the short job for real numbers:

```bash
dotnet run -c Release --project tests/SharpVault.Benchmarks -- --filter '*' --job short
```

The benchmark project is not a test project. `dotnet test` does not run it. Pull requests that touch these paths paste the before and after tables into the description. CI runs `--job dry` as a smoke test on pushes to `main`. The run fails only when a benchmark fails, and the results are not compared.

## Vulnerable packages

```bash
dotnet list SharpVault.slnx package --vulnerable --include-transitive
```

CI fails when this reports vulnerable packages. Restore also runs NuGet audit for all packages, including transitive ones, at low severity and above, and warnings are errors.

## Build warnings

`Directory.Build.props` sets warnings as errors, so a warning fails the build. `dotnet build SharpVault.slnx` should report `0 Warning(s)` and `0 Error(s)`.
