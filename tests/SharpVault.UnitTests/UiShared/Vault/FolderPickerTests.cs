// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class FolderPickerTests : MudTestContext
{
    private const string PickerId = "folder-picker";
    private const string Indent = "  ";

    private static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Child = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Grandchild = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid Sibling = Guid.Parse("00000000-0000-0000-0000-0000000000d4");

    private static FolderDto Folder(Guid id, Guid? parent, string? name, bool damaged = false) =>
        new() { Id = id, ParentId = parent, Name = name, IsDamaged = damaged };

    private static List<FolderDto> Tree() =>
    [
        Folder(Grandchild, Child, "Gamma"),
        Folder(Sibling, null, "alpha"),
        Folder(Root, null, "Bravo"),
        Folder(Child, Root, "Charlie"),
    ];

    private IRenderedComponent<FolderPicker> RenderPicker(List<FolderDto> folders, Action<ComponentParameterCollectionBuilder<FolderPicker>>? configure = null) =>
        Render<FolderPicker>(parameters =>
        {
            parameters
                .Add(picker => picker.Id, PickerId)
                .Add(picker => picker.Label, "Folder")
                .Add(picker => picker.Folders, folders);
            configure?.Invoke(parameters);
        });

    private static List<(string Value, string Text)> Options(IRenderedComponent<FolderPicker> cut) =>
        cut.FindAll($"#{PickerId} option").Select(option => (option.GetAttribute("value")!, option.TextContent)).ToList();

    [Fact]
    public void Render_IncludesNoFolderOptionFirst()
    {
        var cut = RenderPicker([]);

        var only = Assert.Single(Options(cut));
        Assert.Equal((string.Empty, "No folder"), only);
    }

    [Fact]
    public void Render_IndentsByDepth()
    {
        var cut = RenderPicker(Tree());

        Assert.Equal(
            [
                ("", "No folder"),
                (Sibling.ToString("D"), "alpha"),
                (Root.ToString("D"), "Bravo"),
                (Child.ToString("D"), Indent + "Charlie"),
                (Grandchild.ToString("D"), Indent + Indent + "Gamma"),
            ],
            Options(cut));
    }

    [Fact]
    public void ExcludeSubtreeOf_LeavesOutTheFolderAndDescendants()
    {
        var cut = RenderPicker(Tree(), parameters => parameters.Add(picker => picker.ExcludeSubtreeOf, Root));

        Assert.Equal(["", Sibling.ToString("D")], Options(cut).Select(option => option.Value));
    }

    [Fact]
    public void ExcludeSubtreeOf_MiddleFolder_KeepsItsAncestor()
    {
        var cut = RenderPicker(Tree(), parameters => parameters.Add(picker => picker.ExcludeSubtreeOf, Child));

        Assert.Equal(["", Sibling.ToString("D"), Root.ToString("D")], Options(cut).Select(option => option.Value));
    }

    [Fact]
    public void ExcludeSubtreeOf_CyclicData_Terminates()
    {
        var folders = new List<FolderDto> { Folder(Root, Child, "A"), Folder(Child, Root, "B"), Folder(Sibling, null, "C") };

        var cut = RenderPicker(folders, parameters => parameters.Add(picker => picker.ExcludeSubtreeOf, Root));

        Assert.Equal(["", Sibling.ToString("D")], Options(cut).Select(option => option.Value));
    }

    [Fact]
    public void Render_CyclicParents_TerminatesAndOmitsTheCycle()
    {
        var folders = new List<FolderDto> { Folder(Root, Child, "A"), Folder(Child, Root, "B"), Folder(Grandchild, Grandchild, "Self"), Folder(Sibling, null, "C") };

        var cut = RenderPicker(folders);

        Assert.Equal(["", Sibling.ToString("D")], Options(cut).Select(option => option.Value));
    }

    [Fact]
    public void Render_ParentMissingFromTheList_ShowsTheFolderAtRootLevel()
    {
        var orphan = Guid.NewGuid();
        var cut = RenderPicker([Folder(Root, orphan, "Orphan")]);

        Assert.Equal(("" + Root.ToString("D"), "Orphan"), Options(cut)[1]);
    }

    [Fact]
    public void Render_DamagedOrUnnamedFolder_ShowsUnreadableFolder()
    {
        var cut = RenderPicker([Folder(Root, null, "Real name", damaged: true), Folder(Child, null, null)]);

        Assert.All(Options(cut).Skip(1), option => Assert.Equal("Unreadable folder", option.Text));
        Assert.DoesNotContain("Real name", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Render_FolderNameWithMarkup_IsEncoded()
    {
        var cut = RenderPicker([Folder(Root, null, "<img src=x onerror=alert(1)>")]);

        Assert.Empty(cut.FindAll("img"));
        Assert.Contains("&lt;img", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Value_SelectsThatFolder()
    {
        var cut = RenderPicker(Tree(), parameters => parameters.Add(picker => picker.Value, Child));

        var selected = Assert.Single(cut.FindAll($"#{PickerId} option"), option => option.HasAttribute("selected"));
        Assert.Equal(Child.ToString("D"), selected.GetAttribute("value"));
    }

    [Fact]
    public void Change_RaisesTheGuidOrNull()
    {
        var raised = new List<Guid?>();
        var cut = RenderPicker(Tree(), parameters => parameters
            .Add(picker => picker.Value, Child)
            .Add(picker => picker.ValueChanged, value => raised.Add(value)));

        cut.Find($"#{PickerId}").Change(Grandchild.ToString("D"));
        cut.Find($"#{PickerId}").Change(string.Empty);
        cut.Find($"#{PickerId}").Change("not-a-guid");

        Assert.Equal([Grandchild, null, null], raised);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    public void InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<FolderPicker>(parameters => parameters
            .Add(picker => picker.Id, id)
            .Add(picker => picker.Label, "Folder")));
    }
}
