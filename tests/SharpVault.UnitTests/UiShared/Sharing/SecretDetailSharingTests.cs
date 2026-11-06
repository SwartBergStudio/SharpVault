// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using SharpVault.Dtos.Directory;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Pages.Vault;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SecretDetailSharingTests : SharingTestHost
{
    private readonly Guid _secretId = Guid.NewGuid();
    private readonly Guid _colleagueId = Guid.NewGuid();

    public SecretDetailSharingTests()
    {
        Returns(_secretId, "Router login", damaged: false);
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto())));
        GrantsClient.ListSecretGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(_colleagueId, "Ada Lovelace")));
    }

    private void Returns(Guid id, string name, bool damaged) =>
        SecretsClient.GetSecretAsync(id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretDto>.Success(new SecretDto
            {
                Id = id,
                Name = name,
                Type = SecretTypeDto.Login,
                IsDamaged = damaged,
                ConcurrencyToken = "etag-" + id.ToString("N"),
            })));

    private IRenderedComponent<CascadingValue<CurrentUserDto?>> RenderPage(Guid id, bool signedIn = true) =>
        RenderWithUser<SecretDetailPage>(signedIn ? Owner : null, parameters => parameters.Add(page => page.SecretId, id));

    [Fact]
    public void Page_WithCurrentUser_ListsGrantsOfThisSecretForTheOwner()
    {
        var cut = RenderPage(_secretId);

        cut.WaitForElement($"#share-grant-name-{_colleagueId:D}");
        GrantsClient.Received(1).ListSecretGrantsAsync(Owner.UserId, _secretId, Arg.Any<CancellationToken>());
        Assert.Equal("Ada Lovelace", cut.Find($"#share-grant-name-{_colleagueId:D}").TextContent);
        Assert.Equal("Owner One", cut.Find($"#share-grant-by-{_colleagueId:D}").TextContent);
        Assert.NotNull(cut.Find("#share-add"));
    }

    [Fact]
    public void Page_NoCurrentUser_HasNoPanelAndNeverListsGrants()
    {
        var cut = RenderPage(_secretId, signedIn: false);

        cut.WaitForElement("#secret-heading");
        Assert.Empty(cut.FindAll("#share-panel"));
        GrantsClient.DidNotReceiveWithAnyArgs().ListSecretGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Page_DamagedItem_StillHasThePanel()
    {
        Returns(_secretId, "Broken", damaged: true);

        var cut = RenderPage(_secretId);

        cut.WaitForElement("#secret-damaged");
        cut.WaitForElement($"#share-grant-revoke-{_colleagueId:D}");
        Assert.Empty(cut.FindAll("#secret-fields"));
    }

    [Fact]
    public void Page_NoGrants_ShowsTheEmptyStateWithItsHint()
    {
        GrantsClient.ListSecretGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants());

        var cut = RenderPage(_secretId);

        cut.WaitForElement("#share-grants-empty");
        Assert.Contains("Select Add person", cut.Find("#share-grants-empty").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#share-add"));
    }

    [Fact]
    public void Page_GrantListFails_ItemStillRendersAndPanelOffersRetry()
    {
        GrantsClient.ListSecretGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<ShareGrantListDto>.Failed()));

        var cut = RenderPage(_secretId);

        cut.WaitForElement("#share-grants-error");
        Assert.Equal("Router login", cut.Find("#secret-heading").TextContent.Trim());
        Assert.NotNull(cut.Find("#secret-fields"));
        Assert.NotNull(cut.Find("#share-grants-retry"));
        Assert.Empty(cut.FindAll("#share-add"));
    }

    [Fact]
    public void Page_RouteChangesToAnotherSecret_ReloadsThePanelForTheNewSecret()
    {
        var otherId = Guid.NewGuid();
        Returns(otherId, "Other", damaged: false);
        var cut = RenderPage(_secretId);
        cut.WaitForElement($"#share-grant-name-{_colleagueId:D}");

        cut.Render(builder => builder
            .Add(cascade => cascade.Name, LayoutCascades.CurrentUser)
            .Add(cascade => cascade.Value, Owner)
            .AddChildContent<SecretDetailPage>(parameters => parameters.Add(page => page.SecretId, otherId)));

        cut.WaitForAssertion(() => GrantsClient.Received(1).ListSecretGrantsAsync(Owner.UserId, otherId, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Page_DeletePrompt_MentionsThatSharedPeopleLoseAccess()
    {
        var cut = RenderPage(_secretId);
        cut.WaitForElement("#secret-delete").Click();

        var prompt = Provider.WaitForElement("#secret-delete-prompt-dialog");

        Assert.Contains(SharedItemStatusText.SharingNoticeOnDelete, prompt.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_AddPerson_SearchesPicksAndCreatesTheGrantOnTheOwnersRoute()
    {
        var found = new DirectoryUserDto { Id = Guid.NewGuid(), DisplayName = "<b>Grace</b> Hopper", Email = "grace@example.test" };
        DirectoryClient.SearchUsersAsync("gra", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<DirectoryUserListDto>.Success(new DirectoryUserListDto { Items = [found] })));
        GrantsClient.CreateSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateShareGrantRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<ShareGrantDto>.Success(Grant(found.Id, "Grace Hopper"))));
        var cut = RenderPage(_secretId);
        cut.WaitForElement("#share-add").Click();

        Provider.WaitForElement("#recipient-search-input").Input("gra");
        Provider.Find("#recipient-search-form").Submit();
        Provider.WaitForElement($"#recipient-result-{found.Id:D}");
        Assert.DoesNotContain("<b>Grace", Provider.Find("#recipient-results").InnerHtml, StringComparison.Ordinal);
        Provider.Find($"#recipient-result-{found.Id:D}").Click();
        Provider.WaitForElement("#recipient-selected");
        Provider.Find("#share-dialog-confirm").Click();

        cut.WaitForAssertion(() => GrantsClient.Received(1).CreateSecretGrantAsync(
            Owner.UserId,
            _secretId,
            Arg.Is<CreateShareGrantRequestDto>(request => request.RecipientId == found.Id && request.Permission == SharePermissionDto.View),
            Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.Shared, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#share-dialog-confirm")));
        GrantsClient.Received(2).ListSecretGrantsAsync(Owner.UserId, _secretId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Page_SearchTooShort_ShowsMessageAndSendsNoRequest()
    {
        var cut = RenderPage(_secretId);
        cut.WaitForElement("#share-add").Click();

        Provider.WaitForElement("#recipient-search-input").Input("ab");
        Provider.Find("#recipient-search-form").Submit();

        Provider.WaitForElement("#recipient-error");
        DirectoryClient.DidNotReceiveWithAnyArgs().SearchUsersAsync(default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Page_Revoke_WarnsThenRevokesThatRecipientOnly()
    {
        GrantsClient.RevokeSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = RenderPage(_secretId);
        cut.WaitForElement($"#share-grant-revoke-{_colleagueId:D}").Click();

        var text = Provider.WaitForElement("#share-revoke-prompt-text");
        Assert.Equal(SharedItemStatusText.RevokeWarning("Ada Lovelace"), text.TextContent);
        Provider.Find("#share-revoke-prompt-confirm").Click();

        cut.WaitForAssertion(() => GrantsClient.Received(1).RevokeSecretGrantAsync(Owner.UserId, _secretId, _colleagueId, Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.Revoked, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Page_WhileAChangeRuns_AddRevokeAndPermissionAreVisiblyDisabled()
    {
        var gate = new TaskCompletionSource<ApiResponse<ShareGrantDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        GrantsClient.ChangeSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ChangeShareGrantRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task);
        var cut = RenderPage(_secretId);
        cut.WaitForElement($"#share-grant-permission-{_colleagueId:D}").Change(nameof(SharePermissionDto.Edit));

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("#share-add").HasAttribute("disabled"));
            Assert.True(cut.Find($"#share-grant-revoke-{_colleagueId:D}").HasAttribute("disabled"));
            Assert.True(cut.Find($"#share-grant-permission-{_colleagueId:D}").HasAttribute("disabled"));
        });
        GrantsClient.Received(1).ChangeSecretGrantAsync(Owner.UserId, _secretId, _colleagueId, "etag-" + _colleagueId.ToString("N"), Arg.Is<ChangeShareGrantRequestDto>(request => request.Permission == SharePermissionDto.Edit), Arg.Any<CancellationToken>());
        gate.SetResult(ApiResponse<ShareGrantDto>.Conflict());
        cut.WaitForAssertion(() => Assert.Contains("changed elsewhere", cut.Find("#share-status").TextContent, StringComparison.Ordinal));
    }
}
