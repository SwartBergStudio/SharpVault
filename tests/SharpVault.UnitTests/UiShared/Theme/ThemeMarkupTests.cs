// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using Bunit;
using SharpVault.UI.Shared.Components.Brand;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Icons;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed partial class ThemeMarkupTests : MudTestContext
{
    private static readonly string[] MudRendered =
    [
        "StatusChip",
        "StatusChipCustom",
        "SvAlert",
        "EmptyState",
        "SecretField",
        "SecretFieldRevealed",
        "BusyIndicator",
        "StatePanel",
        "SvButton",
        "SvIconButton",
    ];

    public static TheoryData<string> ComponentNames() =>
    [
        "VaultDoorHero",
        "VaultDoorSealing",
        "VaultDoorMark",
        "StatusChip",
        "StatusChipCustom",
        "SvAlert",
        "EmptyState",
        "SecretField",
        "SecretFieldRevealed",
        "BusyIndicator",
        "StatePanel",
        "SvButton",
        "SvIconButton",
    ];

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ComponentNames))]
    public void ThemeComponents_AnyRender_EmitNoStyleAttributeOrScript(string component)
    {
        var markup = RenderRepresentative(component);

        Assert.False(string.IsNullOrWhiteSpace(markup), $"{component} rendered nothing.");
        if (!MudRendered.Contains(component))
        {
            Assert.DoesNotMatch(StyleAttribute(), markup);
        }

        Assert.DoesNotContain("<style", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(HandlerAttribute(), markup);
        Assert.DoesNotContain("javascript:", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(ExternalReference(), markup);
    }

    private string RenderRepresentative(string component) => component switch
    {
        "VaultDoorHero" => Render<VaultDoor>().Markup,
        "VaultDoorSealing" => Render<VaultDoor>(p => p.Add(c => c.Sealing, true)).Markup,
        "VaultDoorMark" => Render<VaultDoor>(p => p.Add(c => c.Variant, VaultDoorVariant.Mark).Add(c => c.Id, "brand-mark")).Markup,
        "StatusChip" => Render<StatusChip>(p => p.Add(c => c.Status, SecurityStatus.Locked)).Markup,
        "StatusChipCustom" => Render<StatusChip>(p => p
            .Add(c => c.Status, SecurityStatus.Warning)
            .Add(c => c.Label, "Lock scheduled")
            .Add(c => c.Icon, SvIcons.Lock)
            .Add(c => c.Id, "lock-status")).Markup,
        "SvAlert" => Render<SvAlert>(p => p
            .Add(c => c.Id, "alert-one")
            .Add(c => c.Kind, AlertKind.Danger)
            .Add(c => c.Title, "Title")
            .AddChildContent("<span>Body</span>")).Markup,
        "EmptyState" => Render<EmptyState>(p => p
            .Add(c => c.Id, "empty-one")
            .Add(c => c.Title, "Nothing")
            .Add(c => c.Actions, "<a id=\"go\" href=\"\">Go</a>")).Markup,
        "SecretField" => Render<SecretField>(p => p.Add(c => c.IdPrefix, "secret-value")).Markup,
        "SecretFieldRevealed" => Render<SecretField>(p => p
            .Add(c => c.IdPrefix, "secret-value")
            .Add(c => c.Revealed, true)
            .AddChildContent("value")
            .Add(c => c.Actions, "<button id=\"copy\" type=\"button\">Copy</button>")).Markup,
        "BusyIndicator" => Render<BusyIndicator>(p => p.Add(c => c.Id, "busy-one")).Markup,
        "StatePanel" => Render<StatePanel>(p => p.Add(c => c.Id, "panel-one").Add(c => c.Title, "Title").AddChildContent("Body")).Markup,
        "SvButton" => Render<SvButton>(p => p.Add(c => c.Id, "button-one").AddChildContent("Go")).Markup,
        "SvIconButton" => Render<SvIconButton>(p => p.Add(c => c.Id, "icon-one").Add(c => c.Icon, SvIcons.Menu).Add(c => c.Label, "Menu")).Markup,
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, "Unknown component."),
    };

    [GeneratedRegex("\\sstyle\\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex("\\son[a-z]+\\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HandlerAttribute();

    [GeneratedRegex("(src|href|action|xlink:href)\\s*=\\s*\"(https?:)?//", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExternalReference();
}
