# One-time links

A one-time link lets someone create a link to one secret. Anyone with the link can open it once, including people outside the company. The link is single-use: the first successful open destroys it. It also expires, and its creator can revoke it.

Requirements: OTL-1 to OTL-9 in [REQUIREMENTS.md](REQUIREMENTS.md#5-one-time-share-links).

## Flow

1. **Snapshot.** The owner, or a user with Manage on the secret, calls `POST /api/one-time-links/owners/{ownerId}/secrets/{secretId}/snapshot`. The API decrypts the secret and returns a snapshot (type, name, username, URL, value, notes). The reveal is audited as `OneTimeLinkSnapshotRevealed`.
2. **Seal in the browser.** The create page (`/one-time-links/new/{OwnerId}/{SecretId}`, in `SharpVault.UI.Shared`) generates a 32-byte link key with `crypto.getRandomValues`. It encrypts the snapshot with AES-256-GCM. If the creator typed a passphrase, the key is also derived from it with PBKDF2-HMAC-SHA256 (600,000 iterations). The passphrase must be at least 8 characters, checked in the browser only.
3. **Store.** The browser posts the sealed blob (base64) to `POST /api/one-time-links/owners/{ownerId}/secrets/{secretId}`. The API stores the blob, the creator, the owner, the secret id and the times. It writes `OneTimeLinkCreated`. The response is `201` with a `OneTimeLinkDto` (`Id`, `OwnerId`, `ItemId`, `ItemName`, `CreatedAt`, `ExpiresAt`).
4. **Share.** The browser builds the URL `{base}/l/{linkId}#{K}`, or `#p.{K}` when a passphrase is set. The fragment is never sent to any server. The URL is written only into the page DOM. Key material does not cross Blazor JS interop.
5. **Open.** The recipient loads `/l/{linkId}`. This is a static page: anonymous, no circuit, and no API call on GET. Its script reads the fragment, and if there is a passphrase it asks for it. It then posts to `POST /l/{linkId}/open` on the Web, which relays to the API. Decryption happens in the browser.
6. **Notice.** A successful open writes a notice in the creator's partition, and removes the bookkeeping for the link. The creator sees it as an in-app notice on the next load of the vault or links pages (`GET /api/one-time-links/notices`, dismissed with `DELETE /api/one-time-links/notices/{noticeId}`). No email or push is sent.

Revoke is `DELETE /api/one-time-links/{linkId}`. List is `GET /api/one-time-links`, paged with `ContinuationToken`. Both act only on the caller's own links.

## What the server can and cannot see

- **Server-side encryption, not zero-knowledge.** The server decrypts the secret during the snapshot step (the same as a reveal), so for that request the plaintext passes through the API. The server does not keep it.
- **After the snapshot, the server holds only opaque bytes.** The stored blob is encrypted with a key the server never receives. The server cannot read the link contents at any time afterwards.
- **The server never sees the link key or the passphrase.** Neither is in the POST body, the Web relay or the API. The API checks only that the blob is between 30 bytes and 131,072 bytes and that its first byte is `0x80` or higher (`CreateOneTimeLinkCommandValidator`). It does not decrypt or verify the blob.
- **Passphrase.** The server holds nothing derived from the passphrase, so it cannot check a guess. A wrong passphrase costs nothing on the server. A visitor who has the link but not the passphrase can still spend the link. The creator then sees a notice.
- **The Web sees the visitor's address and user agent.** It passes them to the API in a signed proof (see [Forwarded headers](#forwarded-headers)). They are written to the audit log for the open.

## Limits

| Limit | Value | Where |
|---|---|---|
| Default lifetime | 24 hours, clamped to the maximum | `OneTimeLinkLimits.DefaultLifetime` |
| Minimum lifetime | 5 minutes. An explicit lifetime under that, or over the maximum, returns 400 | `OneTimeLinkLimits.MinLifetimeMinutes` |
| Maximum lifetime | 7 days by default. Allowed range 1 hour to 7 days | `OneTimeLinks:MaxLifetime` |
| Payload | 30 to 131,072 bytes | `OneTimeLinkLimits.MaxPayloadBytes` |
| Active links per creator | 100. The count check is not atomic, so parallel creates can overshoot a little | `OneTimeLinkLimits.MaxActiveLinksPerCreator` |
| Opens per visitor | 20 per minute by default, partitioned by the visitor address from the proof. Over the limit the Web returns 429 `{"status":"rate-limited"}` before any storage call | `VaultRateLimits:OneTimeLinkOpensPerMinute` (1 to 100,000) |
| Passphrase | At least 8 characters, checked in the browser only | Create script (`one-time-link-create.js`) |

## Open responses

Every failure gives the same answer, so a probe cannot tell an unknown, used, expired, revoked or locked link from another:

- Success: the Web returns the payload from the API (`200`).
- Rate limited: `429` with `{"status":"rate-limited"}`.
- Anything else, including API errors: `404` with `{"status":"unavailable"}`.

The API returns `404` for every refused open. The check order is: feature enabled, link present and not expired, creator `Active`, then a conditional delete with the ETag from the read. Only the request that wins the delete gets the payload and writes `OneTimeLinkOpened`. The gateway proof is checked before the open handler, so a request with a bad proof does not consume the link.

Timing differs slightly by state (one read for most states, two for a locked creator, none when the feature is off). This is accepted because the id is a random 122-bit value and opens are rate limited.

## Lock, unlock and expiry

- **Lock.** Locking a user revokes all of that user's active links, each with a `OneTimeLinkRevoked` row. A repeat lock also runs the sweep. Details in [TRANSFERS.md](TRANSFERS.md#locking-and-unlocking).
- **Unlock.** A locked user's links are swept before the status changes. Their links stay revoked.
- **Open after lock.** The open handler reads the creator's status on every open, so a locked creator's links do not open even if the sweep is incomplete.
- **Expiry purge.** Each link has an expiry row in an hourly partition (`e_{yyyyMMddHH}`, UTC). The purge (`OneTimeLinks:PurgeEnabled`, on by default) walks each expiry hour from its last completed run through the current hour, and deletes each expired link with its index row and expiry row. With no earlier run, it starts at the maximum lifetime plus an hour before now. It holds the lease `one-time-link-purge`. Expired links are refused on open whether or not the purge has run.

## Snapshot rule: a link is a copy

A link is a snapshot. Its contents are copied at create time and do not change, so an edit never reaches it. The edit page warns about the caller's own open links to that secret and offers to revoke them. It reads at most 5 pages of 100 links. It cannot see links made by other people with Manage.

The open handler also checks that the secret is still live in the owner's vault. If the secret was recycled, purged or transferred away, every open gets the generic 404, for links from every creator. The link is not consumed, so it opens again if the secret is restored before the link expires. It stays on the creator's list until it expires or is revoked.

Revoking the creator's Manage grant does not stop a link. Only the checks above, the creator's lock, a revoke, the link's own expiry, or an open do.

The purge's crypto-shredding (see [ENCRYPTION.md](ENCRYPTION.md#history-and-crypto-shredding)) deletes the vault's wrapped data keys. It does not cover a link copy. The link's blob is encrypted under the link key, which the vault key ring does not wrap. A copy of a link therefore lives until it is opened, revoked or expires, whatever happens to the secret.

## Configuration

| Key | Set on | Default | Notes |
|---|---|---|---|
| `OneTimeLinks:Enabled` | API | `false` | Off by default outside local runs. The AppHost run mode sets it to `true` on `api` (`OneTimeLinks__Enabled`). When it is off, create and snapshot return 403, and open returns the generic 404. List, revoke, notices, the lock sweep and the purge still work. |
| `OneTimeLinks:MaxLifetime` | API | 7 days | Between 1 hour and 7 days. |
| `OneTimeLinks:PurgeEnabled` | API | `true` | |
| `OneTimeLinks:PurgeInitialDelay` | API | 1 minute | |
| `OneTimeLinks:PurgeInterval` | API | 5 minutes | Between 1 minute and 1 hour. |
| `OneTimeLinks:PurgeLeaseDuration` | API | 5 minutes | Between 1 and 30 minutes. |
| `WebGateway:Key` | API and Web | none | Shared secret for the Web-to-API proof. Required outside Development on both hosts, at least 32 characters after trimming. |
| `VaultRateLimits:OneTimeLinkOpensPerMinute` | API | 20 | Between 1 and 100,000. |

`OneTimeLinks:Enabled` set to `true` without a valid `WebGateway:Key` on the API stops the API at startup, naming the setting and not the value. The API also refuses to start with `WebGateway:Key` set but shorter than 32 characters.

## Deployment

Do these in order before the first production deployment with links enabled.

1. **Generate one key** of at least 32 characters, for example 48 random bytes encoded as base64 (`[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))` in PowerShell). Keep it in a secret store.
2. **Set `WebGateway:Key` to the same value on both hosts** (`WebGateway__Key` as an environment variable). It must not be in `appsettings*.json` or in the repository. Outside Development both hosts refuse to start without it.
3. **Configure forwarded headers before go-live.** Neither host calls `UseForwardedHeaders` today, and the ingress work is not done (see [Forwarded headers](#forwarded-headers)). Until it is done, audit rows hold the ingress address and all visitors share one open-rate bucket.
4. **Turn the feature on** with `OneTimeLinks:Enabled` on the API only, when you are ready. It is off by default.

**Rotating the key.** Change it on both hosts in the same deployment. If the two hosts disagree for a while, every open fails the proof check. Visitors then get the generic `404`, and the links are not consumed, so nothing is lost. Creating a link is unaffected.

## Forwarded headers

The Web reads the visitor's address from `Connection.RemoteIpAddress` when it captures the client. It does not read `X-Forwarded-For`. Behind an ingress without forwarded headers configured, that address is the ingress.

| Deployment | Visitor address in audit rows | Open-rate bucket |
|---|---|---|
| AppHost or local | Loopback | Loopback |
| Web reached directly | The browser | Per browser |
| Behind an ingress, no forwarded headers (today) | The ingress | **One bucket for every visitor** |
| Behind an ingress with `UseForwardedHeaders` and `KnownNetworks` | The browser | Per browser |

Configure `UseForwardedHeaders` on both the Web and the API, with `KnownNetworks` set to the ingress only. This is a deployment item in [PROGRESS.md](PROGRESS.md). Rows written before the address fix keep the Web host's address, and they are not corrected.

## Not verified

- Real Azure Table Storage. Every test runs against Azurite.
- Firefox and Safari. Web Crypto and the clipboard are not checked there.
- MAUI WebViews. The MAUI app is not built (`feature/maui-app`), and Web Crypto on a MAUI custom scheme is unverified.
- A visitor with no `User-Agent` header. No test opens a link that way end to end.
- An ingress that strips empty headers. If one does, the proof fails closed and the link is not consumed.
- Performance budgets (spec section 7.3) were not run. No benchmark result is claimed.
- Mutation proofs were not run.

## Owner decisions

Chosen as defaults without owner review. Each can be reversed.

- **O1. Creator notice** is in-app only, at the next load. A creator who never loads the vault or links pages is not told. There is no email or push.
- **O2. Off by default**, with only the AppHost run mode turning it on.
- **O3. The Web-to-API proof is a shared HMAC key.** It needs no tenant and works the same in Entra and local mode. A stolen key lets someone forge proofs with any address.
- **O4. Links stop opening once the secret leaves the vault.** Changed by the owner on 2026-11-07. See [the snapshot rule](#snapshot-rule-a-link-is-a-copy).
- **D6. Passphrase minimum of 8 characters, in the browser only.** A shorter passphrase fails in the UI, and the server cannot enforce it.
- **D12. AUD-4 CSV export** must neutralise formula prefixes, because the user agent is visitor-controlled text.
