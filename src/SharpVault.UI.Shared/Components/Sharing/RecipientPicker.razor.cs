// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos.Directory;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UI.Shared.Components.Sharing;

public sealed partial class RecipientPicker
{
    public const int MinTermLength = 3;
    public const int MaxTermLength = 100;
    public const string TooShortText = "Type at least 3 characters to search.";

    private readonly SearchModel _model = new();
    private IReadOnlyList<DirectoryUserDto>? _results;
    private string? _error;
    private bool _searching;

    [Parameter, EditorRequired]
    public Func<string, CancellationToken, Task<ApiResponse<DirectoryUserListDto>>> Search { get; set; } = default!;

    [Parameter]
    public DirectoryUserDto? Selected { get; set; }

    [Parameter]
    public EventCallback<DirectoryUserDto?> SelectedChanged { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    protected override void OnParametersSet() => ArgumentNullException.ThrowIfNull(Search);

    private static string Describe(DirectoryUserDto user) =>
        string.IsNullOrWhiteSpace(user.Email) ? user.DisplayName : $"{user.DisplayName} ({user.Email})";

    private void SetTerm(string value) => _model.Term = value;

    private Task SelectAsync(DirectoryUserDto user)
    {
        _results = null;
        _error = null;
        return SelectedChanged.InvokeAsync(user);
    }

    private Task ClearAsync()
    {
        _model.Term = string.Empty;
        _results = null;
        _error = null;
        return SelectedChanged.InvokeAsync(null);
    }

    private async Task SearchAsync()
    {
        if (_searching || Disabled)
        {
            return;
        }

        var term = _model.Term.Trim();
        _error = null;
        _results = null;
        if (term.Length < MinTermLength)
        {
            _error = TooShortText;
            return;
        }

        _searching = true;
        try
        {
            var response = await Search(term, Cts.Token);
            if (!IsDisposed)
            {
                Apply(response);
            }
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSearchFailed(Logger, exception.GetType().Name);
            _error = SharedItemStatusText.SearchFailed;
        }
        finally
        {
            _searching = false;
        }

        await InvokeStateChangedAsync();
    }

    private void Apply(ApiResponse<DirectoryUserListDto> response)
    {
        switch (response.Status)
        {
            case ApiCallStatus.Success when response.Value is not null:
                _results = response.Value.Items;
                break;
            case ApiCallStatus.SignInRequired:
            case ApiCallStatus.AccessDenied:
                break;
            case ApiCallStatus.Invalid:
            case ApiCallStatus.RateLimited:
                _error = VaultStatusText.For(response);
                break;
            default:
                _error = SharedItemStatusText.SearchFailed;
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A recipient search failed with {ExceptionType}")]
    private static partial void LogSearchFailed(ILogger logger, string exceptionType);

    private sealed class SearchModel
    {
        public string Term { get; set; } = string.Empty;
    }
}
