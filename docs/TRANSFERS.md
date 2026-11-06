# Admin transfers

An admin transfer moves one user's vault to another user, for example when someone leaves, is dismissed, or changes role. It is Stage 6 of the build. Requirement ids: TRF-1 to TRF-10, RSN-1 and LCK-1 to LCK-5 (see [REQUIREMENTS.md](REQUIREMENTS.md)). Where the requirements disagree with the code, the conflict is listed in [PROGRESS.md](PROGRESS.md).

## What a transfer does

A request names a source user, a target user, a scope, a reason category and a written reason. When a second admin approves it, a background job:

1. Copies each in-scope item into a hidden staging area in the target's partition (rows with the `x_` prefix). Values, notes, metadata and history are re-encrypted under the target's keys. The per-secret data key is moved unchanged.
2. Removes the items from the source vault.
3. Publishes the staged items into the target's vault, folders first, then secrets.
4. Moves the share grants the source gave on those items to the target, and revokes the source's copies.
5. Writes the notices and marks the transfer Completed.

Scope:

- **All items**: every active and recycled secret (with its history) and every folder in the source vault.
- **Selected folders**: 1 to 100 top-level active folders, with everything under them. The folders must still be active and top-level at approval.

Placement:

- Everything lands in a folder named `Transferred from {source name} ({yyyy-MM-dd})`, dated with the decision date, in the target's vault. Structure below the selected folders is kept. Items with no folder go directly into that folder.
- Ids are kept. If the target already has an active folder with the same id, the items merge into it.
- Recycled items keep their deletion dates, so the 30-day bin clock does not restart.
- A folder whose name cannot be decrypted is written under the name `Unreadable folder`.
- A secret that cannot be decrypted, or whose data cannot fit in one storage transaction, is skipped. It stays with the source, the job writes a `TransferItemSkipped` audit row, and the transfer still completes. The skipped count is shown on the transfer.

## Roles

| Role | Who can hold it | Rules |
|---|---|---|
| Requester | An active admin who is not the source or the target | Creates the request. Cannot approve it. Can withdraw it (the UI says "Withdraw"; the API uses the reject endpoint). |
| Second admin | An active admin who is not the requester, the source or the target | Approves or rejects. The source and the target get 404 for the transfer, so they cannot act on it even if they are admins. |
| Source | The user whose vault moves | Must be locked for Left the company, Dismissed and Deceased, when the request is made and at each gate check until release starts (see Gate checks). Not required for Role change or Other. Gets no notice. |
| Target | The user who receives the vault | Must be active when the request is made, when it is approved, and at each gate check until release starts. Gets a notice when the transfer completes. |

Other rules:

- The requester and the approver must still be active admins at each gate check. If either is not, the transfer is blocked.
- Gate checks. The requester, approver, source lock, target status and folder limit are checked when an approved job starts, when a blocked job is resumed, and at the end of staging, just before release starts. Release, publish and reassignment do not check them, so a lock or status change after release starts does not stop the job.
- One open transfer per source user.
- No chains. The target cannot be the source of an open transfer, and the source cannot be the target of one. Several sources may send to one target.
- The request needs another active admin (not the requester, the source or the target). The second-admin check reads up to 100 admin rows. Without one, the API refuses the request and the UI disables the submit button.

What admins who are not parties can see:

- The list of transfers: names, category, dates and state. No reason.
- Any one transfer, including the written reason and the decision.
- The preview for a source user: counts, whether a second admin exists, and the top-level folders as opaque references (the last 8 characters of the folder id, with a date and an item count). The preview returns no folder or item names.

## States

| State | Meaning | Moves to |
|---|---|---|
| PendingApproval | Waiting for the second admin | Approved, Rejected, or Expired after the request lifetime (7 days by default) |
| Approved | Approved. Checks run | Staging, or Reassigning if the source has no vault, or Blocked |
| Staging | Copies are being written to the target's hidden area | Releasing, or Blocked |
| Releasing | Source items are being removed. This is the point of no return | Publishing |
| Publishing | Staged items are being published into the target's vault | Reassigning |
| Reassigning | Share grants are moving to the target | Completed |
| Completed | Done. Notices written | none |
| Blocked | A check failed. Retried every 30 seconds until it passes or the transfer is aborted | Approved or Staging (depending on whether the job had started), or Aborted |
| Rejected | Rejected, or withdrawn by the requester | none |
| Expired | Not decided within the request lifetime | none |
| Aborted | Stopped before release. Staged copies are removed | none |

