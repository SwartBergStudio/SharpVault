// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;

namespace SharpVault.UnitTests.Support;

internal sealed class StubGuestAccessPolicy : IGuestAccessPolicy
{
    private readonly List<CancellationToken> _tokens = [];

    public bool GuestsAllowed { get; set; }

    public IReadOnlyList<CancellationToken> Tokens => _tokens;

    public ValueTask<bool> AreGuestsAllowedAsync(CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        return ValueTask.FromResult(GuestsAllowed);
    }
}
