// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class FakeAzureResponse(int status, string? etag) : Response
{
    public override int Status => status;

    public override string ReasonPhrase => string.Empty;

    public override Stream? ContentStream { get; set; }

    public override string ClientRequestId { get; set; } = string.Empty;

    public override void Dispose()
    {
    }

    protected override bool ContainsHeader(string name) =>
        etag is not null && string.Equals(name, "ETag", StringComparison.OrdinalIgnoreCase);

    protected override IEnumerable<HttpHeader> EnumerateHeaders() =>
        etag is null ? [] : [new HttpHeader("ETag", etag)];

    protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
    {
        value = string.Equals(name, "ETag", StringComparison.OrdinalIgnoreCase) ? etag : null;
        return value is not null;
    }

    protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        values = TryGetHeader(name, out var value) ? [value] : null;
        return values is not null;
    }
}
