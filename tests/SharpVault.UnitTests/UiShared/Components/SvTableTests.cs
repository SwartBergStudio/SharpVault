// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvTableTests : MudTestContext
{
    private static readonly string[] Names = ["Ada", "Grace"];
    private static readonly string[] MarkupNames = ["<img src=x onerror=alert(1)>"];

    private static RenderFragment NameColumn(string header = "Name") => builder =>
    {
        builder.OpenComponent<SvColumn<string>>(0);
        builder.AddAttribute(1, nameof(SvColumn<string>.Header), header);
        builder.AddAttribute(2, nameof(SvColumn<string>.Value), (Func<string, string?>)(name => name));
        builder.CloseComponent();
    };

    private static RenderFragment TemplateColumn => builder =>
    {
        builder.OpenComponent<SvColumn<string>>(0);
        builder.AddAttribute(1, nameof(SvColumn<string>.Header), "Shout");
        builder.AddAttribute(2, nameof(SvColumn<string>.ChildContent), (RenderFragment<string>)(name => inner => inner.AddMarkupContent(0, $"<b class=\"shout\">{name.ToUpperInvariant()}</b>")));
        builder.CloseComponent();
    };

    private static RenderFragment TwoColumns => builder =>
    {
        NameColumn()(builder);
        TemplateColumn(builder);
    };

    [Fact]
    public void Table_WithItems_RendersHeaderAndOneRowPerItem()
    {
        var cut = Render<SvTable<string>>(parameters => parameters
            .Add(table => table.Id, "users-table")
            .Add(table => table.Items, Names)
            .Add(table => table.Columns, NameColumn()));

        Assert.Equal("Name", cut.Find("#users-table th").TextContent);
        Assert.Equal(Names, cut.FindAll("#users-table tbody td").Select(cell => cell.TextContent));
    }

    [Fact]
    public void Table_MixedColumns_RenderInDeclarationOrderWithTemplates()
    {
        var cut = Render<SvTable<string>>(parameters => parameters
            .Add(table => table.Id, "users-table")
            .Add(table => table.Items, Names)
            .Add(table => table.Columns, TwoColumns));

        Assert.Equal(["Name", "Shout"], cut.FindAll("#users-table th").Select(header => header.TextContent));
        Assert.Equal(["ADA", "GRACE"], cut.FindAll("#users-table .shout").Select(cell => cell.TextContent));
    }

    [Fact]
    public void Table_NoItems_RendersEmptyContent()
    {
        var cut = Render<SvTable<string>>(parameters => parameters
            .Add(table => table.Id, "users-table")
            .Add(table => table.Items, Array.Empty<string>())
            .Add(table => table.Columns, NameColumn())
            .Add(table => table.EmptyContent, "<p id=\"no-users\">No users</p>"));

        Assert.Equal("No users", cut.Find("#no-users").TextContent);
        Assert.Empty(cut.FindAll("#users-table tbody td[data-label]"));
    }

    [Fact]
    public void Table_Id_IsOnTheTable()
    {
        var cut = Render<SvTable<string>>(parameters => parameters
            .Add(table => table.Id, "users-table")
            .Add(table => table.Items, Names)
            .Add(table => table.Columns, NameColumn()));

        Assert.NotNull(cut.Find("#users-table"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Table_CellText_WithMarkup_IsEncoded()
    {
        var cut = Render<SvTable<string>>(parameters => parameters
            .Add(table => table.Id, "users-table")
            .Add(table => table.Items, MarkupNames)
            .Add(table => table.Columns, NameColumn()));

        Assert.Empty(cut.FindAll("img"));
        Assert.Contains("&lt;img", cut.Find("#users-table tbody td").InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Column_OutsideATable_Throws()
    {
        var exception = Record.Exception(() => Render<SvColumn<string>>(parameters => parameters.Add(column => column.Header, "Name")));

        Assert.IsType<InvalidOperationException>(exception?.GetBaseException());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    public void Table_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvTable<string>>(parameters => parameters.Add(table => table.Id, id)));
    }
}