Block reasons: requester no longer an admin, approver no longer an admin, source missing, source not locked, target missing, target locked, target folder limit reached, source vault unreadable, target vault unreadable.

Approval checks, in order: the transfer is still pending and the `If-Match` value matches (409 otherwise); the request has not expired (400); the requester is still an active admin; the source is still locked where the category needs it; the target is still active; the folder selection is still valid. The approval itself is audited when the job starts, not when the approve call returns.

Abort is allowed in Approved, Staging and Blocked. It is refused with 409 from Releasing onwards.

## What each party sees

**Source.** For Dismissed, Left the company and Deceased the source is locked, so every request from them is refused. They see nothing, including their vault, and they get no notice. For Role change and Other the source stays active. Their items disappear from the vault during releasing and publishing, they get no notice, and the shares they gave on the moved items become the target's.

**Target.** Nothing is visible until the transfer completes. Staged copies sit outside every vault read range, and no notice or folder is written yet. During publishing the target can see items appear (see the next section). At completion the target gets a `Received` notice, shown as a banner on the vault page, which links to the new folder. The shares the source had given now belong to the target and appear under Shares you gave.

**Requester and approver.** See the transfer and its state, and receive a completion notice.

**Other admins.** See the list and the detail, as described above.

## Publish window (decision D3)

TRF-6 says the target sees nothing until the transfer is approved and completed. The code keeps that promise up to the start of publishing. Publishing writes items into the target's vault folders first, then secrets in pages of 50. Each secret with its versions is one atomic group, and several groups share a storage transaction when they fit. For a large transfer this takes seconds to minutes, and during that time the target can see some items and folders before the transfer completes. Between releasing and publishing, the items exist only in the target's hidden staging rows. A share recipient of a moved item gets 404 from the moment the source copy is removed in releasing until reassignment moves that item's grant to the target. The grant still names the source as its owner until then, so this window lasts past the end of publishing.

A strict flip would need a read filter on every vault read path, because Table Storage cannot commit more than 100 actions atomically. The owner has to decide whether this wording of TRF-6 is acceptable.

## Locking and unlocking

- An admin locks a user with a reason category and a written reason. An admin cannot lock themselves.
- The lock applies to the user's next API call, once the lock status cache has expired. The cache holds the status for 5 seconds by default (`UserAccess:CacheTtl`, up to 30), and each API instance has its own cache.
- An open browser session ends at its next check, within about a minute. The Web asks the API once a minute, and a locked user is refused there. The page then shows "Sign in required", and a revealed value leaves the page with it.
- A Dismissed lock also revokes every share the locked user received. Each revoke is audited. Shares the locked user owns keep working for their recipients.
- If the revoke cannot finish, the request returns 500 and the lock stays applied. Repeating the Dismissed lock finishes the revoke.
- Locking an already locked user returns 400 for every category except Dismissed. A repeat Dismissed lock writes a second `UserLocked` audit row and returns 200. This is the current behaviour and is an owner review item.
- An admin unlocks with a reason. Unlocking an active user returns 400 and writes no audit row.
- Scheduled locks (LCK-6) are not built. "Left the company" locks the user immediately.
- Each lock and unlock writes an audit row with the category, the reason, the admin, the target and the IP address.

## Reasons

- Required for: creating a transfer, rejecting or withdrawing one, aborting one, locking and unlocking.
- Trimmed length: 20 to 1,000 characters.
- Refused: control characters other than carriage return, line feed and tab, and unpaired surrogates.
- Stored trimmed. Kept in the audit row and in the transfer detail, which only non-party admins can read.
- Never in notices, never in the list summaries, and not logged by the transfer code. The job logs carry ids and exception type names only.

## Notices

| Notice | Who gets it | When |
|---|---|---|
| Received | Target | Completion, only if items moved. Carries the new folder id |
| RequestCompleted | Requester | Completion |
| ApprovalCompleted | Approver | Completion |

- Notices are in-app only. No email is sent (decision D12).
- The source never gets a notice.
- Notices carry the source display name, counts and dates. They never carry a reason or category.
- A user sees up to 20 notices and can dismiss one. Dismissing removes that user's copy only.

## Shares you gave

After completion the target can open Shares you gave (`/transfers/shares`). It lists the caller's own share grants, with item names decrypted under the caller's key, and each row has a Revoke action with a confirmation. The page is the same for any user who has given shares, not only for transfer targets. See [SHARING.md](SHARING.md).

## Crash behaviour and retries

