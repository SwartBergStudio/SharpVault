// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Users;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Users;

public sealed class LockUserDialogTests : ShellTestContext
{
    private const string LongEnoughReason = "Left the company, access removed";

    private const string ShortReason = "too short";

    private LockUserRequestDto? _confirmed;

    private IRenderedComponent<MudDialogProvider> _provider = default!;

    private void RenderOpen(bool unlock = false)
    {
        _provider = RenderDialogProviders();
        Render<LockUserDialog>(parameters => parameters
            .Add(dialog => dialog.Open, true)
            .Add(dialog => dialog.Unlock, unlock)
            .Add(dialog => dialog.OnConfirm, EventCallback.Factory.Create<LockUserRequestDto>(this, request => _confirmed = request)));
    }

    [Fact]
    public async Task Lock_ReasonTooShort_ConfirmIsDisabledAndNothingIsSent()
    {
        RenderOpen();

        var confirm = _provider.Find("#user-lock-dialog-confirm");
        Assert.True(confirm.HasAttribute("disabled"));
        await confirm.ClickAsync(new MouseEventArgs());

        Assert.Null(_confirmed);
    }

    [Fact]
    public async Task Lock_ValidReason_SendsTheTrimmedReason()
    {
        RenderOpen();
        _provider.Find("#user-lock-dialog-reason").Input($"  {LongEnoughReason}  ");

        await _provider.Find("#user-lock-dialog-confirm").ClickAsync(new MouseEventArgs());

        Assert.NotNull(_confirmed);
        Assert.Equal(LongEnoughReason, _confirmed.Reason);
    }

    [Fact]
    public void Unlock_UsesTheUnlockIdsAndNoLockIds()
    {
        RenderOpen(unlock: true);

        Assert.NotNull(_provider.Find("#user-unlock-dialog-reason"));
        Assert.Empty(_provider.FindAll("#user-lock-dialog-reason"));
    }

    [Fact]
    public void ShortReason_KeepsConfirmDisabled()
    {
        RenderOpen();
        _provider.Find("#user-lock-dialog-reason").Input(ShortReason);

        Assert.True(_provider.Find("#user-lock-dialog-confirm").HasAttribute("disabled"));
    }
}
