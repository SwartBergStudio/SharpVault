# Sharing

Sharing lets a user give colleagues access to one of their secrets or folders. It is Stage 5 of the build. Requirement ids: SHR-1 to SHR-6 (see [REQUIREMENTS.md](REQUIREMENTS.md)). The rule behind every check is that only the owner's own partition is ever read for an item, and a recipient gets access only through a grant the owner or a manager created.

One-time share links are not built.

## Permissions

| Level | What the holder can do |
|---|---|
| View | See the item, read its fields, and reveal or copy the value (each reveal is audited). For a folder: see the folder, its subfolders and their secrets. |
| Edit | Everything View allows, and change a secret's fields and value. Edit does not allow moving, creating, deleting or restoring anything, and it does not open history. |
| Manage | Everything Edit allows, and share the item (or items inside a shared folder) with others at View or Edit. Manage can change or revoke only the grants that manager created. Only the owner can give Manage to someone else. |
| Owner | Everything, including delete (to the bin), restore, purge, move, create, history and the full grant list. Only the owner can delete. |

- An item the caller cannot reach at the required level returns 404, the same as an item that does not exist (decision D27). The UI hides the actions the caller cannot use.
- History (past versions, reveal of a past version, restore) is owner-only (decision D11). Recipients have no history routes.
- A recipient cannot move a secret out of the owner's folder, even with Manage (decision D10).

## Grants

- A grant is one recipient on one item. The owner's partition holds two rows for it (one keyed by item, one keyed by recipient), written in one transaction.
- Creating a grant requires Manage on the item. The request names one recipient. The UI adds people one at a time.
- The recipient must exist in the directory, must not be the owner or the caller, and must be active if the recipient already has a user row. A recipient who has never signed in can still receive a share (decision D16). Guests are refused in Entra mode unless the guest setting allows them.
- A second grant for the same recipient on the same item returns 409.
- An item holds at most 100 grants. The count check is not atomic: parallel creates can store a few more. Overshoot is bounded, because later creates are refused once the stored count reaches 100, and the list reads up to 200 grants so every grant stays visible. See PROGRESS.
- Changing a grant's level requires the owner or the grant's creator. The change uses the grant's `If-Match` value, so a stale page cannot silently raise a permission.
- Revoking requires the owner or the grant's creator. Revoke takes no `If-Match`. It retries with the server's current ETags, so a stale page cannot stop a revoke (decision D7). Revoking takes effect on the recipient's next request.
- A manager's grants stay in place when the manager is revoked. The list shows who granted each one ("Granted by").
- The grant stores a snapshot of the recipient's display name and email (decision D26). A renamed colleague shows the old name until the grant is recreated.
- A manager can grant View or Edit only. Only the owner can give Manage. This is a default chosen for owner review (it replaces the earlier wider rule, see [PROGRESS.md](PROGRESS.md)). The rule is `ShareGrantRules.CanGrant`, checked on create and on change. The list reports `CanGrantManage`, so the share dialog does not offer Manage to a manager. A manager's existing grants stay when the manager is revoked. To reverse the rule, set `SharingLimits.ManagerMaxGrant` to `SharePermissionDto.Manage`.

The effective level for a recipient is the highest of their direct grant and any grant on an ancestor folder, looked up at read time. A direct View never lowers a folder Edit (decision D9).

## Folder shares

- A folder grant gives access to that folder, every subfolder under it, and every secret in them. Secrets added later are included (SHR-3).
- Access is evaluated at read time by walking up from the secret or folder, up to the vault's depth limit of 10 folders. Nothing is copied onto the secrets, so a move or a new secret needs no grant write (decision D2).
- A recycled folder is not visible to recipients until it is restored.

## Shared with me

- The list is built from the caller's inbox: one row per grant received. The inbox lives in its own table (`ShareInbox`), so it grants nothing by itself.
- The UI groups the list by owner. Names are decrypted on the server with the owner's keys. A name that cannot be decrypted shows as damaged.
- An item the owner has moved to the bin, or deleted, does not appear. Restoring it brings it back, because the grants were kept.
- Secrets that a recipient can reach only through a shared folder appear when the folder is opened, not in the top-level list.
- An owner whose user row is missing shows as an unknown owner.

