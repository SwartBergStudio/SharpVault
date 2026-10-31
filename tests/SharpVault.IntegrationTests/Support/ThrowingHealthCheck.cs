// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SharpVault.IntegrationTests.Support;

public sealed class ThrowingHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(SensitiveMarkers.HealthCheckMessage);
}
