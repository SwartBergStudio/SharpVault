// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using NSubstitute;
using SharpVault.Dtos.Directory;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class RecipientPickerTests : SharingTestHost
{
    private readonly List<string> _terms = [];
    private readonly List<DirectoryUserDto?> _selections = [];
    private Func<string, CancellationToken, Task<ApiResponse<DirectoryUserListDto>>> _search;

    public RecipientPickerTests()
    {
        _search = (_, _) => Task.FromResult(ApiResponse<DirectoryUserListDto>.Success(new DirectoryUserListDto()));
    }

    private static DirectoryUserDto Person(string name = "Grace Hopper", string? email = "grace@example.test") =>
        new() { Id = Guid.NewGuid(), DisplayName = name, Email = email };

    private static ApiResponse<DirectoryUserListDto> Found(params DirectoryUserDto[] people) =>
        ApiResponse<DirectoryUserListDto>.Success(new DirectoryUserListDto { Items = [.. people] });

    private void Returns(ApiResponse<DirectoryUserListDto> response) =>
        _search = (term, _) =>
        {
            _terms.Add(term);
            return Task.FromResult(response);
        };

    private IRenderedComponent<RecipientPicker> RenderPicker(DirectoryUserDto? selected = null, bool disabled = false) =>
        Render<RecipientPicker>(parameters => parameters
            .Add(picker => picker.Search, (term, token) => _search(term, token))
            .Add(picker => picker.Selected, selected)
            .Add(picker => picker.Disabled, disabled)
            .Add(picker => picker.SelectedChanged, user => _selections.Add(user)));

    private static async Task SearchAsync(IRenderedComponent<RecipientPicker> cut, string term)
    {
        cut.Find("#recipient-search-input").Input(term);
        await cut.Find("#recipient-search-form").SubmitAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("   ")]
    [InlineData("  ab  ")]
    public async Task Search_TooShort_NoCall(string term)
    {
        Returns(Found(Person()));
        var cut = RenderPicker();

        await SearchAsync(cut, term);

        Assert.Empty(_terms);
        Assert.Contains(RecipientPicker.TooShortText, cut.Find("#recipient-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#recipient-results"));
    }

    [Fact]
    public async Task Search_ExactlyMinimumLength_SendsTheTrimmedTerm()
    {
        Returns(Found(Person()));
        var cut = RenderPicker();

        await SearchAsync(cut, "  gra ");

        cut.WaitForElement("#recipient-results");
        Assert.Equal(["gra"], _terms);
        Assert.Empty(cut.FindAll("#recipient-error"));
    }

    [Fact]
    public async Task Search_NoResults_ShowsNoneAndNoList()
    {
        Returns(Found());
        var cut = RenderPicker();

        await SearchAsync(cut, "zzz");

        Assert.Equal(SharedItemStatusText.NoPeopleFound, cut.WaitForElement("#recipient-none").TextContent.Trim());
        Assert.Empty(cut.FindAll("#recipient-results"));
        Assert.Empty(cut.FindAll("#recipient-error"));
    }

    [Fact]
    public async Task Search_RateLimited_ShowsMessageAndNoResults()
    {
        Returns(ApiResponse<DirectoryUserListDto>.RateLimited());
        var cut = RenderPicker();

        await SearchAsync(cut, "gra");

        Assert.Contains(LoadableComponentBase<DirectoryUserListDto>.RateLimitedText, cut.WaitForElement("#recipient-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#recipient-none"));
        Assert.Empty(cut.FindAll("#recipient-results"));
    }

    [Theory]
    [InlineData("Search term is not allowed.", "Search term is not allowed.")]
    [InlineData(null, VaultStatusText.InvalidFallbackText)]
    [InlineData("   ", VaultStatusText.InvalidFallbackText)]
    public async Task Search_Invalid_ShowsTheDetailOrTheFallback(string? detail, string expected)
    {
        Returns(ApiResponse<DirectoryUserListDto>.Invalid(detail));
        var cut = RenderPicker();

        await SearchAsync(cut, "gra");

        Assert.Contains(expected, cut.WaitForElement("#recipient-error").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ApiCallStatus.Failed)]
    [InlineData(ApiCallStatus.NotFound)]
    [InlineData(ApiCallStatus.Conflict)]
    [InlineData(ApiCallStatus.ConfigurationMismatch)]
    public async Task Search_OtherFailures_ShowTheSearchFailedText(ApiCallStatus status)
    {
        Returns(new ApiResponse<DirectoryUserListDto>(status, null));
        var cut = RenderPicker();

        await SearchAsync(cut, "gra");

        Assert.Contains(SharedItemStatusText.SearchFailed, cut.WaitForElement("#recipient-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#recipient-results"));
    }

    [Theory]
    [InlineData(ApiCallStatus.SignInRequired)]
    [InlineData(ApiCallStatus.AccessDenied)]
    public async Task Search_SessionStatuses_ShowNeitherResultsNorErrors(ApiCallStatus status)
    {
        Returns(new ApiResponse<DirectoryUserListDto>(status, null));
        var cut = RenderPicker();

        await SearchAsync(cut, "gra");

        Assert.Single(_terms);
        Assert.Empty(cut.FindAll("#recipient-error"));
        Assert.Empty(cut.FindAll("#recipient-none"));
        Assert.Empty(cut.FindAll("#recipient-results"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Search_Throws_ShowsSearchFailedAndLogsOnlyTheExceptionType()
    {
        const string leaked = "directory-token-abc123";
        _search = (_, _) => throw new InvalidOperationException(leaked);
        var cut = RenderPicker();

        await SearchAsync(cut, "gra");

        Assert.Contains(SharedItemStatusText.SearchFailed, cut.WaitForElement("#recipient-error").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(leaked, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(leaked, LogText, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_AfterFailure_NextSearchClearsTheErrorAndShowsResults()
    {
        var calls = 0;
        _search = (_, _) => Task.FromResult(++calls == 1 ? ApiResponse<DirectoryUserListDto>.Failed() : Found(Person()));
        var cut = RenderPicker();
        await SearchAsync(cut, "gra");
        cut.WaitForElement("#recipient-error");

        await SearchAsync(cut, "gra");

        cut.WaitForElement("#recipient-results");
        Assert.Empty(cut.FindAll("#recipient-error"));
    }

    [Fact]
    public async Task Search_ThenTooShort_ClearsTheOldResults()
    {
        Returns(Found(Person()));
        var cut = RenderPicker();
        await SearchAsync(cut, "gra");
        cut.WaitForElement("#recipient-results");

        await SearchAsync(cut, "gr");

        Assert.Empty(cut.FindAll("#recipient-results"));
        Assert.NotEmpty(cut.FindAll("#recipient-error"));
    }

    [Fact]
    public async Task Search_ResultsListEachPersonWithNameAndEmail()
    {
        var withEmail = Person("Grace Hopper", "grace@example.test");
        var withoutEmail = Person("Alan Turing", null);
        var blankEmail = Person("Ada Lovelace", "  ");
        Returns(Found(withEmail, withoutEmail, blankEmail));
        var cut = RenderPicker();

        await SearchAsync(cut, "gra");

        cut.WaitForElement("#recipient-results");
        Assert.Equal("Grace Hopper (grace@example.test)", cut.Find($"#recipient-result-{withEmail.Id:D}").TextContent.Trim());
        Assert.Equal("Alan Turing", cut.Find($"#recipient-result-{withoutEmail.Id:D}").TextContent.Trim());
        Assert.Equal("Ada Lovelace", cut.Find($"#recipient-result-{blankEmail.Id:D}").TextContent.Trim());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Render_NameWithMarkup_IsEncodedInResultsAndSelection()
    {
        var hostile = Person("<img src=x onerror=alert(1)>", "<script>alert(2)</script>@example.test");
        Returns(Found(hostile));
        var cut = RenderPicker();

        await SearchAsync(cut, "gra");

        cut.WaitForElement("#recipient-results");
        Assert.Empty(cut.FindAll("#recipient-results img"));
        Assert.Empty(cut.FindAll("#recipient-results script"));
        Assert.Contains("<img src=x onerror=alert(1)>", cut.Find($"#recipient-result-{hostile.Id:D}").TextContent, StringComparison.Ordinal);
        var selected = Render<RecipientPicker>(parameters => parameters
            .Add(picker => picker.Search, (term, token) => _search(term, token))
            .Add(picker => picker.Selected, hostile));
        Assert.Empty(selected.FindAll("#recipient-selected img"));
        Assert.Empty(selected.FindAll("#recipient-selected script"));
        Assert.Contains("<img src=x onerror=alert(1)>", selected.Find("#recipient-selected").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Select_RaisesTheChoiceAndHidesTheResultsAndError()
    {
        var chosen = Person();
        Returns(Found(chosen, Person("Other Person")));
        var cut = RenderPicker();
        await SearchAsync(cut, "gra");

        await cut.WaitForElement($"#recipient-result-{chosen.Id:D}").ClickAsync();

        Assert.Equal([chosen], _selections);
        Assert.Empty(cut.FindAll("#recipient-results"));
        Assert.Empty(cut.FindAll("#recipient-error"));
    }

    [Fact]
    public async Task Select_ShowsSelectedAndClearRaisesNullAndShowsAnEmptySearch()
    {
        var chosen = Person();
        var cut = RenderPicker(chosen);

        Assert.Equal("Grace Hopper (grace@example.test)", cut.Find("#recipient-selected").TextContent.Trim());
        Assert.Empty(cut.FindAll("#recipient-search-input"));
        await cut.Find("#recipient-clear").ClickAsync();

        Assert.Single(_selections);
        Assert.Null(_selections[0]);
        cut.Render(parameters => parameters.Add(picker => picker.Selected, null));
        Assert.Empty(cut.FindAll("#recipient-selected"));
        Assert.Equal(string.Empty, cut.Find("#recipient-search-input").GetAttribute("value") ?? string.Empty);
        Assert.Empty(cut.FindAll("#recipient-results"));
    }

    [Fact]
    public async Task Clear_AfterASearch_DoesNotBringBackOldResultsOrTerm()
    {
        var chosen = Person();
        Returns(Found(chosen));
        var cut = RenderPicker();
        await SearchAsync(cut, "gra");
        await cut.WaitForElement($"#recipient-result-{chosen.Id:D}").ClickAsync();
        cut.Render(parameters => parameters.Add(picker => picker.Selected, chosen));

        await cut.Find("#recipient-clear").ClickAsync();
        cut.Render(parameters => parameters.Add(picker => picker.Selected, null));

        Assert.Empty(cut.FindAll("#recipient-results"));
        Assert.Empty(cut.FindAll("#recipient-error"));
        Assert.Equal(string.Empty, cut.Find("#recipient-search-input").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public async Task Search_Disabled_SendsNothingAndDisablesTheControls()
    {
        Returns(Found(Person()));
        var cut = RenderPicker(disabled: true);

        await SearchAsync(cut, "gra");

        Assert.Empty(_terms);
        Assert.True(cut.Find("#recipient-search-input").HasAttribute("disabled"));
        Assert.True(cut.Find("#recipient-search-submit").HasAttribute("disabled"));
    }

    [Fact]
    public void Render_Selected_DisabledPickerDisablesClear()
    {
        var cut = RenderPicker(Person(), disabled: true);

        Assert.True(cut.Find("#recipient-clear").HasAttribute("disabled"));
    }

    [Fact]
    public void Render_SearchInput_LimitsTheTermLength()
    {
        var cut = RenderPicker();

        Assert.Equal(RecipientPicker.MaxTermLength.ToString(System.Globalization.CultureInfo.InvariantCulture), cut.Find("#recipient-search-input").GetAttribute("maxlength"));
    }

    [Fact]
    public async Task Search_SubmittedTwiceWhileRunning_SendsOneRequest()
    {
        var gate = new TaskCompletionSource<ApiResponse<DirectoryUserListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _search = (_, _) =>
        {
            calls++;
            return gate.Task;
        };
        var cut = RenderPicker();
        cut.Find("#recipient-search-input").Input("gra");

        var first = cut.Find("#recipient-search-form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.True(cut.Find("#recipient-search-input").HasAttribute("disabled")));
        await cut.Find("#recipient-search-form").SubmitAsync().WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        gate.SetResult(Found(Person()));
        await first;
        cut.WaitForElement("#recipient-results");
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Search_DisposedWhileRunning_CancelsTheTokenAndDoesNotThrow()
    {
        var gate = new TaskCompletionSource<ApiResponse<DirectoryUserListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken seen = default;
        _search = (_, token) =>
        {
            seen = token;
            return gate.Task;
        };
        var cut = RenderPicker();
        cut.Find("#recipient-search-input").Input("gra");
        var pending = cut.Find("#recipient-search-form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.True(cut.Find("#recipient-search-input").HasAttribute("disabled")));

        await DisposeComponentsAsync();
        gate.SetResult(Found(Person()));
        await pending;

        Assert.True(seen.IsCancellationRequested);
        Assert.Empty(_selections);
    }

    [Fact]
    public async Task Search_CancelledByDisposal_LogsNothingAboutFailure()
    {
        CancellationToken seen = default;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _search = async (_, token) =>
        {
            seen = token;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Found();
        };
        var cut = RenderPicker();
        cut.Find("#recipient-search-input").Input("gra");
        var pending = cut.Find("#recipient-search-form").SubmitAsync();
        await started.Task;

        await DisposeComponentsAsync();
        await pending;

        Assert.True(seen.IsCancellationRequested);
        Assert.DoesNotContain("recipient search failed", LogText, StringComparison.OrdinalIgnoreCase);
    }
}
