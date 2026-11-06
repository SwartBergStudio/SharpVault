// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Pages;

public sealed class TransfersPage(IPage page, string webUrl) : PageObject(page)
{
    private const string TargetSelectId = "transfer-target";
    private const string CategorySelectId = "transfer-category";
    private const string ReasonId = "transfer-reason";
    private const string SubmitId = "transfer-submit";
    private const string StateId = "transfer-state";
    private const string ApproveId = "transfer-approve";
    private const string ApproveConfirmId = "transfer-approve-dialog-confirm";
    private const string ApproveDialogId = "transfer-approve-dialog-dialog";
    private const string ListTableId = "transfers-table";
    private const string LeftCompanyOption = "Left the company";

    public async Task RequestAsync(string targetName, string reason)
    {
        await ChooseOptionAsync(TargetSelectId, targetName);
        await ChooseOptionAsync(CategorySelectId, LeftCompanyOption);
        await FillAsync(ReasonId, reason);
        await ClickAsync(SubmitId);
    }

    public async Task OpenListAsync()
    {
        await GoToAsync(webUrl + Routes.AdminTransfers);
        await ShowsAsync(ListTableId);
    }

    public async Task ApproveAsync()
    {
        try
        {
            await Element(ApproveId).ClickAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            var state = await Element("app-shell").InnerTextAsync(new() { Timeout = 5000 });
            throw new InvalidOperationException($"Approve was not available at {Page.Url}; the page reads \"{state}\": {exception.Message}", exception);
        }
        await ClickAsync(ApproveConfirmId);
        await HidesAsync(ApproveDialogId);
    }

    public Task AssertStateAsync(string state) => ContainsTextAsync(StateId, state);
}
