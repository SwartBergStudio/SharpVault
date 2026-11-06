// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class SecretEditorDegradedTests : VaultTestContext
{
    private readonly List<CreateSecretRequestDto> _creates = [];

    public SecretEditorDegradedTests()
    {
        SecretsClient.CreateSecretAsync(Arg.Do<CreateSecretRequestDto>(_creates.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(new SecretDto { Id = Guid.NewGuid() })));
    }

    [Theory]
    [InlineData(ApiCallStatus.Failed)]
    [InlineData(ApiCallStatus.RateLimited)]
    [InlineData(ApiCallStatus.NotFound)]
    public void New_FolderListFails_FormStillWorksWithRootOnlyAndANote(ApiCallStatus status)
    {
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ApiResponse<FolderListDto>(status, null)));

        var cut = Render<SecretEditor>(parameters => parameters.Add(editor => editor.FolderId, Guid.NewGuid().ToString("D")));
        cut.WaitForElement("#secret-save");

        Assert.Empty(cut.FindAll("#secret-editor-error"));
        Assert.NotNull(cut.Find("#secret-folders-unavailable"));
        Assert.Single(cut.Find("#secret-folder-select").QuerySelectorAll("option"));
        cut.Find("#secret-name-input").Input("Router");
        cut.Find("#secret-save").Click();
        cut.WaitForAssertion(() => Assert.Null(Assert.Single(_creates).FolderId));
    }

    [Theory]
    [InlineData(ApiCallStatus.SignInRequired)]
    [InlineData(ApiCallStatus.AccessDenied)]
    public void New_FolderListSessionStatus_DoesNotShowTheForm(ApiCallStatus status)
    {
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ApiResponse<FolderListDto>(status, null)));

        var cut = Render<SecretEditor>();

        Assert.Empty(cut.FindAll("#secret-save"));
    }

    [Fact]
    public void New_FolderListSucceeds_ShowsNoNote()
    {
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto())));

        var cut = Render<SecretEditor>();
        cut.WaitForElement("#secret-save");

        Assert.Empty(cut.FindAll("#secret-folders-unavailable"));
    }

    [Theory]
    [InlineData(ApiCallStatus.RateLimited)]
    [InlineData(ApiCallStatus.NotFound)]
    [InlineData(ApiCallStatus.Failed)]
    public void Edit_ChangeNotes_RevealFails_UnticksTheChangeNotesCheckbox(ApiCallStatus status)
    {
        var secret = ExistingSecret();
        SecretsClient.RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ApiResponse<RevealedValueDto>(status, null)));
        var cut = Render<SecretEditor>(parameters => parameters.Add(editor => editor.SecretId, secret.Id));
        cut.WaitForElement("#secret-save");

        cut.Find("#secret-change-notes").Change(true);

        cut.WaitForElement("#secret-form-error");
        Assert.True(cut.Find("#secret-notes-input").HasAttribute("disabled"));
        cut.WaitForAssertion(() => Assert.False(cut.Find("#secret-change-notes").HasAttribute("checked")));
    }

    [Fact]
    public void Edit_ChangeNotes_RevealFailsThenSucceeds_RetryWorksOnTheNextTick()
    {
        var secret = ExistingSecret();
        var calls = 0;
        SecretsClient.RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(++calls == 1
                ? new ApiResponse<RevealedValueDto>(ApiCallStatus.Failed, null)
                : ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = "note" })));
        var cut = Render<SecretEditor>(parameters => parameters.Add(editor => editor.SecretId, secret.Id));
        cut.WaitForElement("#secret-save");
        cut.Find("#secret-change-notes").Change(true);
        cut.WaitForElement("#secret-form-error");

        cut.Find("#secret-change-notes").Change(true);

        cut.WaitForAssertion(() => Assert.False(cut.Find("#secret-notes-input").HasAttribute("disabled")));
        Assert.True(cut.Find("#secret-change-notes").HasAttribute("checked"));
        Assert.Equal(2, calls);
    }

    private SecretDto ExistingSecret()
    {
        var secret = new SecretDto
        {
            Id = Guid.NewGuid(),
            Type = SecretTypeDto.Login,
            Name = "name",
            HasNotes = true,
            ConcurrencyToken = "etag",
        };
        SecretsClient.GetSecretAsync(secret.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(secret)));
        return secret;
    }
}
