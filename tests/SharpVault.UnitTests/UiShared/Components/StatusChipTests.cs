// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class StatusChipTests : MudTestContext
{
    public static TheoryData<SecurityStatus> AllStatuses()
    {
        var data = new TheoryData<SecurityStatus>();
        foreach (var status in Enum.GetValues<SecurityStatus>())
        {
            data.Add(status);
        }

        return data;
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(AllStatuses))]
    public void StatusChip_EveryStatus_RendersItsTextLabel(SecurityStatus status)
    {
        var cut = Render<StatusChip>(parameters => parameters.Add(chip => chip.Status, status).Add(chip => chip.Id, "chip-one"));

        var root = cut.Find("#chip-one");
        Assert.Equal(status.ToString(), root.TextContent.Trim());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StatusChip_DefaultStatus_IsUnknownNotActive()
    {
        var cut = Render<StatusChip>();

        Assert.Contains("Unknown", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Active", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(SecurityStatus.Unknown, default(SecurityStatus));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StatusChip_BlankLabel_FallsBackToDefaultText(string? label)
    {
        var cut = Render<StatusChip>(parameters => parameters
            .Add(chip => chip.Status, SecurityStatus.Locked)
            .Add(chip => chip.Label, label));

        Assert.Contains("Locked", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusChip_CustomLabel_IsRendered()
    {
        var custom = Render<StatusChip>(parameters => parameters
            .Add(chip => chip.Status, SecurityStatus.Warning)
            .Add(chip => chip.Label, "Lock scheduled")
            .Add(chip => chip.Id, "lock-status"));

        Assert.Contains("Lock scheduled", custom.Find("#lock-status").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusChip_LabelWithMarkup_IsEncoded()
    {
        var cut = Render<StatusChip>(parameters => parameters.Add(chip => chip.Label, "<b onclick=x>bold</b>"));

        Assert.Empty(cut.FindAll("b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void StatusChip_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<StatusChip>(parameters => parameters.Add(chip => chip.Id, id)));
    }
}
