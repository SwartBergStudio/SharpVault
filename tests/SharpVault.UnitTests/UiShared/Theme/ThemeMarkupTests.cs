// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using Bunit;
using SharpVault.UI.Shared.Components.Brand;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Icons;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed partial class ThemeMarkupTests : BunitContext
{
    public ThemeMarkupTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public static TheoryData<string> ComponentNames() =>
    [
        "Icon",
        "LabelledIcon",
        "VaultDoorHero",
        "VaultDoorSealing",
        "VaultDoorMark",
        "StatusBadge",
        "StatusBadgeCustom",
        "Alert",
        "EmptyState",
        "DialogShell",
        "DialogShellDanger",
        "SecretFrameHidden",
        "SecretFrameRevealed",
        "BusyIndicator",
    ];

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ComponentNames))]
    public void ThemeComponents_AnyRender_EmitNoStyleAttributeOrScript(string component)
    {
        var markup = RenderRepresentative(component);

        Assert.False(string.IsNullOrWhiteSpace(markup), $"{component} rendered nothing.");
        Assert.DoesNotMatch(StyleAttribute(), markup);
        Assert.DoesNotContain("<style", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(HandlerAttribute(), markup);
        Assert.DoesNotContain("javascript:", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(ExternalReference(), markup);
    }

    private string RenderRepresentative(string component) => component switch
    {
        "Icon" => Render<Icon>(p => p.Add(c => c.Name, IconName.Vault)).Markup,
        "LabelledIcon" => Render<Icon>(p => p.Add(c => c.Name, IconName.Key).Add(c => c.Label, "Key")).Markup,
        "VaultDoorHero" => Render<VaultDoor>().Markup,
        "VaultDoorSealing" => Render<VaultDoor>(p => p.Add(c => c.Sealing, true)).Markup,
        "VaultDoorMark" => Render<VaultDoor>(p => p.Add(c => c.Variant, VaultDoorVariant.Mark).Add(c => c.Id, "brand-mark")).Markup,
        "StatusBadge" => Render<StatusBadge>(p => p.Add(c => c.Status, SecurityStatus.Locked)).Markup,
        "StatusBadgeCustom" => Render<StatusBadge>(p => p
            .Add(c => c.Status, SecurityStatus.Warning)
            .Add(c => c.Label, "Lock scheduled")
            .Add(c => c.Icon, IconName.ScheduledLock)
            .Add(c => c.Id, "lock-status")).Markup,
        "Alert" => Render<Alert>(p => p
            .Add(c => c.Id, "alert-one")
            .Add(c => c.Kind, AlertKind.Danger)
            .Add(c => c.Title, "Title")
            .AddChildContent("<span>Body</span>")).Markup,
        "EmptyState" => Render<EmptyState>(p => p
            .Add(c => c.Id, "empty-one")
            .Add(c => c.Title, "Nothing")
            .Add(c => c.Actions, "<a id=\"go\" href=\"\">Go</a>")).Markup,
        "DialogShell" => Render<DialogShell>(p => p
            .Add(c => c.IdPrefix, "confirm")
            .Add(c => c.Title, "Confirm")
            .Add(c => c.Open, true)
            .Add(c => c.Footer, "<button id=\"confirm-ok\" type=\"button\">OK</button>")).Markup,
        "DialogShellDanger" => Render<DialogShell>(p => p
            .Add(c => c.IdPrefix, "confirm")
            .Add(c => c.Title, "Confirm")
            .Add(c => c.Open, true)
            .Add(c => c.Danger, true)).Markup,
        "SecretFrameHidden" => Render<SecretFrame>(p => p.Add(c => c.IdPrefix, "secret-value")).Markup,
        "SecretFrameRevealed" => Render<SecretFrame>(p => p
            .Add(c => c.IdPrefix, "secret-value")
            .Add(c => c.Revealed, true)
            .AddChildContent("value")
            .Add(c => c.Actions, "<button id=\"copy\" type=\"button\">Copy</button>")).Markup,
        "BusyIndicator" => Render<BusyIndicator>(p => p.Add(c => c.Id, "busy-one")).Markup,
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, "Unknown component."),
    };

    [GeneratedRegex("\\sstyle\\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex("\\son[a-z]+\\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HandlerAttribute();

    [GeneratedRegex("(src|href|action|xlink:href)\\s*=\\s*\"(https?:)?//", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExternalReference();
}
