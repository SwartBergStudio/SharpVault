# Vault

The vault stores each user's secrets, folders, history and recycle bin. It is on the `feature/local-debug-auth` branch (Stage 4). Sharing (Stage 5, see [SHARING.md](SHARING.md)) and admin transfers (Stage 6, see [TRANSFERS.md](TRANSFERS.md)) are built on the same branch. The audit log screens are not built yet.

Requirement ids: SEC-1 to SEC-5, FLD-1 to FLD-3 and VIS-1 (see [REQUIREMENTS.md](REQUIREMENTS.md)). VIS-2 holds in the API: admin rights give no access to other users' items, and every vault endpoint uses the caller's own partition. The owner-only access policy is the sharing hook (see [Access](#access-and-the-sharing-hook)).

## What a secret holds

| Field | Required | Limits |
|---|---|---|
| Type | yes | `Login`, `SecureNote`, `ApiKey`, `Card` or `Other` |
| Name | yes | 1 to 200 characters, trimmed, single line |
| Username | no | up to 256 characters, single line. A blank value is refused |
| URL | no | up to 2,048 characters, an absolute `http` or `https` address with no spaces |
| Value | no | 1 to 8,192 characters. Stored byte-exact, with no trimming |
| Notes | no | 1 to 8,192 characters. Stored byte-exact |
| Tags | no | up to 20 tags, each 1 to 50 characters, unique ignoring case |
| Folder | no | one folder, or unfiled (the root) |

Names, usernames, URLs and tags are trimmed before they are encrypted. Single-line fields refuse control characters and the bidirectional embedding and isolation characters. Names still accept other Unicode format characters (for example the left-to-right mark), because real right-to-left and emoji text uses them. URLs refuse every format character.

The API returns the value and the notes only through a reveal (see [Reveal and copy](#reveal-and-copy)). A secret's list and detail responses say whether a value or notes exist, never what they say.

If a stored item cannot be decrypted (for example after the local key changed, see [LOCAL-DEV.md](LOCAL-DEV.md#if-the-local-key-changes)), it is listed with `isDamaged` set and no name. Reveal and history restore of that item fail with a generic server error, and the response never gives the reason. The API refuses an edit of a damaged item (400, nothing written or audited), so the unreadable ciphertext survives until the key is restored. Renaming a folder whose name cannot be read is refused the same way. A damaged item can still be moved and deleted. The web app does not offer the edit form for a damaged item.

## Folders

- Folders nest up to 10 levels deep. A top-level folder is level 1.
- A vault holds at most 1,000 active folders.
- A folder name is 1 to 100 characters, single line.
- A folder cannot be moved into itself or into one of its subfolders.
- Folders are listed sorted by name. The UI builds the tree from the list.

### Deleting a folder

Deleting a folder needs a `mode`. There is no default, so contents are never deleted by accident.

- `MoveToParent`: secrets in the folder go to its parent (or the root), and subfolders move up to the parent. Then the folder goes to the recycle bin.
- `DeleteContents`: the folder, its subfolders and every secret in them go to the recycle bin together, as one batch.

A delete, restore or purge that touches more than 1,000 items is refused with 400 and changes nothing.

## Recycle bin and purge

- Deleting a secret or a folder moves it to the recycle bin. It is hidden from every normal list and from every normal endpoint.
- A restored secret returns to its original folder if that folder is still active, and to the root otherwise. A restored folder returns to its original parent if that parent is active and the depth still fits, and to the root otherwise. Restoring a folder brings back the subfolders and secrets that were deleted with it in the same delete.
- Items stay in the bin for 30 days. Each item shows `purgeAfter`, which is 30 days after it was deleted.
- Deleting an item again after a restore gives it a new 30-day period.
- A background job purges expired items. It starts one minute after the host starts and runs every six hours. Only one instance runs it at a time, using a lease in the `Maintenance` table. It walks the users page by page and resumes from a checkpoint after a crash.
- A purge deletes the item, its history and its wrapped keys. The value cannot be recovered afterwards. Each purge is audited with the system actor.
- A user can also purge an item by hand from the bin (E19 and E20).

A change to the same item by another request while a purge or restore runs returns 409. The purge job skips any item whose ETag changed, and running the job twice purges nothing new.

## History

- Each change of a secret's value keeps the previous value as a history entry. The history keeps the 20 newest previous values.
- Changing only the name, username, URL, tags, notes or folder does not add a history entry.
- Restoring a history entry makes the current value a new history entry, then copies the chosen entry back. The chosen entry stays in the history.
- History is visible only while the secret is active. A secret in the recycle bin has no visible history.
- Revealing or restoring a history entry is audited, as described under [Audit](#audit).

## Listing, search and order

- A list is paged. The default page size is 50 and the maximum is 200. The response carries a continuation token. Pass it back to get the next page. The token is opaque.
- A list can be filtered to one folder, or to the unfiled view. Both at once is refused.
- Search matches the name, username, URL and tags, ignoring case. The search text is 1 to 100 characters after trimming.
- Names are encrypted, so search runs on the server. It reads the vault in pages of 1,000 rows, decrypts each row and checks the match. One request examines at most 5,000 rows. When the limit is reached, the response carries a token and the UI keeps loading. Rows that cannot be decrypted are not matched.
- Lists come in storage order, which is creation order. The server cannot sort by an encrypted name, so the UI sorts the items it has loaded by name.
- The recycle bin lists folders first, then secrets, and it uses the same paging rules.

## Reveal and copy

- A value or the notes are hidden by default. Showing them is an explicit reveal, and the response holds the text for that one call only.
- Reveal and copy are the same request with a different intent (`View` or `Copy`). Each one is audited before the text is returned. If the audit cannot be written, the request fails and the text is not returned.
- The reveal endpoints allow 60 calls per minute for each user. All other vault endpoints allow 600 calls per minute for each user. Both limits count one-minute windows for the caller's object id. A caller over the limit gets 429.
- In the browser, a copied value is written to the clipboard and the clipboard is cleared 30 seconds later. The clear writes an empty string without checking what the clipboard holds by then, so anything copied after the secret is also cleared. A second copy restarts the timer. A revealed value is hidden again after 30 seconds. Both delays come from the UI settings (`VaultUiSettings`). They are not yet configurable by an admin (ADM-4).
- If the browser refuses the clear, for example because the window has no focus, the clear is retried when the window gains focus or becomes visible again. Until then the secret stays on the clipboard.
- The timer runs in the server circuit, and the clear reaches the browser through JS interop. If the circuit has ended, the clear is not sent.
- Behaviour in Firefox, Safari and unfocused windows has not been checked in a browser yet.

## Password generator

The generator is in `SharpVault.PasswordGeneration`. It runs in the app's UI code, not through the API, and uses `RandomNumberGenerator`.

- Passwords are 8 to 128 characters, with a choice of character sets. Ambiguous characters can be left out. Each chosen set can be required.
- Passphrases are 3 to 20 words from the EFF large word list, with a choice of separator, optional capitalisation, and an optional digit added to one word.
- The dialog shows an estimate of the entropy and a strength label.

The generated value goes into the form only when the user chooses to use it.

## Access and the sharing hook

- Every vault endpoint requires the `SharpVault.User` role and the lock check.
- An item is found only in the caller's own partition. An id that belongs to someone else returns 404, the same as an id that does not exist, and changes nothing (the IDOR tests cover every item endpoint).
- Owner routes search only the caller's partition. The check goes through `ISecretAccessPolicy`, which grants the owner full access. The shared routes (under `/api/shared`) resolve a grant through `ResolveSharedAsync`, so a recipient reaches only what a grant allows (see [SHARING.md](SHARING.md)).
- Each endpoint needs a permission level: View for reads and reveals, Edit for changes, Owner for delete. Only the owner can delete (SHR-2). History is owner-only.

## Audit

Every reveal, copy, create, change, move, delete, restore, purge and history action writes an audit entry (AUD-2). An entry holds the event type, the actor, the owner, the item id, the time and the IP address. It never holds a value. Writes after a change is committed use a separate path that does not depend on the request being still open, so a client that disconnects does not lose the entry. If an entry cannot be written, the failure is logged with the event type, the item id and the exception type. The log never holds a value. The log search screen (AUD-4, AUD-5) is not built yet.

## Errors

| Status | When |
|---|---|
| 400 | Validation failed, a continuation token is malformed, `If-Match` is malformed or missing, a body has an unknown member or a bad enum value, a folder limit or depth is exceeded, or a folder operation is too large |
| 404 | The item does not exist, is in the bin when it should be active, belongs to someone else, or its folder is missing |
| 409 | The ETag is stale, or another request changed the item first. Reload and try again |
| 413 | The body is larger than 1 MiB |
| 429 | A rate limit was hit. `Retry-After` is sent when known |
| 500 | An item cannot be decrypted. The title is "The item could not be read." with no reason |

## Endpoints

All routes are under `/api/vault`, need the `SharpVault.User` role, and are rate limited. Every path comes from an `ApiRoutes` constant.

| # | Method and route | Body or query | Success |
|---|---|---|---|
| E1 | `GET /secrets` | `folderId`, `unfiled`, `search`, `pageSize`, `continuationToken` | 200 `SecretPageDto` |
| E2 | `GET /secrets/{secretId}` | | 200 `SecretDto` |
| E3 | `POST /secrets` | `type`, `name`, optional `username`, `url`, `value`, `notes`, `tags`, `folderId` | 201 `SecretDto` |
| E4 | `PUT /secrets/{secretId}` | `If-Match`; `type`, `name`, optional `username`, `url`, `tags`, `replaceValue`, `value`, `replaceNotes`, `notes` | 200 `SecretDto` |
| E5 | `POST /secrets/{secretId}/move` | `If-Match`; `folderId` (optional, null for the root) | 200 `SecretDto` |
| E6 | `DELETE /secrets/{secretId}` | `If-Match` | 204 |
| E7 | `POST /secrets/{secretId}/reveal` | `field` (`Value` or `Notes`), `intent` (`View` or `Copy`) | 200 `RevealedValueDto` |
| E8 | `GET /secrets/{secretId}/history` | | 200 `SecretVersionListDto` |
| E9 | `POST /secrets/{secretId}/history/{versionId}/reveal` | `intent` | 200 `RevealedValueDto` |
| E10 | `POST /secrets/{secretId}/history/{versionId}/restore` | `If-Match` (the secret's concurrency token) | 200 `SecretDto` |
| E11 | `GET /folders` | | 200 `FolderListDto` |
| E12 | `POST /folders` | `name`, optional `parentId` | 201 `FolderDto` |
| E13 | `PUT /folders/{folderId}` | `name` | 200 `FolderDto` |
| E14 | `POST /folders/{folderId}/move` | optional `parentId` | 200 `FolderDto` |
| E15 | `DELETE /folders/{folderId}` | query `mode` (`MoveToParent` or `DeleteContents`, required) | 204 |
| E16 | `GET /recycle-bin` | `pageSize`, `continuationToken` | 200 `RecycleBinPageDto` |
| E17 | `POST /recycle-bin/secrets/{secretId}/restore` | | 200 `SecretDto` |
| E18 | `POST /recycle-bin/folders/{folderId}/restore` | | 200 `FolderDto` (the restored root of the subtree) |
| E19 | `DELETE /recycle-bin/secrets/{secretId}` | | 204 |
| E20 | `DELETE /recycle-bin/folders/{folderId}` | | 204 |

Input bodies reject unknown members and do not accept ids, owners, timestamps or revisions. Enum values travel by exact name.

## Pages in the web app

| Route | Page |
|---|---|
| `/vault` | All secrets, with the folder tree |
| `/vault?folderId=...` | One folder |
| `/vault/unfiled` | Secrets with no folder |
| `/vault/secrets/new` | New secret |
| `/vault/secrets/{id}` | Secret detail, with reveal and copy |
| `/vault/secrets/{id}/edit` | Edit a secret (refused for a damaged item) |
| `/vault/secrets/{id}/history` | History, with reveal, copy and restore |
| `/vault/recycle-bin` | Recycle bin, with restore and purge |

Vault pages run in interactive server mode. They are reached through the side navigation.

## Not built yet

- Sharing, transfers and one-time share links are built (see [SHARING.md](SHARING.md), [TRANSFERS.md](TRANSFERS.md) and [ONE-TIME-LINKS.md](ONE-TIME-LINKS.md)).
- Audit search and history screens (AUD-4, AUD-5).
- A configurable clipboard timeout (ADM-4).
- Real browser checks of the clipboard, the dialogs and the idle lock (see [PROGRESS.md](PROGRESS.md)).
