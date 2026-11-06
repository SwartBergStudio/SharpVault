// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvTableRuntimeCellsTests : VaultTestContext
{
    private const string Secret = "cell-secret-value";

    private static readonly string[] Replaced = ["one", "three"];

    private readonly List<string> _clicked = [];
    private readonly List<string> _revealed = [];

    private RenderFragment Columns => builder =>
    {
        builder.OpenComponent<SvColumn<string>>(0);
        builder.AddAttribute(1, nameof(SvColumn<string>.Header), "Value");
        builder.AddAttribute(2, nameof(SvColumn<string>.ChildContent), (RenderFragment<string>)(row => ValueCell(row)));
        builder.CloseComponent();
        builder.OpenComponent<SvColumn<string>>(3);
        builder.AddAttribute(4, nameof(SvColumn<string>.Header), "Actions");
        builder.AddAttribute(5, nameof(SvColumn<string>.ChildContent), (RenderFragment<string>)(row => ActionCell(row)));
        builder.CloseComponent();
    };

    private RenderFragment ValueCell(string row) => builder =>
    {
        builder.OpenComponent<RevealableField>(0);
        builder.AddAttribute(1, nameof(RevealableField.IdPrefix), $"cell-value-{row}");
        builder.AddAttribute(2, nameof(RevealableField.Label), "Value");
        builder.AddAttribute(3, nameof(RevealableField.Reveal), (Func<RevealIntentDto, CancellationToken, Task<ApiResponse<RevealedValueDto>>>)((_, _) =>
        {
            _revealed.Add(row);
            return Task.FromResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = Secret + row }));
        }));
        builder.CloseComponent();
    };

    private RenderFragment ActionCell(string row) => builder =>
    {
        builder.OpenComponent<SvButton>(0);
        builder.AddAttribute(1, nameof(SvButton.Id), $"cell-button-{row}");
        builder.AddAttribute(2, nameof(SvButton.OnClick), EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(this, () => _clicked.Add(row)));
        builder.AddAttribute(3, nameof(SvButton.ChildContent), (RenderFragment)(inner => inner.AddContent(0, "Go")));
        builder.CloseComponent();
    };

    private IRenderedComponent<SvTable<string>> RenderTable(params string[] rows) =>
        Render<SvTable<string>>(parameters => parameters
            .Add(table => table.Id, "cell-table")
            .Add(table => table.Items, rows)
            .Add(table => table.Columns, Columns));

    [Fact]
    public void Table_CellsHoldingRevealableFieldAndButton_RenderAfterTheFirstPass()
    {
        var cut = RenderTable("one", "two");

        Assert.Equal(2, cut.FindAll("#cell-table tbody tr").Count);
        Assert.NotNull(cut.Find("#cell-value-one-reveal"));
        Assert.NotNull(cut.Find("#cell-value-two-reveal"));
        Assert.NotNull(cut.Find("#cell-button-one"));
        Assert.NotNull(cut.Find("#cell-button-two"));
    }

    [Fact]
    public void Table_ButtonInCell_ClickReachesTheRowHandler()
    {
        var cut = RenderTable("one", "two");

        cut.Find("#cell-button-two").Click();

        Assert.Equal(["two"], _clicked);
    }

    [Fact]
    public void Table_RevealInCell_RevealsOnlyThatRowAndHideWorks()
    {
        var cut = RenderTable("one", "two");

        cut.Find("#cell-value-two-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains(Secret + "two", cut.Find("#cell-value-two").TextContent, StringComparison.Ordinal));
        Assert.Equal(["two"], _revealed);
        Assert.Equal("hidden", cut.Find("#cell-value-one-frame").GetAttribute("data-state"));
        cut.Find("#cell-value-two-hide").Click();
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_ItemsReplaced_NewRowsKeepWorkingControls()
    {
        var cut = RenderTable("one");

        cut.Render(parameters => parameters
            .Add(table => table.Id, "cell-table")
            .Add(table => table.Items, Replaced)
            .Add(table => table.Columns, Columns));

        cut.Find("#cell-button-three").Click();
        cut.Find("#cell-value-three-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(Secret + "three", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(["three"], _clicked);
    }
}