- The job state is saved after each step with the job's ETag. A runner holding an old copy of the job fails its save and stops before it removes any staged copy.
- Staging is fenced. Each stage pass takes a fence row, `x_{transferId:N}-fence`, in the target's partition, and then saves the job. If that save shows a newer runner, the pass releases the fence it took and stops. Every stage transaction starts with an ETag-checked update of the fence. A runner whose fence was taken over therefore writes nothing: its transaction fails and the pass stops. Completion, abort and cleanup take the fence and delete it. The fence holds no content and is outside every vault read range. See [STORAGE.md](STORAGE.md#vaults-table).
- Staged rows are the resume marker. Publishing replaces a staged row with the real row in the same transaction, so a staged row exists until its real row does.
- One runner holds the `transfer-jobs` lease at a time. The lease lasts 2 minutes and is renewed about every 40 seconds. If the API process stops, the lease expires and the next run resumes from the saved state.
- A background poller runs every 15 seconds. Approve and abort wake it at once, but only in the process that handled the request. Other instances pick up the work at their next poll.
- A failed step increases the attempt count and waits before the next try: 30 seconds, then doubling up to a cap of 15 minutes. A successful step resets the count.
- There is no attempt limit, on purpose. A job that keeps failing retries every 15 minutes until it is fixed or aborted, and the transfer page shows the last failure kind and the time the failures started. Publishing cannot be aborted, so a job that fails there can only be fixed. The owner reviews this choice (see PROGRESS).
- A publish collision (a secret id that already exists at the target) is not a failed step. The staged copy is published under a new id derived from the transfer id and the source id (`VaultIds.TransferredSecretIdFor`). The copy is re-bound to that id (metadata and content, since both are bound to the secret id) and placed in the transfer's root folder. The target's own copy is left unchanged. The job counts these in `RenamedItemCount`, writes one `SecretTransferredUnderNewId` audit row per renamed secret, naming both ids, and shows the count on the transfer. Each such secret also has a `SecretTransferred` row, written before the publish, that names the source id. That id is true, because the secret was at the source, and the row is linked to the transfer by `TransferId` (accepted, see PROGRESS). A share grant on a secret moves to the new id, and its `ShareReassigned` row names that id. A grant on a secret that is still at the source (outside the selection) stays with the source, even when the target holds a copy with the same id.
- Version envelopes are bound to the version id and the owner, not the secret id. A renamed secret's versions are re-encrypted with a fresh nonce under the same context, so they are not left as the same ciphertext.
- A failed step is logged as a warning until it has failed five times in a row, then as an error. The transfer shows the last failure kind and the time it started failing.
- If the source's or the target's keys cannot read the vault, the preflight step blocks and moves nothing until the keys are repaired. In staging and releasing the same condition fails the step instead, and the step retries with backoff. Items staged or released before the failure stay where they are, so some items may already have moved. Publishing reads the target keys only to rename a colliding secret. A blocked job re-checks the keys before it writes `TransferResumed`, so it writes one `TransferBlocked` row rather than one pair per retry.
- The release step only removes a folder when its staged copy is current. A folder renamed or created after staging stays at the source until the next pass stages it. A secret that leaves the selection after staging stays at the source and is unstaged on the next pass.
- After the point of no return, a source row that is already missing is treated as released (decision D16).

## Limits

| Limit | Value | Setting |
|---|---|---|
| Request lifetime | 7 days | `Transfers:RequestLifetime` (1 minute to 90 days) |
| Poll interval | 15 seconds | `Transfers:PollInterval` (1 second to 5 minutes) |
| Lease duration | 2 minutes | `Transfers:LeaseDuration` (30 seconds to 30 minutes) |
| Retry cap | 15 minutes | `Transfers:MaxBackoff` (1 minute to 1 hour) |
| Background runner | on | `Transfers:Enabled`. `false` stops the runner; requests and decisions still work |
| Reason length | 20 to 1,000 characters | fixed |
| Selected folders | 1 to 100 | fixed |
| Active folders in the target | 1,000, checked at request, approval and during the job | fixed |
| Folder depth | not checked for moved folders (D17) | fixed |
| Open transfers | one per source, no chains | fixed |
| Admin rows read for the second-admin check | 100 | fixed |
| Preview counts | 50 inventory pages, then the counts are marked capped | fixed |
| Steps per job per run | 12 | fixed |
| Actions per storage transaction | 100. An item too big for one transaction is skipped | fixed |
| Publish page | 50 secrets | fixed |
| Admin calls | 120 per minute per caller | `VaultRateLimits:AdminActionsPerMinute` |
| Transfer list page | 25 by default, 100 at most | query `pageSize` |
| Notices listed | 20 | fixed |

