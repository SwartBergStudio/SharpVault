// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AutoFixture;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class SecretEditorTests : VaultTestContext
{
    private readonly Fixture _fixture = new();
    private readonly IRenderedComponent<MudDialogProvider> _provider;
    private readonly SecretDto _secret;
    private readonly List<UpdateSecretRequestDto> _updates = [];
    private readonly List<CreateSecretRequestDto> _creates = [];
    private readonly string _notes;

    public SecretEditorTests()
    {
        _provider = RenderDialogProviders();
        _notes = "note-" + _fixture.Create<Guid>().ToString("N");
        _secret = new SecretDto
        {
            Id = _fixture.Create<Guid>(),
            Type = SecretTypeDto.Login,
            Name = "name-" + _fixture.Create<Guid>().ToString("N"),
            Username = "user-" + _fixture.Create<Guid>().ToString("N"),
            Url = "https://example.test/" + _fixture.Create<Guid>().ToString("N"),
            Tags = ["alpha", "beta"],
            HasValue = true,
            HasNotes = true,
            ConcurrencyToken = "etag-" + _fixture.Create<Guid>().ToString("N"),
        };
        ReturnsSecret();
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto())));
        SecretsClient.RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = _notes })));
        SecretsClient.UpdateSecretAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Do<UpdateSecretRequestDto>(_updates.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(_secret)));
        SecretsClient.CreateSecretAsync(Arg.Do<CreateSecretRequestDto>(_creates.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(_secret)));
    }

    private NavigationManager Navigator => Services.GetRequiredService<NavigationManager>();

    private static string ValueOf(IElement element) => element switch
    {
        IHtmlInputElement input => input.Value,
        IHtmlTextAreaElement area => area.Value,
        _ => element.TextContent,
    };

    private static UpdateSecretRequestDto ModelOf(IRenderedComponent<SecretEditor> cut) =>
        (UpdateSecretRequestDto)typeof(SecretEditor).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;

    private void ReturnsSecret() =>
        SecretsClient.GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(_secret)));

    private IRenderedComponent<SecretEditor> RenderEdit()
    {
        var cut = Render<SecretEditor>(parameters => parameters.Add(editor => editor.SecretId, _secret.Id));
        cut.WaitForElement("#secret-save");
        return cut;
    }

    private IRenderedComponent<SecretEditor> RenderNew(Guid? folderId = null)
    {
        var cut = Render<SecretEditor>(parameters => parameters.Add(editor => editor.FolderId, folderId?.ToString("D")));
        cut.WaitForElement("#secret-save");
        return cut;
    }

    private void AssertNoReveal() =>
        SecretsClient.DidNotReceive().RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>());

    [Fact]
    [Trait("Category", "Security")]
    public void Edit_Load_SecretNotFound_ShowsGoneMessage()
    {
        SecretsClient.GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.NotFound()));

        var cut = Render<SecretEditor>(parameters => parameters.Add(editor => editor.SecretId, _secret.Id));

        cut.WaitForAssertion(() => Assert.Contains("no longer exists", cut.Find("#secret-editor-error").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#secret-save"));
    }

    [Fact]
    public void Editor_DamagedSecret_DoesNotOfferTheForm()
    {
        _secret.IsDamaged = true;
        _secret.Name = null;

        var cut = Render<SecretEditor>(parameters => parameters.Add(editor => editor.SecretId, _secret.Id));

        cut.WaitForElement("#secret-editor-damaged");
        Assert.Empty(cut.FindAll("#secret-save"));
        Assert.Empty(cut.FindAll("#secret-name-input"));
        Assert.Contains(SecretEditor.DamagedText, cut.Find("#secret-editor-damaged").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("#secret-cancel"));
        SecretsClient.DidNotReceive().UpdateSecretAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task New_SaveSendsCreateRequestWithFolderAndTags()
    {
        var folder = new FolderDto { Id = _fixture.Create<Guid>(), Name = "Servers" };
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto { Items = [folder] })));
        var cut = RenderNew(folder.Id);
        cut.Find("#secret-name-input").Input("  Router  ");
        cut.Find("#secret-username-input").Input("admin");
        cut.Find("#secret-value-input").Input("s3cret-value");
        cut.Find("#secret-url-input").Input("https://router.test/");
        cut.Find("#secret-notes-input").Input("remember me");
        cut.Find("#secret-tags-input").Input(" one, two ,, three ");

        await cut.Find("#secret-save").ClickAsync();

        var request = Assert.Single(_creates);
        Assert.Equal("Router", request.Name);
        Assert.Equal(SecretTypeDto.Login, request.Type);
        Assert.Equal("admin", request.Username);
        Assert.Equal("s3cret-value", request.Value);
        Assert.Equal("https://router.test/", request.Url);
        Assert.Equal("remember me", request.Notes);
        Assert.Equal(["one", "two", "three"], request.Tags);
        Assert.Equal(folder.Id, request.FolderId);
        Assert.EndsWith(VaultRoutes.Secret(_secret.Id), Navigator.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_FolderIdFromAnotherUser_IsNotSent()
    {
        var cut = RenderNew(_fixture.Create<Guid>());
        cut.Find("#secret-name-input").Input("Router");

        await cut.Find("#secret-save").ClickAsync();

        Assert.Null(Assert.Single(_creates).FolderId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task New_SecureNote_CreateSendsNulls()
    {
        var cut = RenderNew();
        cut.Find("#secret-username-input").Input("typed-before-switching");
        cut.Find("#secret-value-input").Input("typed-secret-before-switching");
        cut.Find("#secret-url-input").Input("https://typed.test/");
        cut.Find("#secret-type-select").Change("SecureNote");
        cut.Find("#secret-name-input").Input("My note");
        cut.Find("#secret-notes-input").Input("body of the note");

        await cut.Find("#secret-save").ClickAsync();

        var request = Assert.Single(_creates);
        Assert.Equal(SecretTypeDto.SecureNote, request.Type);
        Assert.Null(request.Username);
        Assert.Null(request.Url);
        Assert.Null(request.Value);
        Assert.Equal("body of the note", request.Notes);
    }

    [Fact]
    public async Task New_Invalid_ShowsDetail()
    {
        SecretsClient.CreateSecretAsync(Arg.Any<CreateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Invalid("The URL is not valid.")));
        var cut = RenderNew();
        cut.Find("#secret-name-input").Input("Router");

        await cut.Find("#secret-save").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains("The URL is not valid.", cut.Find("#secret-form-error").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(VaultRoutes.Secret(_secret.Id), Navigator.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_BlankName_ShowsErrorAndCallsNothing()
    {
        var cut = RenderNew();
        cut.Find("#secret-name-input").Input("   ");

        await cut.Find("#secret-save").ClickAsync();

        Assert.Contains(SecretEditor.NameRequiredText, cut.Find("#secret-form-error").TextContent, StringComparison.Ordinal);
        await SecretsClient.DidNotReceive().CreateSecretAsync(Arg.Any<CreateSecretRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Save_WhileSaving_DoesNotSendASecondRequest()
    {
        var gate = new TaskCompletionSource<ApiResponse<SecretDto>>();
        SecretsClient.CreateSecretAsync(Arg.Any<CreateSecretRequestDto>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderNew();
        cut.Find("#secret-name-input").Input("Router");

        cut.Find("#secret-save").Click();
        cut.Find("#secret-save").Click();

        SecretsClient.Received(1).CreateSecretAsync(Arg.Any<CreateSecretRequestDto>(), Arg.Any<CancellationToken>());
        gate.SetResult(ApiResponse<SecretDto>.Success(_secret));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Edit_StartsWithChangeUnchecked_ValueAndNotesEmptyAndDisabled()
    {
        var cut = RenderEdit();

        Assert.False(cut.Find("#secret-change-value").HasAttribute("checked"));
        Assert.False(cut.Find("#secret-change-notes").HasAttribute("checked"));
        var value = cut.Find("#secret-value-input");
        var notes = cut.Find("#secret-notes-input");
        Assert.Equal(string.Empty, ValueOf(value));
        Assert.Equal(string.Empty, ValueOf(notes));
        Assert.True(value.HasAttribute("disabled"));
        Assert.True(notes.HasAttribute("disabled"));
        AssertNoReveal();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Edit_Load_NeverRevealsAndNeverPutsAValueInTheMarkup()
    {
        var cut = RenderEdit();

        AssertNoReveal();
        Assert.DoesNotContain(_notes, cut.Markup, StringComparison.Ordinal);
        Assert.Null(ModelOf(cut).Value);
        Assert.Null(ModelOf(cut).Notes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Edit_ChangeValueChecked_FetchesNothingAndEnablesAnEmptyInput()
    {
        var cut = RenderEdit();

        cut.Find("#secret-change-value").Change(true);

        var value = cut.Find("#secret-value-input");
        Assert.True(cut.Find("#secret-change-value").HasAttribute("checked"));
        Assert.False(value.HasAttribute("disabled"));
        Assert.Equal(string.Empty, ValueOf(value));
        AssertNoReveal();
        SecretsClient.Received(1).GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Edit_ChangeNotes_RevealsNotesOnceWithViewIntent()
    {
        var cut = RenderEdit();

        cut.Find("#secret-change-notes").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(_notes, ValueOf(cut.Find("#secret-notes-input"))));
        SecretsClient.Received(1).RevealSecretAsync(_secret.Id, Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>());
        SecretsClient.Received(1).RevealSecretAsync(
            _secret.Id,
            Arg.Is<RevealSecretRequestDto>(request => request.Field == RevealFieldDto.Notes && request.Intent == RevealIntentDto.View),
            Arg.Any<CancellationToken>());
        Assert.False(cut.Find("#secret-notes-input").HasAttribute("disabled"));
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    public async Task Edit_ChangeNotes_ItemWithoutNotes_DoesNotRevealAndEnablesAnEmptyInput()
    {
        _secret.HasNotes = false;
        var cut = RenderEdit();

        cut.Find("#secret-change-notes").Change(true);

        cut.WaitForAssertion(() => Assert.False(cut.Find("#secret-notes-input").HasAttribute("disabled")));
        Assert.Equal(string.Empty, ValueOf(cut.Find("#secret-notes-input")));
        AssertNoReveal();
        cut.Find("#secret-notes-input").Input("fresh notes");
        await cut.Find("#secret-save").ClickAsync();
        var request = Assert.Single(_updates);
        Assert.True(request.ReplaceNotes);
        Assert.Equal("fresh notes", request.Notes);
    }

    [Fact]
    public async Task Edit_ChangeNotes_RevealFails_ShowsErrorKeepsNotesLockedAndSendsNoReplacement()
    {
        SecretsClient.RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<RevealedValueDto>.RateLimited()));
        var cut = RenderEdit();

        cut.Find("#secret-change-notes").Change(true);

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.For(ApiResponse<bool>.RateLimited()), cut.Find("#secret-form-error").TextContent, StringComparison.Ordinal));
        Assert.True(cut.Find("#secret-notes-input").HasAttribute("disabled"));
        await cut.Find("#secret-save").ClickAsync();
        Assert.False(Assert.Single(_updates).ReplaceNotes);
        Assert.Equal(string.Empty, ValueOf(cut.Find("#secret-notes-input")));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_ChangeNotesUnchecked_AfterReveal_DropsTheNotesAndKeepsThem()
    {
        var cut = RenderEdit();
        cut.Find("#secret-change-notes").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(_notes, ValueOf(cut.Find("#secret-notes-input"))));

        cut.Find("#secret-change-notes").Change(false);

        Assert.Equal(string.Empty, ValueOf(cut.Find("#secret-notes-input")));
        Assert.True(cut.Find("#secret-notes-input").HasAttribute("disabled"));
        Assert.DoesNotContain(_notes, cut.Markup, StringComparison.Ordinal);
        await cut.Find("#secret-save").ClickAsync();
        var request = Assert.Single(_updates);
        Assert.False(request.ReplaceNotes);
        Assert.Null(request.Notes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_SaveSendsReplaceFlagsAndConcurrencyToken()
    {
        var cut = RenderEdit();
        cut.Find("#secret-name-input").Input("Renamed");

        await cut.Find("#secret-save").ClickAsync();

        await SecretsClient.Received(1).UpdateSecretAsync(_secret.Id, _secret.ConcurrencyToken, Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>());
        var request = Assert.Single(_updates);
        Assert.False(request.ReplaceValue);
        Assert.Null(request.Value);
        Assert.False(request.ReplaceNotes);
        Assert.Null(request.Notes);
        Assert.Equal("Renamed", request.Name);
        Assert.Equal(["alpha", "beta"], request.Tags);
        Assert.Equal(SecretTypeDto.Login, request.Type);
        Assert.EndsWith(VaultRoutes.Secret(_secret.Id), Navigator.Uri, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_SaveWithTickedEmptyValue_SendsReplaceTrueWithNullValueToClear()
    {
        var cut = RenderEdit();
        cut.Find("#secret-change-value").Change(true);

        await cut.Find("#secret-save").ClickAsync();

        await SecretsClient.Received(1).UpdateSecretAsync(_secret.Id, _secret.ConcurrencyToken, Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>());
        var request = Assert.Single(_updates);
        Assert.True(request.ReplaceValue);
        Assert.Null(request.Value);
        Assert.False(request.ReplaceNotes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_SaveWithTickedValue_SendsTheTypedValue()
    {
        var cut = RenderEdit();
        cut.Find("#secret-change-value").Change(true);
        cut.Find("#secret-value-input").Input("brand-new-value");

        await cut.Find("#secret-save").ClickAsync();

        var request = Assert.Single(_updates);
        Assert.True(request.ReplaceValue);
        Assert.Equal("brand-new-value", request.Value);
        Assert.False(request.ReplaceNotes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_UntickValueAfterTyping_SendsNoReplacement()
    {
        var cut = RenderEdit();
        cut.Find("#secret-change-value").Change(true);
        cut.Find("#secret-value-input").Input("typed-then-abandoned");

        cut.Find("#secret-change-value").Change(false);
        await cut.Find("#secret-save").ClickAsync();

        Assert.Equal(string.Empty, ValueOf(cut.Find("#secret-value-input")));
        var request = Assert.Single(_updates);
        Assert.False(request.ReplaceValue);
        Assert.Null(request.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_SaveWithRevealedNotesEdited_SendsReplaceNotesWithTheEditedText()
    {
        var cut = RenderEdit();
        cut.Find("#secret-change-notes").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(_notes, ValueOf(cut.Find("#secret-notes-input"))));
        cut.Find("#secret-notes-input").Input(_notes + " more");

        await cut.Find("#secret-save").ClickAsync();

        var request = Assert.Single(_updates);
        Assert.True(request.ReplaceNotes);
        Assert.Equal(_notes + " more", request.Notes);
        Assert.False(request.ReplaceValue);
        Assert.Null(request.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_Conflict_ShowsConflictPanelAndKeepsEveryTypedField()
    {
        SecretsClient.UpdateSecretAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Conflict()));
        var cut = RenderEdit();
        cut.Find("#secret-name-input").Input("Typed name");
        cut.Find("#secret-username-input").Input("typed-user");
        cut.Find("#secret-url-input").Input("https://typed.test/");
        cut.Find("#secret-tags-input").Input("x, y");
        cut.Find("#secret-change-value").Change(true);
        cut.Find("#secret-value-input").Input("typed-secret-value");
        cut.Find("#secret-change-notes").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(_notes, ValueOf(cut.Find("#secret-notes-input"))));
        cut.Find("#secret-notes-input").Input("typed notes");

        await cut.Find("#secret-save").ClickAsync();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#secret-conflict")));
        Assert.Contains(SecretEditor.ConflictText, cut.Find("#secret-conflict").TextContent, StringComparison.Ordinal);
        Assert.Equal("Typed name", ValueOf(cut.Find("#secret-name-input")));
        Assert.Equal("typed-user", ValueOf(cut.Find("#secret-username-input")));
        Assert.Equal("https://typed.test/", ValueOf(cut.Find("#secret-url-input")));
        Assert.Equal("x, y", ValueOf(cut.Find("#secret-tags-input")));
        Assert.Equal("typed-secret-value", ValueOf(cut.Find("#secret-value-input")));
        Assert.Equal("typed notes", ValueOf(cut.Find("#secret-notes-input")));
        Assert.True(cut.Find("#secret-change-value").HasAttribute("checked"));
        Assert.True(cut.Find("#secret-change-notes").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("#secret-form-error"));
        await SecretsClient.Received(1).GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>());
        await SecretsClient.Received(1).UpdateSecretAsync(_secret.Id, _secret.ConcurrencyToken, Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>());
        Assert.DoesNotContain(VaultRoutes.Secret(_secret.Id), Navigator.Uri, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_Conflict_SaveAgain_SendsTheStaleTokenAgainAndOverwritesNothing()
    {
        SecretsClient.UpdateSecretAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Conflict()));
        var cut = RenderEdit();
        cut.Find("#secret-name-input").Input("Typed name");
        await cut.Find("#secret-save").ClickAsync();
        cut.WaitForElement("#secret-conflict");

        await cut.Find("#secret-save").ClickAsync();

        await SecretsClient.Received(2).UpdateSecretAsync(_secret.Id, _secret.ConcurrencyToken, Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>());
        await SecretsClient.Received(1).GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>());
        Assert.Equal("Typed name", ValueOf(cut.Find("#secret-name-input")));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_Conflict_Reload_LoadsTheLatestAndDiscardsTheTypedFields()
    {
        var latest = new SecretDto
        {
            Id = _secret.Id,
            Type = SecretTypeDto.Login,
            Name = "latest-name",
            Username = "latest-user",
            HasValue = true,
            ConcurrencyToken = "etag-latest",
        };
        SecretsClient.UpdateSecretAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Conflict()));
        var cut = RenderEdit();
        cut.Find("#secret-name-input").Input("Typed name");
        cut.Find("#secret-change-value").Change(true);
        cut.Find("#secret-value-input").Input("typed-secret-value");
        await cut.Find("#secret-save").ClickAsync();
        cut.WaitForElement("#secret-conflict");
        SecretsClient.GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(latest)));

        cut.Find("#secret-conflict-reload").Click();

        cut.WaitForAssertion(() => Assert.Equal("latest-name", ValueOf(cut.Find("#secret-name-input"))));
        Assert.Empty(cut.FindAll("#secret-conflict"));
        Assert.DoesNotContain("typed-secret-value", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(string.Empty, ValueOf(cut.Find("#secret-value-input")));
        Assert.False(cut.Find("#secret-change-value").HasAttribute("checked"));
        await SecretsClient.Received(2).GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>());
        await cut.Find("#secret-save").ClickAsync();
        await SecretsClient.Received(1).UpdateSecretAsync(_secret.Id, "etag-latest", Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ValueInput_IsPasswordTypeWithNewPasswordAutocomplete()
    {
        var cut = RenderNew();

        var input = cut.Find("#secret-value-input");
        Assert.Equal("password", input.GetAttribute("type"));
        Assert.Equal("new-password", input.GetAttribute("autocomplete"));
        Assert.Equal("false", input.GetAttribute("spellcheck"));
        var notes = cut.Find("#secret-notes-input");
        Assert.Equal("new-password", notes.GetAttribute("autocomplete"));
    }

    [Fact]
    public void ValueToggle_ShowsAndHidesTheTypedValue()
    {
        var cut = RenderNew();
        cut.Find("#secret-value-input").Input("typed");

        cut.Find("#secret-value-toggle").Click();
        Assert.Equal("text", cut.Find("#secret-value-input").GetAttribute("type"));
        cut.Find("#secret-value-toggle").Click();

        Assert.Equal("password", cut.Find("#secret-value-input").GetAttribute("type"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Generate_UseFillsValueAndSetsReplaceValue()
    {
        var cut = RenderEdit();
        Assert.False(cut.Find("#secret-change-value").HasAttribute("checked"));
        cut.Find("#secret-generate").Click();
        var generated = _provider.WaitForElement("#generator-output").TextContent;

        _provider.Find("#generator-use").Click();

        cut.WaitForAssertion(() => Assert.True(cut.Find("#secret-change-value").HasAttribute("checked")));
        Assert.Equal(generated, ValueOf(cut.Find("#secret-value-input")));
        Assert.False(cut.Find("#secret-value-input").HasAttribute("disabled"));
        await cut.Find("#secret-save").ClickAsync();
        var request = Assert.Single(_updates);
        Assert.True(request.ReplaceValue);
        Assert.Equal(generated, request.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Dispose_ClearsInMemoryValueAndNotes()
    {
        var cut = RenderEdit();
        cut.Find("#secret-change-value").Change(true);
        cut.Find("#secret-value-input").Input("typed-secret-value");
        cut.Find("#secret-change-notes").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(_notes, ValueOf(cut.Find("#secret-notes-input"))));
        var model = ModelOf(cut);
        Assert.Equal("typed-secret-value", model.Value);
        Assert.Equal(_notes, model.Notes);

        await cut.Instance.DisposeAsync();

        Assert.Null(model.Value);
        Assert.Null(model.Notes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Save_NeverWritesTheValueOrNotesToTheLogs()
    {
        var cut = RenderEdit();
        cut.Find("#secret-change-value").Change(true);
        cut.Find("#secret-value-input").Input("typed-secret-value-for-logs");
        cut.Find("#secret-change-notes").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(_notes, ValueOf(cut.Find("#secret-notes-input"))));

        await cut.Find("#secret-save").ClickAsync();

        Assert.DoesNotContain("typed-secret-value-for-logs", LogText, StringComparison.Ordinal);
        Assert.DoesNotContain(_notes, LogText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_SecureNote_ReplacingNotes_StillKeepsStoredUsernameAndUrlAndValue()
    {
        _secret.Type = SecretTypeDto.SecureNote;
        _secret.Username = "legacy-user";
        _secret.Url = "https://legacy.test/path";
        var cut = RenderEdit();
        cut.Find("#secret-change-notes").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(_notes, ValueOf(cut.Find("#secret-notes-input"))));
        cut.Find("#secret-notes-input").Input("new body");

        await cut.Find("#secret-save").ClickAsync();

        var request = Assert.Single(_updates);
        Assert.True(request.ReplaceNotes);
        Assert.Equal("new body", request.Notes);
        Assert.Equal("legacy-user", request.Username);
        Assert.Equal("https://legacy.test/path", request.Url);
        Assert.False(request.ReplaceValue);
        Assert.Null(request.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Edit_ChangeTypeToSecureNote_KeepsStoredUsernameUrlAndValueAndDropsAnyPendingValue()
    {
        var cut = RenderEdit();
        cut.Find("#secret-change-value").Change(true);
        cut.Find("#secret-value-input").Input("pending-secret-value");

        cut.Find("#secret-type-select").Change("SecureNote");

        Assert.Empty(cut.FindAll("#secret-username-input"));
        Assert.Empty(cut.FindAll("#secret-value-input"));
        await cut.Find("#secret-save").ClickAsync();
        var request = Assert.Single(_updates);
        Assert.Equal(SecretTypeDto.SecureNote, request.Type);
        Assert.Equal(_secret.Username, request.Username);
        Assert.Equal(_secret.Url, request.Url);
        Assert.False(request.ReplaceValue);
        Assert.Null(request.Value);
    }

    [Fact]
    public async Task Edit_ChangeTypeFromSecureNoteToLogin_ShowsLockedCredentialInputs()
    {
        _secret.Type = SecretTypeDto.SecureNote;
        _secret.HasValue = false;
        var cut = RenderEdit();

        cut.Find("#secret-type-select").Change("Login");

        Assert.True(cut.Find("#secret-value-input").HasAttribute("disabled"));
        Assert.False(cut.Find("#secret-change-value").HasAttribute("checked"));
        Assert.Equal(_secret.Username, ValueOf(cut.Find("#secret-username-input")));
        await cut.Find("#secret-save").ClickAsync();
        var request = Assert.Single(_updates);
        Assert.Equal(SecretTypeDto.Login, request.Type);
        Assert.False(request.ReplaceValue);
        Assert.Null(request.Value);
    }

    [Fact]
    public async Task Edit_SignInRequired_OnSave_DoesNotShowAnError()
    {
        SecretsClient.UpdateSecretAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.SignInRequired()));
        var cut = RenderEdit();

        await cut.Find("#secret-save").ClickAsync();

        Assert.Empty(cut.FindAll("#secret-form-error"));
        Assert.Empty(cut.FindAll("#secret-conflict"));
    }

    [Fact]
    public void Cancel_LinksBackToTheItem()
    {
        var cut = RenderEdit();

        Assert.Equal(VaultRoutes.Secret(_secret.Id), cut.Find("#secret-cancel").GetAttribute("href"));
    }

    [Fact]
    public void Cancel_OnNewItemInFolder_LinksBackToTheFolder()
    {
        var folder = new FolderDto { Id = _fixture.Create<Guid>(), Name = "Servers" };
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto { Items = [folder] })));
        var cut = RenderNew(folder.Id);

        Assert.Equal(VaultRoutes.Folder(folder.Id), cut.Find("#secret-cancel").GetAttribute("href"));
    }
}
