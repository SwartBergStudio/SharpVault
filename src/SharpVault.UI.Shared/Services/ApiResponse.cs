// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services;

public sealed record ApiResponse<T>(ApiCallStatus Status, T? Value)
{
    public static ApiResponse<T> Success(T value) => new(ApiCallStatus.Success, value);

    public static ApiResponse<T> SignInRequired() => new(ApiCallStatus.SignInRequired, default);

    public static ApiResponse<T> AccessDenied() => new(ApiCallStatus.AccessDenied, default);

    public static ApiResponse<T> Failed() => new(ApiCallStatus.Failed, default);
}
