// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Audit;

namespace SharpVault.IntegrationTests.Support;

public sealed class ThrowingAuditLog : IAuditLog
{
    private const int ServiceUnavailable = 503;
    private const string Message = "Service unavailable";

    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken) =>
        throw new RequestFailedException(ServiceUnavailable, Message);
}
