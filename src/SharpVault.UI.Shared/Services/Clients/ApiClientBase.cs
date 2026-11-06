// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos.Common;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public abstract partial class ApiClientBase(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger logger)
{
    private const string BearerScheme = "Bearer";

    private const string IfMatchHeader = "If-Match";
    private const char UnboundPlaceholderMarker = '{';
    private const char RouteSeparator = '/';
    private const char BackslashSeparator = '\\';

    protected Task<ApiResponse<T>> GetAsync<T>(string route, CancellationToken cancellationToken) =>
        SendAsync<T>(HttpMethod.Get, route, null, null, cancellationToken);

    protected Task<ApiResponse<TResponse>> PostAsync<TBody, TResponse>(string route, TBody body, string? ifMatch, CancellationToken cancellationToken) =>
        SendAsync<TResponse>(HttpMethod.Post, route, JsonContent.Create(body, options: JsonSerializerOptions.Web), ifMatch, cancellationToken);

    protected Task<ApiResponse<TResponse>> PostAsync<TResponse>(string route, string? ifMatch, CancellationToken cancellationToken) =>
        SendAsync<TResponse>(HttpMethod.Post, route, null, ifMatch, cancellationToken);

    protected Task<ApiResponse<TResponse>> PutAsync<TBody, TResponse>(string route, TBody body, string? ifMatch, CancellationToken cancellationToken) =>
        SendAsync<TResponse>(HttpMethod.Put, route, JsonContent.Create(body, options: JsonSerializerOptions.Web), ifMatch, cancellationToken);

    protected Task<ApiResponse<bool>> DeleteAsync(string route, string? ifMatch, CancellationToken cancellationToken) =>
        SendAsync<bool>(HttpMethod.Delete, route, null, ifMatch, cancellationToken);

    private async Task<ApiResponse<T>> SendAsync<T>(
        HttpMethod method,
        string route,
        HttpContent? content,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        using var ownedContent = content;
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        if (!IsSameHostRoute(route))
        {
            throw new ArgumentException("The route must be a relative path with a single leading slash.", nameof(route));
        }

        if (route.Contains(UnboundPlaceholderMarker, StringComparison.Ordinal))
        {
            throw new ArgumentException("The route still has an unbound placeholder.", nameof(route));
        }

        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true || !await sessionValidator.IsActiveAsync(principal, cancellationToken))
        {
            return ApiResponse<T>.SignInRequired();
        }

        ApiAccessToken token;
        try
        {
            token = await tokenProvider.GetAccessTokenAsync(principal, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogApiCallThrew(logger, exception.GetType().Name);
            return ApiResponse<T>.Failed();
        }

        if (token.Kind == ApiAccessTokenKind.SignInRequired)
        {
            LogSignInRequired(logger);
            return ApiResponse<T>.SignInRequired();
        }

        using var request = new HttpRequestMessage(method, route.TrimStart('/')) { Content = content };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(IfMatchHeader, ifMatch);
        }

        if (token is { Kind: ApiAccessTokenKind.Bearer, Value: not null })
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token.Value);
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return await ReadAsync<T>(response, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogApiCallThrew(logger, exception.GetType().Name);
            return ApiResponse<T>.Failed();
        }
    }

    private static bool IsSameHostRoute(string route) =>
        route[0] == RouteSeparator
        && (route.Length == 1 || route[1] != RouteSeparator)
        && !route.Any(character => character == BackslashSeparator || char.IsControl(character));

    protected virtual bool IsValid<T>(T value) => true;

    private async Task<ApiResponse<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return await ReadBodyAsync<T>(response, cancellationToken);
            case HttpStatusCode.NoContent when typeof(T) == typeof(bool):
                return ApiResponse<T>.Success((T)(object)true);
            case HttpStatusCode.Created:
                return await ReadBodyAsync<T>(response, cancellationToken);
            case HttpStatusCode.BadRequest:
                return ApiResponse<T>.Invalid(await ReadProblemDetailAsync(response, cancellationToken));
            case HttpStatusCode.NotFound:
                return ApiResponse<T>.NotFound();
            case HttpStatusCode.Conflict:
                return ApiResponse<T>.Conflict();
            case HttpStatusCode.TooManyRequests:
                return ApiResponse<T>.RateLimited();
            case HttpStatusCode.Unauthorized:
                if (tokenProvider.InteractiveSignInAvailable)
                {
                    return ApiResponse<T>.SignInRequired();
                }

                LogLocalAccountRejected(logger);
                return ApiResponse<T>.ConfigurationMismatch();
            case HttpStatusCode.Forbidden:
                return ApiResponse<T>.AccessDenied();
            default:
                LogApiCallFailed(logger, (int)response.StatusCode);
                return ApiResponse<T>.Failed();
        }
    }

    private async Task<ApiResponse<T>> ReadBodyAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, cancellationToken);
        return value is null || !IsValid(value) ? ApiResponse<T>.Failed() : ApiResponse<T>.Success(value);
    }

    private static async Task<string?> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDto>(JsonSerializerOptions.Web, cancellationToken);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }

            return problem?.Errors?.Values.SelectMany(messages => messages ?? []).FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Interactive sign-in is required to call the API")]
    private static partial void LogSignInRequired(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The API rejected the local account token. Check that SharpVault.Api runs in local mode and shares the local signing key.")]
    private static partial void LogLocalAccountRejected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API call failed with status {StatusCode}")]
    private static partial void LogApiCallFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API call failed with {ExceptionType}")]
    private static partial void LogApiCallThrew(ILogger logger, string exceptionType);
}
