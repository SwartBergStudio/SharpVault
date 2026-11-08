// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using NSubstitute;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.OneTimeLinks;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinkEditWarningTests : ShellTestContext
{
    private const string Warning = "#otl-edit-warning";
    private const string NextPage = "next-page";

    private readonly Guid _secretId = Guid.NewGuid();

    [Fact]
    public void Load_NoLinkToThisItem_ShowsNothing()
    {
        ReturnsPage(null, new OneTimeLinkPageDto { Items = [Link(Guid.NewGuid())] });

        var cut = RenderWarning();

        cut.WaitForAssertion(() => _ = OneTimeLinksClient.Received(1).ListAsync(Arg.Any<int>(), null, Arg.Any<CancellationToken>()));
        Assert.Empty(cut.FindAll(Warning));
    }

    [Fact]
    public void Load_LinksAcrossPages_CountsOnlyThisItem()
    {
        ReturnsPage(null, new OneTimeLinkPageDto { Items = [Link(_secretId), Link(Guid.NewGuid())], ContinuationToken = NextPage });
        ReturnsPage(NextPage, new OneTimeLinkPageDto { Items = [Link(_secretId)] });

        var cut = RenderWarning();

        var warning = cut.WaitForElement(Warning);
        Assert.Contains(OneTimeLinkEditWarning.WarningText(2), warning.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ListFails_ShowsNoWarningAndNoError()
    {
        OneTimeLinksClient.ListAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkPageDto>.Failed()));

        var cut = RenderWarning();

        cut.WaitForAssertion(() => _ = OneTimeLinksClient.Received(1).ListAsync(Arg.Any<int>(), null, Arg.Any<CancellationToken>()));
        Assert.Empty(cut.FindAll(Warning));
        Assert.Empty(cut.FindAll("#otl-edit-links-error"));
    }

    [Fact]
    public void Revoke_Confirmed_RevokesEachLinkToThisItemAndClearsTheWarning()
    {
        var first = Link(_secretId);
        var second = Link(_secretId);
        var other = Link(Guid.NewGuid());
        OneTimeLinksClient.ListAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(ApiResponse<OneTimeLinkPageDto>.Success(new OneTimeLinkPageDto { Items = [first, second, other] })),
                Task.FromResult(ApiResponse<OneTimeLinkPageDto>.Success(new OneTimeLinkPageDto { Items = [other] })));
        OneTimeLinksClient.RevokeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = RenderWarning();

        cut.WaitForElement("#otl-edit-revoke").Click();
        cut.InvokeAsync(() => cut.FindComponent<ConfirmDialog>().Instance.OnConfirm.InvokeAsync());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(OneTimeLinkEditWarning.RevokedText, cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll(Warning));
        });
        _ = OneTimeLinksClient.Received(1).RevokeAsync(first.Id, Arg.Any<CancellationToken>());
        _ = OneTimeLinksClient.Received(1).RevokeAsync(second.Id, Arg.Any<CancellationToken>());
        _ = OneTimeLinksClient.DidNotReceive().RevokeAsync(other.Id, Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<OneTimeLinkEditWarning> RenderWarning() =>
        Render<OneTimeLinkEditWarning>(parameters => parameters.Add(warning => warning.SecretId, _secretId));

    private void ReturnsPage(string? continuation, OneTimeLinkPageDto page) =>
        OneTimeLinksClient.ListAsync(Arg.Any<int>(), continuation, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkPageDto>.Success(page)));

    private static OneTimeLinkDto Link(Guid itemId) => new() { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), ItemId = itemId };
}
