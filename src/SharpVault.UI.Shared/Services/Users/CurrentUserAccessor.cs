// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components.Authorization;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UI.Shared.Services.Users;

public sealed class CurrentUserAccessor(IMeApiClient client, AuthenticationStateProvider authenticationState) : ICurrentUserAccessor
{
    private readonly Lock _gate = new();
    private string? _key;
    private CurrentUserDto? _cached;
    private Task<ApiResponse<CurrentUserDto>>? _inFlight;

    public async Task<ApiResponse<CurrentUserDto>> GetAsync(CancellationToken cancellationToken)
    {
        var key = await CurrentKeyAsync();
        Task<ApiResponse<CurrentUserDto>> pending;
        TaskCompletionSource<ApiResponse<CurrentUserDto>>? started = null;
        lock (_gate)
        {
            if (!string.Equals(_key, key, StringComparison.Ordinal))
            {
                _key = key;
                _cached = null;
                _inFlight = null;
            }

            if (_cached is not null)
            {
                return ApiResponse<CurrentUserDto>.Success(_cached);
            }

            if (_inFlight is null)
            {
                started = new TaskCompletionSource<ApiResponse<CurrentUserDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight = started.Task;
            }

            pending = _inFlight;
        }

        if (started is not null)
        {
            _ = LoadAsync(started);
        }

        return await pending.WaitAsync(cancellationToken);
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
            _inFlight = null;
        }
    }

    private async Task<string> CurrentKeyAsync()
    {
        var state = await authenticationState.GetAuthenticationStateAsync();
        return state.User.FindFirst(SessionClaims.ObjectId)?.Value ?? string.Empty;
    }

    private async Task LoadAsync(TaskCompletionSource<ApiResponse<CurrentUserDto>> completion)
    {
        try
        {
            var response = await client.GetCurrentUserAsync(CancellationToken.None);
            Complete(completion, response);
            completion.SetResult(response);
        }
        catch (Exception exception)
        {
            Complete(completion, null);
            completion.SetException(exception);
        }
    }

    private void Complete(TaskCompletionSource<ApiResponse<CurrentUserDto>> completion, ApiResponse<CurrentUserDto>? response)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_inFlight, completion.Task))
            {
                _inFlight = null;
                if (response is { Status: ApiCallStatus.Success, Value: not null })
                {
                    _cached = response.Value;
                }
            }
        }
    }
}
