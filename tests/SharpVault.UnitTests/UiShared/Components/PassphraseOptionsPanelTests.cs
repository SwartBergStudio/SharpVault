// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using SharpVault.PasswordGeneration;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class PassphraseOptionsPanelTests : MudTestContext
{
    private const string SeparatorId = "generator-separator";

    private async Task<List<PassphraseOptions>> ChangeSeparatorAsync(string value)
    {
        var changes = new List<PassphraseOptions>();
        var cut = Render<PassphraseOptionsPanel>(parameters => parameters
            .Add(panel => panel.Options, PassphraseOptions.Default)
            .Add(panel => panel.OptionsChanged, EventCallback.Factory.Create<PassphraseOptions>(this, changes.Add)));
        await cut.Find($"#{SeparatorId}").ChangeAsync(new ChangeEventArgs { Value = value });
        return changes;
    }

    [Fact]
    public async Task SeparatorChanged_ExactName_RaisesTheChange()
    {
        var changes = await ChangeSeparatorAsync(nameof(PassphraseSeparator.Period));

        Assert.Equal(PassphraseSeparator.Period, Assert.Single(changes).Separator);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("Hyphen,Period")]
    [InlineData("Hyphen,Space")]
    [InlineData("99")]
    [InlineData("hyphen")]
    public async Task SeparatorChanged_NumericCombinedOrUnknownValue_RaisesNothing(string value)
    {
        var changes = await ChangeSeparatorAsync(value);

        Assert.Empty(changes);
    }
}
