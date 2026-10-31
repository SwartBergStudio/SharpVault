// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Icons;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class StatusBadgeTests : BunitContext
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
    public void StatusBadge_EveryStatus_RendersIconAndTextLabel(SecurityStatus status)
    {
        var cut = Render<StatusBadge>(parameters => parameters.Add(badge => badge.Status, status));

        var root = cut.Find("span.sv-status");
        Assert.Contains($"sv-status--{status.ToString().ToLowerInvariant()}", root.ClassList);
        Assert.NotNull(root.QuerySelector("svg.sv-icon"));
        var label = cut.Find(".sv-status__label");
        Assert.False(string.IsNullOrWhiteSpace(label.TextContent));
        Assert.Equal(status.ToString(), label.TextContent.Trim());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StatusBadge_Statuses_HaveDistinctIcons()
    {
        var icons = Enum.GetValues<SecurityStatus>()
            .Select(status => Render<StatusBadge>(parameters => parameters.Add(badge => badge.Status, status))
                .Find("svg.sv-icon").InnerHtml)
            .ToList();

        for (var first = 0; first < icons.Count; first++)
        {
            for (var second = first + 1; second < icons.Count; second++)
            {
                Assert.NotEqual(icons[first], icons[second]);
            }
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StatusBadge_DefaultStatus_IsUnknownNotActive()
    {
        var cut = Render<StatusBadge>();

        Assert.Equal("Unknown", cut.Find(".sv-status__label").TextContent.Trim());
        Assert.Contains("sv-status--unknown", cut.Find("span.sv-status").ClassList);
        Assert.DoesNotContain("sv-status--active", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Active", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(SecurityStatus.Unknown, default(SecurityStatus));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StatusBadge_BlankLabel_FallsBackToDefaultText(string? label)
    {
        var cut = Render<StatusBadge>(parameters => parameters
            .Add(badge => badge.Status, SecurityStatus.Locked)
            .Add(badge => badge.Label, label));

        Assert.Equal("Locked", cut.Find(".sv-status__label").TextContent.Trim());
    }

    [Fact]
    public void StatusBadge_CustomLabelAndIcon_AreRendered()
    {
        var withDefaults = Render<StatusBadge>(parameters => parameters.Add(badge => badge.Status, SecurityStatus.Warning));
        var defaultIcon = withDefaults.Find("svg.sv-icon").InnerHtml;

        var cut = Render<StatusBadge>(parameters => parameters
            .Add(badge => badge.Status, SecurityStatus.Warning)
            .Add(badge => badge.Label, "Lock scheduled")
            .Add(badge => badge.Icon, IconName.ScheduledLock));

        Assert.Equal("Lock scheduled", cut.Find(".sv-status__label").TextContent.Trim());
        Assert.Contains("sv-status--warning", cut.Find("span.sv-status").ClassList);
        Assert.NotEqual(defaultIcon, cut.Find("svg.sv-icon").InnerHtml);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StatusBadge_LabelWithMarkup_IsEncoded()
    {
        var cut = Render<StatusBadge>(parameters => parameters
            .Add(badge => badge.Status, SecurityStatus.Danger)
            .Add(badge => badge.Label, "<img src=x onerror=alert(1)>"));

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("<img src=x onerror=alert(1)>", cut.Find(".sv-status__label").TextContent);
    }

    [Fact]
    public void StatusBadge_ValidId_IsRendered()
    {
        var cut = Render<StatusBadge>(parameters => parameters.Add(badge => badge.Id, "user-lock-status"));

        Assert.Equal("user-lock-status", cut.Find("span.sv-status").GetAttribute("id"));
    }

    [Fact]
    public void StatusBadge_WithoutId_RendersNoIdAttribute()
    {
        var cut = Render<StatusBadge>();

        Assert.False(cut.Find("span.sv-status").HasAttribute("id"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void StatusBadge_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<StatusBadge>(parameters => parameters.Add(badge => badge.Id, id)));
    }
}
