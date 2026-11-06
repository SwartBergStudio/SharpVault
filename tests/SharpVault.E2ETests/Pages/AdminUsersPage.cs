// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Pages;

public sealed class AdminUsersPage(IPage page, string webUrl) : PageObject(page)
{
    private const string TableId = "admin-users-table";
    private const string CountsIdPrefix = "user-counts-";
    private const string CountsSecretsId = "user-counts-secrets";
    private const string CountsSharesId = "user-counts-shares";
    private const string CountsCloseId = "user-counts-close";
    private const string LockIdPrefix = "user-lock-";
    private const string TransferIdPrefix = "user-transfer-";
    private const string LockDialogPrefix = "user-lock-dialog";
    private const string LockCategoryId = LockDialogPrefix + "-category";
    private const string LockReasonId = LockDialogPrefix + "-reason";
    private const string LockConfirmId = LockDialogPrefix + "-confirm";
    private const string LockCategoryOption = "Left the company";

    public async Task OpenAsync()
    {
        await GoToAsync(webUrl + Routes.AdminUsers);
        await ShowsAsync(TableId);
    }

    public async Task AssertCountsAsync(Guid userId, string secrets, string shares)
    {
        await ClickAsync(CountsIdPrefix + userId.ToString("D"));
        await HasExactTextAsync(CountsSecretsId, secrets);
        await HasExactTextAsync(CountsSharesId, shares);
        await ClickAsync(CountsCloseId);
    }

    public async Task LockAsync(Guid userId, string reason)
    {
        await ClickAsync(LockIdPrefix + userId.ToString("D"));
        await ChooseOptionAsync(LockCategoryId, LockCategoryOption);
        await FillAsync(LockReasonId, reason);
        await ClickAsync(LockConfirmId);
        await HidesAsync(LockConfirmId);
    }

    public async Task StartTransferAsync(Guid userId)
    {
        await ClickAsync(TransferIdPrefix + userId.ToString("D"));
    }
}
