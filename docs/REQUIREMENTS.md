# SharpVault product requirements

SharpVault is a full-featured password and secrets manager that a company runs for its own employees, comparable to Keeper or 1Password Business. Each deployment is connected to one Microsoft Entra ID tenant.

The `planner` agent plans every feature against this document. Requirement ids (for example `SHR-3`) are referenced in plans, tests and pull requests.

## 1. Tenancy and who can use SharpVault

- **TEN-1** A deployment is connected to exactly one Entra tenant (configured tenant id).
- **TEN-2** Only members of that tenant can sign in. Tokens from any other tenant are rejected (`tid` claim check).
- **TEN-3** Entra B2B guest accounts are rejected by default. An admin setting can allow guests.
- **TEN-4** A user must have the `SharpVault.User` or `SharpVault.Admin` app role to use the app.
- **TEN-5** On sign-in, the user is created or updated in the `Users` table (oid, display name, email, status).

## 2. What a user can see

- **VIS-1** A user sees only their own vault: secrets and folders they own, plus secrets and folders shared with them. Nothing else.
- **VIS-2** Admins are users too. Being an admin gives **no** access to other users' secrets.
- **VIS-3** Admins can see, per user: status, number of secrets, number of shares and last sign-in. Admins never see secret names, values or folder names of other users.
- **VIS-4** The only way anyone gets access to another user's secrets is a share from the owner (section 4) or an approved transfer (section 7).

## 3. Secrets and folders

- **SEC-1** A secret has: name, type (login, secure note, API key, card, other), username, value, URL, notes, tags, folder, created and modified dates.
- **SEC-2** Secret values, notes and usernames are encrypted (envelope encryption, see `CLAUDE.md`). Names, tags and folder names are encrypted too, because users' own data is private even from admins.
- **SEC-3** Values are hidden by default and shown only on an explicit reveal or copy. Every reveal and copy is audited.
- **SEC-4** Password generator: length, character sets, passphrases, using `RandomNumberGenerator`.
- **SEC-5** Secret history: the previous versions of a value are kept and can be restored by the owner.
- **FLD-1** Users organise secrets in folders, with nested sub-folders.
- **FLD-2** A secret belongs to at most one folder of its owner. Moving a secret between folders does not change its shares.
- **FLD-3** Deleting a folder asks whether to move its secrets to the parent folder or delete them. Deleted items go to a recycle bin for 30 days.

## 4. Sharing with employees

- **SHR-1** The owner of a secret or folder can share it with other users in the same tenant only (TEN-2). Recipients are picked by searching the tenant directory (Microsoft Graph, `User.ReadBasic.All`), and stored by oid.
- **SHR-2** Permissions: **View** (see and copy), **Edit** (also change the secret), **Manage** (also share with others). Only the owner can delete.
- **SHR-3** Sharing a folder shares every secret in it, including secrets added later.
- **SHR-4** The owner can change or revoke a share at any time. Revoking takes effect immediately.
- **SHR-5** Shared items appear in the recipient's "Shared with me" view, grouped by owner. Recipients can file shared items into their own folders without affecting the owner.
- **SHR-6** Sharing with a locked user is not allowed. Shares to a user who becomes locked stop working while they are locked.

## 5. One-time share links

- **OTL-1** The owner (or a user with Manage) can create a one-time link for a single secret. **Anyone with the link can open it**, including people outside the company.
- **OTL-2** A link opens **once**, then it is destroyed. It also expires after a time limit chosen by the creator (default 24 hours, maximum set by admins, up to 7 days).
- **OTL-3** The link contains a random key in the URL fragment (`#...`). The server stores only the secret encrypted with that key, so the server and its admins cannot read link contents, and the fragment never reaches the server logs. Decryption happens in the browser.
- **OTL-4** Optional extra passphrase that the recipient must enter.
- **OTL-5** The link shows a snapshot of the secret at the moment the link was created. Later changes to the secret are not visible through the link.
- **OTL-6** The creator can see their active links and revoke them. Revoked and expired links are deleted.
- **OTL-7** Creating, opening (with IP address and user agent) and revoking links is audited. The creator is notified when a link is opened.
- **OTL-8** Admin settings: enable or disable one-time links for the company, and set the maximum lifetime.
- **OTL-9** The open endpoint is rate limited and returns the same response for unknown, used and expired links.

