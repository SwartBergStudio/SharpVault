// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.Web.Security;

public sealed partial class ApiSharedSessionClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    ILogger<ApiSharedSessionClient> logger) : ISharedSessionClient
{
    private const string BearerScheme = "Bearer";
    private const long MaxUnixSeconds = 253402300799;

    public async Task<SharedSessionState> CheckAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!TryReadReference(principal, out var body))
        {
            LogMalformedClaims(logger);
            return SharedSessionState.Unknown;
        }

        try
        {
            var token = await tokenProvider.GetAccessTokenAsync(principal, cancellationToken).WaitAsync(cancellationToken);
            if (token is not { Kind: ApiAccessTokenKind.Bearer, Value: not null })
            {
                LogNoBearerToken(logger);
                return SharedSessionState.Unknown;
            }

            using var request = BuildRequest(HttpMethod.Post, ApiRoutes.MeSessionCheck, body, token.Value);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return await ReadCheckAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogCheckFailed(logger, exception.GetType().Name);
            return SharedSessionState.Unknown;
        }
    }

    public async Task RevokeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryReadReference(principal, out var body))
            {
                return;
            }

            var token = await tokenProvider.GetAccessTokenAsync(principal, cancellationToken).WaitAsync(cancellationToken);
            if (token is not { Kind: ApiAccessTokenKind.Bearer, Value: not null })
            {
                LogNoBearerToken(logger);
                return;
            }

            using var request = BuildRequest(HttpMethod.Post, ApiRoutes.MeSessionRevoke, body, token.Value);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRevokeStatus(logger, (int)response.StatusCode);
            }
        }
        catch (Exception exception)
        {
            LogRevokeFailed(logger, exception.GetType().Name);
        }
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string route, SessionReferenceRequestDto body, string bearer)
    {
        var request = new HttpRequestMessage(method, route.TrimStart('/'))
        {
            Content = JsonContent.Create(body, options: JsonSerializerOptions.Web),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, bearer);
        return request;
    }

    private async Task<SharedSessionState> ReadCheckAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return SharedSessionState.Forbidden;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            LogCheckStatus(logger, (int)response.StatusCode);
            return SharedSessionState.Unknown;
        }

        var status = await response.Content.ReadFromJsonAsync<SessionStatusDto>(JsonSerializerOptions.Web, cancellationToken);
        return status?.IsActive == true ? SharedSessionState.Active : SharedSessionState.Ended;
    }

    private static bool TryReadReference(ClaimsPrincipal principal, out SessionReferenceRequestDto body)
    {
        body = new SessionReferenceRequestDto();
        var sessionId = principal.FindFirst(SessionClaims.SessionId)?.Value;
        var expiry = principal.FindFirst(SessionClaims.ExpiresAt)?.Value;
        if (string.IsNullOrWhiteSpace(sessionId)
            || !long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds is < 0 or > MaxUnixSeconds)
        {
            return false;
        }

        body = new SessionReferenceRequestDto
        {
            SessionId = sessionId,
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds),
        };
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared session check skipped: the session claims are missing or malformed")]
    private static partial void LogMalformedClaims(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared session check skipped: no bearer token could be acquired")]
    private static partial void LogNoBearerToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared session check failed ({ExceptionType})")]
    private static partial void LogCheckFailed(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared session check returned status {StatusCode}")]
    private static partial void LogCheckStatus(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared session revoke returned status {StatusCode}")]
    private static partial void LogRevokeStatus(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared session revoke failed ({ExceptionType})")]
    private static partial void LogRevokeFailed(ILogger logger, string exceptionType);
}
