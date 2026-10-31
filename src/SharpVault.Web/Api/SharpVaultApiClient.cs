// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using SharpVault.UI.Shared.Models;
using SharpVault.UI.Shared.Services;

namespace SharpVault.Web.Api;

public sealed partial class SharpVaultApiClient(
    HttpClient httpClient,
    ITokenAcquisition tokenAcquisition,
    AuthenticationStateProvider authenticationStateProvider,
    IOptions<SharpVaultApiOptions> options,
    ILogger<SharpVaultApiClient> logger) : ISharpVaultApiClient
{
    private const string CurrentUserPath = "api/me";
    private const string BearerScheme = "Bearer";

    public async Task<ApiResponse<CurrentUserProfile>> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return ApiResponse<CurrentUserProfile>.SignInRequired();
        }

        string token;
        try
        {
            token = await tokenAcquisition.GetAccessTokenForUserAsync(
                options.Value.Scopes,
                authenticationScheme: null,
                tenantId: null,
                userFlow: null,
                user: principal,
                tokenAcquisitionOptions: null);
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            LogSignInRequired(logger);
            return ApiResponse<CurrentUserProfile>.SignInRequired();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, CurrentUserPath);
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var profile = await response.Content.ReadFromJsonAsync<CurrentUserProfile>(cancellationToken);
                    return !IsValidProfile(profile)
                        ? ApiResponse<CurrentUserProfile>.Failed()
                        : ApiResponse<CurrentUserProfile>.Success(profile!);
                case HttpStatusCode.Unauthorized:
                    return ApiResponse<CurrentUserProfile>.SignInRequired();
                case HttpStatusCode.Forbidden:
                    return ApiResponse<CurrentUserProfile>.AccessDenied();
                default:
                    LogApiCallFailed(logger, (int)response.StatusCode);
                    return ApiResponse<CurrentUserProfile>.Failed();
            }
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogApiCallThrew(logger, exception.GetType().Name);
            return ApiResponse<CurrentUserProfile>.Failed();
        }
    }

    private static bool IsValidProfile(CurrentUserProfile? profile) =>
        profile is not null && profile.UserId != Guid.Empty && !string.IsNullOrWhiteSpace(profile.DisplayName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Interactive sign-in is required to call the API")]
    private static partial void LogSignInRequired(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API call failed with status {StatusCode}")]
    private static partial void LogApiCallFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API call failed with {ExceptionType}")]
    private static partial void LogApiCallThrew(ILogger logger, string exceptionType);
}
