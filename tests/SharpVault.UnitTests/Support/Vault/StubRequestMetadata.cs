// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class StubRequestMetadata(string? ipAddress = "203.0.113.7") : IRequestMetadata
{
    public string? IpAddress { get; } = ipAddress;
}
