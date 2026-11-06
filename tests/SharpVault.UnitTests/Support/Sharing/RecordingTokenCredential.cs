// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Core;

namespace SharpVault.UnitTests.Support.Sharing;

internal sealed class RecordingTokenCredential(string token = "graph-token-value") : TokenCredential
{
    private readonly List<string[]> _scopes = [];
    private readonly List<CancellationToken> _cancellationTokens = [];

    public IReadOnlyList<string[]> Scopes => _scopes;

    public IReadOnlyList<CancellationToken> CancellationTokens => _cancellationTokens;

    public Exception? Failure { get; set; }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        Issue(requestContext, cancellationToken);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Issue(requestContext, cancellationToken));

    private AccessToken Issue(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        _scopes.Add(requestContext.Scopes);
        _cancellationTokens.Add(cancellationToken);
        if (Failure is not null)
        {
            throw Failure;
        }

        return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
    }
}