## Filing

- A recipient can file a shared item into one of their own active folders. Filing sets a folder id on the inbox row only. The owner's data is not touched.
- If the recipient later deletes that folder, the item shows as "Not filed". Filing is not cascaded and is not audited (decision D18).

## Revoke

- Revoking removes both grant rows and the inbox row, and writes `SecretShareRevoked` or `FolderShareRevoked`.
- The recipient loses access on their next request. A page they already have open can keep showing a value they revealed until that value's own timer hides it. The server does not push the change to the page.
- The revoke dialog warns that a copied value may need changing, because a revoked recipient may already have copied it (decision D21).

## Recycle, restore and purge

- **Recycling** a secret or a folder hides it from every recipient (shared lists drop it, shared reads return 404). The grants stay in place and are dormant. **Restore** brings access back (decision D20).
- **Folder delete, move to parent**: only the folder goes to the bin. Its secrets move to the parent folder, so a grant on the deleted folder no longer reaches them.
- **Folder delete, delete contents**: the folder's secrets and subfolders go to the bin with their grants dormant.
- **Purge** (from the bin, or the 30-day background job) erases the item and its grant rows, both halves. Purging a folder erases the grants on the folder and on the secrets it holds. Each erased grant writes a `ShareErased` audit row, and the inbox rows of the erased grants are removed.
- Purge erases first and audits second, on purpose (a restore race would otherwise log a purge that did not happen). If a purge is interrupted, some grant rows can survive. See PROGRESS.

Moving a secret between folders does not change its grants (FLD-2).

## Locks and transfers

- A locked user cannot call the API at all, so a locked recipient loses every share at their next request. A locked user cannot be granted a new share (the create is refused).
- A Dismissed lock revokes every grant the locked user received, one at a time, each with an audit row (see [TRANSFERS.md](TRANSFERS.md#locking-and-unlocking)). Other lock reasons keep received grants in place, and they stop working while the user is locked.
- A lock on the owner does not stop the owner's shares. Recipients keep access to items the locked owner shared (LCK-3).
- An owner is never told that a recipient is locked (decision D13).
- A transfer moves the source's outgoing grants to the target (see [TRANSFERS.md](TRANSFERS.md)).

## Finding people

- **Entra mode**: Microsoft Graph user search, `$search` on display name and mail, with the application permission `User.ReadBasic.All` through the API's managed identity. Search results are eventually consistent. Guests are filtered out unless the guest setting allows them (decision D14).
- **Local mode**: searches the `Users` table, scanning up to 1,000 rows (decision D15). A local account can only be granted after it has a row, which the seeded accounts always have.
- The search term must be 3 to 100 characters, using letters, marks, digits, spaces and `. ' @ _ -`. Up to 10 results are returned, and the server looks at 11 to know whether there are more. Results are not paged. The caller is excluded, and users known to be inactive (locked) are skipped.
- Searches are limited to 30 per minute per caller (`VaultRateLimits:DirectorySearchesPerMinute`). That is a throttle only. There is no per-recipient cap (see PROGRESS).
- Searches and filing are not audited (decision D18).

## Audit

`SecretShared`, `SecretShareChanged`, `SecretShareRevoked`, `FolderShared`, `FolderShareChanged`, `FolderShareRevoked` and `ShareErased`. Each row holds the owner, the item, the recipient and the actor. Reveals of shared values are audited by the reveal path with the owner's id.

## Limits

| Limit | Value | Where |
|---|---|---|
| Grants per item | 100 (overshoot possible under parallel writes) | `SharingLimits.MaxGrantsPerItem` |
| Grants read per item | 200 | `SharingLimits.GrantReadLimit` |
| Folder levels walked | 10 | vault depth limit |
| Share writes | 120 per minute per caller | `VaultRateLimits:ShareWritesPerMinute` |
| Directory searches | 30 per minute per caller | `VaultRateLimits:DirectorySearchesPerMinute` |
| Shared-list page | 50 by default, 100 at most | query `pageSize` |
| Dismissed lock revoke | stops after 20 empty inbox pages and the lock returns 500 for a retry | `SharingLimits.MaxEmptyInboxPagesPerCall` |