Storage: the transfer tests run against Azurite. Real Table Storage batch behaviour is not verified.

## Known gaps and residual risks

- Publish window (D3), and the requirement conflicts TRF-2 against table 7.2 (D6) and TRF-1 against VIS-3 (D13). These are owner decisions, listed in PROGRESS.
- Demotion (D11). An admin's role is read from the sign-in token, using the token's `iat` to pick the newest observation. An admin who was demoted and presents no newer token can keep acting until the token expires (about an hour in Entra, five minutes locally). This is not verified against a real tenant.
- No attempt limit on failing jobs (see above).
- The wake-up signal is per process.
- Two colluding admins are outside the four-eyes model. The audit log shows both.
- A deleted target user's bin would not be purged, because no user-delete feature exists.

## Audit

Each step writes an audit row with the event type, the actor (System for job steps), the source, the target, the transfer id and, where relevant, the category and reason. Events: TransferRequested, TransferApproved, TransferApprovalDenied, TransferRejected, TransferAborted, TransferExpired, TransferStarted, TransferBlocked, TransferResumed, TransferReleaseStarted, SecretTransferred, SecretTransferredUnderNewId, FolderTransferred, TransferItemSkipped, ShareReassigned, ShareRevokedByTransfer, TransferCompleted, UserLocked and UserUnlocked. Lock and unlock reasons are stored trimmed, as the validator measures them. A denied approval (the requester trying to approve) writes TransferApprovalDenied.

## Defects found in review and fixed

- **Owner move in the release window duplicated a secret.** An owner moving a secret out of the selection after staging left a stale staged reference. The transfer completed with the secret at both users. Found by reading the release finish check. Fixed by refusing to finish while a staged secret or a staged folder that left the scope is still at the source. Verified by `SecretMovedOutOfTheSelectionInTheReleaseWindow_StaysWithTheOwnerAndIsNotPublished`, which fails when the guard is removed. Cost: one extra pass in that case.
- **Renamed or new folders lost in the release window.** A folder removed without a current staged copy lost its new name, or was lost if it was created after staging. Fixed by removing a folder only when its staged copy has the same source ETag. Verified by `FolderRenamedInTheReleaseWindow_TargetGetsTheNewName` (fails without the check). The new-folder test (`FolderCreatedInsideTheSelectionInTheReleaseWindow_IsPublishedNotLost`) passes with and without the check: folder creation changes the key ring ETag, which makes the release pass stop before it reaches the folder step. The check is kept as a guard; the path is not reproduced.
- **Publish collision had no way out.** A secret already at the target stopped the job from completing, and it retried forever. Fixed by publishing under a derived id (see Crash behaviour). Verified by `SecretAlreadyAtTarget_JobCompletes_BothCopiesReadable_TargetOwnCopyUnchanged_AndSharesMove` (fails without the rename path) and by a share check that fails when the grant does not follow the new id.
- **Lock and unlock with a padded reason.** The validator measured the trimmed reason, but the audit row stored the raw one. A reason of 40,000 spaces made the audit insert fail after the status had changed, so the user was locked with no audit row. Fixed by storing the trimmed reason. Verified by `Lock_ValidReasonPaddedWithSpaces_Returns200AndOneAuditRow` and `Unlock_ValidReasonPaddedWithSpaces_Returns200AndOneAuditRow` (both return 500 without the trim). The other commands that carry a reason (create, reject, abort) already trim.
- **Blocked job wrote audit rows every 30 seconds.** A job blocked on an unreadable key was resumed, then blocked again on the next step. That wrote `TransferResumed` and `TransferBlocked` about 5,760 times a day. Fixed by re-checking the keys before resuming. Verified by `Job_TargetRingStillUnreadable_BlockedRetriesWriteNoResumedOrBlockedRows` (fails without the check).
- **A stale runner could add or overwrite a staged copy.** A stage upsert had no ETag. A runner that stalled after saving its job, while another runner had moved on, could land its write later. That could add an extra staged copy that publishes a second time under a new id, or overwrite a newer staged copy and lose an owner's edit. Found by reading the staging store and the phase that calls it. Fixed by the fence (see Crash behaviour). Verified by the `StaleStageWrite_*` tests in `TransferStaleStageTests` (the three cases named in the plan: after another runner completed, while another runner publishes, and after an owner edit in between). The red run against the old code is not recorded in `red-green.md`, so the old-code failure is not evidenced yet.

## Trying a transfer locally

See [LOCAL-DEV.md](LOCAL-DEV.md#transfers-with-the-four-local-accounts).
