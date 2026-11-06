// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Users;

namespace SharpVault.UI.Shared.Components.Base;

public abstract partial class LoadableComponentBase<T> : SharpVaultComponentBase
{
    public const string GenericErrorText = "This could not be loaded. Try again.";
    public const string ConfigurationMismatchText = "The web app and the API are set up for different sign-in modes. Check that both run in the same mode.";
    public const string NotFoundText = "This item no longer exists.";
    public const string RateLimitedText = "Too many requests. Try again in a minute.";
    public const string SignInLoopText = "Signing in again did not help. Contact an administrator.";

    private const string RetryFlagValue = "1";

    protected LoadState State { get; private set; } = LoadState.Loading;

    protected T? Value { get; private set; }

    protected string? ErrorText { get; private set; }

    protected bool IsLoadingMore { get; private set; }

    protected bool LoadMoreFailed { get; set; }

    protected string? LoadMoreText { get; private set; }

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ICurrentUserAccessor CurrentUser { get; set; } = default!;

    [Inject]
    private ISessionActions SessionActions { get; set; } = default!;

    protected abstract Task<ApiResponse<T>> LoadAsync(CancellationToken cancellationToken);

    protected virtual bool IsEmpty(T value) => false;

    protected override Task OnInitializedAsync() => RunLoadAsync();

    protected async Task ReloadAsync()
    {
        State = LoadState.Loading;
        ErrorText = null;
        await InvokeStateChangedAsync();
        await RunLoadAsync();
    }

    private async Task RunLoadAsync()
    {
        try
        {
            var response = await LoadAsync(Cts.Token);
            if (IsDisposed)
            {
                return;
            }

            Apply(response);
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
        }
        catch (Exception exception)
        {
            LogLoadFailed(Logger, exception.GetType().Name);
            Fail(GenericErrorText);
        }
    }

    private void Apply(ApiResponse<T> response)
    {
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            Value = response.Value;
            State = IsEmpty(response.Value) ? LoadState.Empty : LoadState.Loaded;
            return;
        }

        if (!HandleFailureStatus(response.Status))
        {
            Fail(GenericErrorText);
        }
    }

    protected bool HandleFailureStatus(ApiCallStatus status)
    {
        if (TryHandleSessionStatus(status))
        {
            return true;
        }

        switch (status)
        {
            case ApiCallStatus.ConfigurationMismatch:
                Fail(ConfigurationMismatchText);
                return true;
            case ApiCallStatus.NotFound:
                Fail(NotFoundText);
                return true;
            case ApiCallStatus.RateLimited:
                Fail(RateLimitedText);
                return true;
            default:
                return false;
        }
    }

    protected Task RunApiCommandAsync<TResponse>(
        Func<CancellationToken, Task<ApiResponse<TResponse>>> call,
        Func<ApiResponse<TResponse>, Task> onResponse,
        Action? onFailed = null) =>
        RunCommandAsync(
            async token =>
            {
                var response = await call(token);
                if (!TryHandleSessionStatus(response.Status))
                {
                    await onResponse(response);
                }
            },
            onFailed);

    protected Task RunLoadMoreAsync<TPage>(
        Func<CancellationToken, Task<ApiResponse<TPage>>> call,
        Action<TPage> onLoaded,
        string failedText) =>
        RunLoadMoreAsync(call, onLoaded, response => response.Status == ApiCallStatus.RateLimited ? RateLimitedText : failedText, failedText);

    protected async Task RunLoadMoreAsync<TPage>(
        Func<CancellationToken, Task<ApiResponse<TPage>>> call,
        Action<TPage> onLoaded,
        Func<ApiResponse<TPage>, string> failedTextFor,
        string thrownText)
    {
        if (IsLoadingMore)
        {
            return;
        }

        IsLoadingMore = true;
        LoadMoreFailed = false;
        try
        {
            await RunApiCommandAsync(
                call,
                response =>
                {
                    ApplyLoadMore(response, onLoaded, failedTextFor);
                    return Task.CompletedTask;
                },
                () => ShowLoadMoreFailed(thrownText));
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    protected virtual bool HandleLoadMoreFailure(ApiCallStatus status) => false;

    protected bool TryHandleSessionStatus(ApiCallStatus status)
    {
        switch (status)
        {
            case ApiCallStatus.SignInRequired:
                RedirectToSignIn();
                return true;
            case ApiCallStatus.AccessDenied:
                OnAccessDenied();
                return true;
            default:
                return false;
        }
    }

    protected virtual void OnAccessDenied()
    {
        CurrentUser.Invalidate();
        Navigation.NavigateTo(SessionActions.AccessDeniedUrl, forceLoad: true);
    }

    private void RedirectToSignIn()
    {
        if (HasRetryFlag())
        {
            Fail(SignInLoopText);
            return;
        }

        var current = new Uri(Navigation.Uri);
        var relative = "/" + Navigation.ToBaseRelativePath(current.GetLeftPart(UriPartial.Path)) + current.Query;
        var separator = relative.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var returnUrl = $"{relative}{separator}{AccountRoutes.SignInRetryParameter}={RetryFlagValue}";
        Navigation.NavigateTo(SessionActions.SignInUrl(returnUrl), forceLoad: true);
    }

    private bool HasRetryFlag()
    {
        var query = HttpUtility.ParseQueryString(new Uri(Navigation.Uri).Query);
        return query[AccountRoutes.SignInRetryParameter] is not null;
    }

    private void Fail(string text)
    {
        ErrorText = text;
        State = LoadState.Error;
    }

    private void ApplyLoadMore<TPage>(ApiResponse<TPage> response, Action<TPage> onLoaded, Func<ApiResponse<TPage>, string> failedTextFor)
    {
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            onLoaded(response.Value);
            return;
        }

        if (HandleLoadMoreFailure(response.Status))
        {
            return;
        }

        ShowLoadMoreFailed(failedTextFor(response));
    }

    private void ShowLoadMoreFailed(string text)
    {
        LoadMoreText = text;
        LoadMoreFailed = true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Loading failed with {ExceptionType}")]
    private static partial void LogLoadFailed(ILogger logger, string exceptionType);
}
