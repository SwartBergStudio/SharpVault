// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;

namespace SharpVault.IntegrationTests.Support;

public sealed class ApiOpenFault
{
    public HttpStatusCode? Status { get; set; }

    public TimeSpan? RetryAfter { get; set; }
}
