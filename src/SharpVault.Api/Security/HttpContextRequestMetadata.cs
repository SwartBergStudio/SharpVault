// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Users.Access;

namespace SharpVault.Api.Security;

public sealed class HttpContextRequestMetadata(IHttpContextAccessor accessor) : IRequestMetadata
{
    public string? IpAddress =>
        UserProfileText.NormalizeIpAddress(accessor.HttpContext?.Connection.RemoteIpAddress?.ToString());
}
