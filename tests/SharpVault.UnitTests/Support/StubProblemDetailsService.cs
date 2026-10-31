// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http;

namespace SharpVault.UnitTests.Support;

internal sealed class StubProblemDetailsService : IProblemDetailsService
{
    private readonly List<ProblemDetailsContext> _written = [];

    public bool Result { get; set; } = true;

    public IReadOnlyList<ProblemDetailsContext> Written => _written;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        _written.Add(context);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
    {
        _written.Add(context);
        return ValueTask.FromResult(Result);
    }
}