## 6. Locking users out

- **LCK-1** An admin can lock a user. A locked user cannot use SharpVault at all, even with a valid Entra token. Lock status is checked on every API request, not only at sign-in.
- **LCK-2** Locking requires a reason category and a written reason (see 7.2).
- **LCK-3** Locking immediately: ends the user's sessions, revokes their active one-time links, and suspends shares they receive (SHR-6). Shares they own keep working for recipients, so colleagues are not cut off from shared passwords.
- **LCK-4** An admin can unlock a user, with a written reason. Lock and unlock are audited.
- **LCK-5** An admin cannot lock themselves.
- **LCK-6** A lock can be scheduled for a future date and time (for example an employee's last day).

## 7. Offboarding and transfers

### 7.1 Transfer flow (four-eyes approval)

- **TRF-1** An admin creates a **transfer request**: source user, target user, scope (all secrets, or selected top-level folders), reason category and written reason.
- **TRF-2** The source user must be locked (or a lock is scheduled at the same time) before a transfer can be requested.
- **TRF-3** A transfer needs approval by a **second, different admin**. The requesting admin cannot approve their own request. Neither admin can be the source or target user.
- **TRF-4** The approving admin sees the request details and reason, then approves or rejects. Rejecting requires a reason.
- **TRF-5** Requests expire if not decided within 7 days (configurable).
- **TRF-6** **The target user sees nothing until the transfer is approved and completed.** Before approval, no data is copied or re-encrypted for the target.
- **TRF-7** On approval, a resumable transfer job re-encrypts each secret for the new owner, moves ownership, and places everything in a folder named "Transferred from {source name} ({date})" in the target's vault, keeping the original folder structure inside it.
- **TRF-8** Shares the source user had given to others are kept and now belong to the target user, who can review and revoke them.
- **TRF-9** The source user is notified neither before nor after (they are locked). The target user and both admins are notified when the transfer completes.
- **TRF-10** If only one admin exists, transfers cannot be approved. The admin screen explains that a second admin must be assigned in Entra first.

### 7.2 Reason categories and their defaults

| Category | Default lock | Default scope | Notes |
|---|---|---|---|
| **Left the company** | Scheduled for the last working day | All secrets | Usually transferred to the manager or replacement |
| **Dismissed** | Immediately | All secrets | One-time links and incoming shares revoked at once |
| **Deceased** | Immediately | All secrets | Wording in the UI is respectful; notifications avoid automated messages to the user |
| **Role change** | No lock | Selected folders | Hand over work secrets, the user keeps the rest |
| **Other** | Admin chooses | Admin chooses | Written reason is mandatory |

- **RSN-1** Every lock, unlock, transfer request, approval and rejection records: category, written reason (minimum 20 characters), admin, date and time.

## 8. Audit log

- **AUD-1** The audit log is append-only. Nobody, including admins, can edit or delete entries through the app.
- **AUD-2** Audited events: sign-in, reveal, copy, create, change, delete, share, unshare, one-time link create/open/revoke, lock, unlock, transfer request/approve/reject/complete, admin setting changes.
- **AUD-3** Entries hold ids, actor, target, time, IP address and reason where relevant. Never secret values.
- **AUD-4** Admins can search and filter the audit log by user, event type and date, and export it as CSV.
- **AUD-5** Users can see the audit history of their own secrets.

## 9. Admin settings

- **ADM-1** Allow or block Entra guest accounts (TEN-3).
- **ADM-2** Enable or disable one-time links, and set their maximum lifetime (OTL-8).
- **ADM-3** Transfer request expiry (TRF-5).
- **ADM-4** Idle auto-lock timeout and clipboard clear timeout.
- **ADM-5** Every settings change is audited.

## 10. Later phases (Keeper-level features)

Not in the first release, but the design must not block them:

- Import from Keeper, Bitwarden, 1Password and CSV; encrypted export
- TOTP codes stored with logins, file attachments
- Password health report (weak, reused, old passwords) and breach checks
- Browser extension and mobile autofill
- Teams (share with an Entra group instead of individuals)
- Emergency access set up by the user in advance
- Multi-tenant hosted edition

## Open questions

- Should a "Dismissed" transfer also revoke the shares the user owns, or keep them working (current default: keep, TRF-8)?
- Should unlocking a user also need a second admin's approval (current default: no)?
