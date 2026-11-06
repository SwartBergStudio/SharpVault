// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services;

public static class ApiCallStatusExtensions
{
    public static bool FailsTheWholeView(this ApiCallStatus status) =>
        status is ApiCallStatus.SignInRequired or ApiCallStatus.AccessDenied or ApiCallStatus.ConfigurationMismatch;
}
