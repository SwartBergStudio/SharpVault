// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using NSubstitute;
using SharpVault.UI.Shared.Components.Sharing;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class FolderSharingDialogTests : SharingDialogTestHost
{
    private readonly Guid _folderId = Guid.NewGuid();
    private readonly Guid _otherFolderId = Guid.NewGuid();
    private readonly Guid _colleagueId = Guid.NewGuid();
    private int _closed;

    public FolderSharingDialogTests()
    {
        GrantsClient.ListFolderGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(_colleagueId, "Ada Lovelace")));
    }

    private IRenderedComponent<FolderSharingDialog> RenderDialog(bool open = true, string name = "Finance") =>
        Render<FolderSharingDialog>(parameters => parameters
            .Add(dialog => dialog.Open, open)
            .Add(dialog => dialog.OwnerId, Owner.UserId)
            .Add(dialog => dialog.FolderId, _folderId)
            .Add(dialog => dialog.FolderName, name)
            .Add(dialog => dialog.OnClose, () => _closed++));

    [Fact]
    public void Render_Closed_ShowsNoPanelAndListsNothing()
    {
        RenderDialog(open: false);

        Assert.Empty(Provider.FindAll("#folder-sharing-close"));
        Assert.Empty(Provider.FindAll("#share-panel"));
        GrantsClient.DidNotReceiveWithAnyArgs().ListFolderGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Render_Open_TitlesTheFolderAndListsItsGrantsOnTheOwnersRoute()
    {
        RenderDialog();

        Provider.WaitForElement($"#share-grant-name-{_colleagueId:D}");
        Assert.Equal("Share folder: Finance", Provider.Find("#folder-sharing-title").TextContent.Trim());
        GrantsClient.Received(1).ListFolderGrantsAsync(Owner.UserId, _folderId, Arg.Any<CancellationToken>());
        GrantsClient.DidNotReceiveWithAnyArgs().ListSecretGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Render_FolderNameWithMarkup_IsEncodedInTheTitle()
    {
        RenderDialog(name: "<img src=x onerror=alert(1)>");

        var title = Provider.WaitForElement("#folder-sharing-title");

        Assert.Contains("<img src=x onerror=alert(1)>", title.TextContent, StringComparison.Ordinal);
        Assert.Empty(title.QuerySelectorAll("img"));
    }

    [Fact]
    public async Task Close_RaisesOnClose()
    {
        RenderDialog();

        await Provider.WaitForElement("#folder-sharing-close").ClickAsync();

        Assert.Equal(1, _closed);
    }

    [Fact]
    public async Task Escape_RaisesOnClose()
    {
        RenderDialog();
        Provider.WaitForElement("#folder-sharing-close");

        await PressEscapeAsync();

        await PollAsync(() => _closed > 0);

        Assert.Equal(1, _closed);
    }

    [Fact]
    public async Task Reopen_ListsTheGrantsAgainInsteadOfShowingStaleOnes()
    {
        var cut = RenderDialog();
        Provider.WaitForElement($"#share-grant-name-{_colleagueId:D}");
        cut.Render(parameters => parameters.Add(dialog => dialog.Open, false));
        Provider.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#share-panel")));
        await WaitForClosedDialogsToSettleAsync();
        var newcomerId = Guid.NewGuid();
        GrantsClient.ListFolderGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(newcomerId, "Grace Hopper")));

        cut.Render(parameters => parameters.Add(dialog => dialog.Open, true));

        Provider.WaitForElement($"#share-grant-name-{newcomerId:D}");
        Assert.Empty(Provider.FindAll($"#share-grant-name-{_colleagueId:D}"));
        await GrantsClient.Received(2).ListFolderGrantsAsync(Owner.UserId, _folderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_FolderChangesWhileOpen_ListsTheGrantsOfTheNewFolder()
    {
        var cut = RenderDialog();
        Provider.WaitForElement($"#share-grant-name-{_colleagueId:D}");
        var newcomerId = Guid.NewGuid();
        GrantsClient.ListFolderGrantsAsync(Owner.UserId, _otherFolderId, Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(newcomerId, "Grace Hopper")));

        cut.Render(parameters => parameters
            .Add(dialog => dialog.FolderId, _otherFolderId)
            .Add(dialog => dialog.FolderName, "Legal"));

        Provider.WaitForElement($"#share-grant-name-{newcomerId:D}");
        Assert.Empty(Provider.FindAll($"#share-grant-name-{_colleagueId:D}"));
        Assert.Equal("Share folder: Legal", Provider.Find("#folder-sharing-title").TextContent.Trim());
    }
}
