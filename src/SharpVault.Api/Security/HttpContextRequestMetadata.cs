// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;

namespace SharpVault.Api.Security;

public sealed class HttpContextRequestMetadata(IHttpContextAccessor accessor, ClientAddressResolver resolver) : IRequestMetadata
{
    public string? IpAddress => accessor.HttpContext is { } context ? resolver.Resolve(context) : null;
}
