// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Users.Common;
using SharpVault.UI.Shared.Components.Users;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Contracts;

[Trait("Category", "Security")]
public sealed class AdminReasonRulesContractTests
{
    private const int Min = ReasonRules.MinLength;
    private const int Max = ReasonRules.MaxLength;

    public static TheoryData<string, string?> SharedReasonInputs()
    {
        var inputs = new TheoryData<string, string?>
        {
            { "null", null },
            { "empty", string.Empty },
            { "spaces only", new string(' ', 50) },
            { "min minus one", new string('a', Min - 1) },
            { "min exactly", new string('a', Min) },
            { "max exactly", new string('a', Max) },
            { "max plus one", new string('a', Max + 1) },
            { "padded minimum", "  " + new string('a', Min) + "  " },
            { "embedded line breaks and tabs", "First line of the reason\nSecond line\tdetail" },
            { "nul inside", new string('a', 10) + "\u0000" + new string('a', 10) },
            { "valid surrogate pair", new string('a', Min - 2) + "{HIGH}{LOW}" },
            { "lone high surrogate", new string('a', 10) + "{HIGH}" + new string('a', 10) },
            { "over max with nul", new string('a', Max + 1) + "\u0000" },
        };

        return inputs;
    }

    [Fact]
    public void Limits_MatchTheServerRules()
    {
        Assert.Equal(ReasonRules.MinLength, AdminReasonRules.MinLength);
        Assert.Equal(ReasonRules.MaxLength, AdminReasonRules.MaxLength);
    }

    [Theory]
    [MemberData(nameof(SharedReasonInputs))]
    public void IsValid_SameInput_AcceptsOrRejectsLikeTheServer(string label, string? template)
    {
        var reason = template is null ? null : ReasonText.Expand(template);
        var server = ServerReasonRules.Accepts(reason);
        var ui = AdminReasonRules.IsValid(reason);

        Assert.True(server == ui, $"{label}: the server accepts {server}, the UI accepts {ui}.");
    }
}
