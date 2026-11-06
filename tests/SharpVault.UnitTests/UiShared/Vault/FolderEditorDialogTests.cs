// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using MudBlazor;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class FolderEditorDialogTests : MudTestContext
{
    private static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Child = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Sibling = Guid.Parse("00000000-0000-0000-0000-0000000000d4");

    private readonly IRenderedComponent<MudDialogProvider> _provider;
    private readonly List<FolderEditorInput> _saved = [];
    private int _cancelled;

    public FolderEditorDialogTests()
    {
        _provider = RenderDialogProviders();
    }

    private static List<FolderDto> Folders() =>
    [
        new() { Id = Root, Name = "Root" },
        new() { Id = Child, ParentId = Root, Name = "Child" },
        new() { Id = Sibling, Name = "Sibling" },
    ];

    private IRenderedComponent<FolderEditorDialog> RenderDialog(
        FolderEditorMode mode = FolderEditorMode.Create,
        bool open = true,
        string initialName = "",
        Guid? initialParent = null,
        Guid? excludeSubtreeOf = null,
        bool busy = false,
        string? errorText = null) =>
        Render<FolderEditorDialog>(parameters =>
        {
            parameters
                .Add(dialog => dialog.Open, open)
                .Add(dialog => dialog.Mode, mode)
                .Add(dialog => dialog.InitialName, initialName)
                .Add(dialog => dialog.InitialParentId, initialParent)
                .Add(dialog => dialog.Folders, Folders())
                .Add(dialog => dialog.ExcludeSubtreeOf, excludeSubtreeOf)
                .Add(dialog => dialog.Busy, busy)
                .Add(dialog => dialog.OnSave, input => _saved.Add(input))
                .Add(dialog => dialog.OnCancel, () => _cancelled++);
            if (errorText is not null)
            {
                parameters.Add(dialog => dialog.ErrorText, errorText);
            }
        });

    private void TypeName(string name) => _provider.Find("#folder-editor-name-input").Input(name);

    private bool SaveDisabled() => _provider.Find("#folder-editor-save").HasAttribute("disabled");

    [Fact]
    [Trait("Category", "Security")]
    public void Save_EmptyName_ShowsErrorWithoutRaisingSave()
    {
        RenderDialog();

        Assert.True(SaveDisabled());
        _provider.Find("#folder-editor-save").Click();

        Assert.Empty(_saved);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task Save_WhitespaceName_StaysDisabledAndRaisesNothing(string name)
    {
        RenderDialog();

        TypeName(name);

        _provider.WaitForAssertion(() => Assert.True(SaveDisabled()));
        await _provider.Find("#folder-editor-save").ClickAsync();
        Assert.Empty(_saved);
    }

    [Fact]
    public void Save_RaisesWithNameAndParent()
    {
        RenderDialog(initialParent: Root);

        TypeName("Payroll");
        _provider.WaitForAssertion(() => Assert.False(SaveDisabled()));
        _provider.Find("#folder-editor-save").Click();

        Assert.Equal([new FolderEditorInput("Payroll", Root)], _saved);
    }

    [Fact]
    public void Name_InputHasTheFolderNameMaxLength()
    {
        RenderDialog();

        Assert.Equal(VaultFieldRules.FolderNameMaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture), _provider.Find("#folder-editor-name-input").GetAttribute("maxlength"));
        Assert.Empty(_provider.FindAll("#folder-editor-parent-select"));
    }

    [Fact]
    public void Rename_PrefillsTheNameAndSavesTheEditedValue()
    {
        RenderDialog(FolderEditorMode.Rename, initialName: "Old name");

        Assert.Equal("Old name", _provider.Find("#folder-editor-name-input").GetAttribute("value"));
        Assert.Equal("Rename", _provider.Find("#folder-editor-save").TextContent.Trim());
        TypeName("New name");
        _provider.Find("#folder-editor-save").Click();

        Assert.Equal("New name", Assert.Single(_saved).Name);
    }

    [Fact]
    public void Move_ShowsPickerWithoutNameInputAndExcludesTheMovedSubtree()
    {
        RenderDialog(FolderEditorMode.Move, initialParent: Root, excludeSubtreeOf: Root);

        Assert.Empty(_provider.FindAll("#folder-editor-name-input"));
        var values = _provider.FindAll("#folder-editor-parent-select option").Select(option => option.GetAttribute("value")).ToList();
        Assert.Equal(["", Sibling.ToString("D")], values);
        Assert.Equal("Move", _provider.Find("#folder-editor-save").TextContent.Trim());
    }

    [Fact]
    public void Move_ChoosingAnotherParent_RaisesItsId()
    {
        RenderDialog(FolderEditorMode.Move, initialParent: Root, excludeSubtreeOf: Child);

        _provider.Find("#folder-editor-parent-select").Change(Sibling.ToString("D"));
        _provider.Find("#folder-editor-save").Click();

        Assert.Equal([new FolderEditorInput(string.Empty, Sibling)], _saved);
    }

    [Fact]
    public void Move_ChoosingNoFolder_RaisesNullParent()
    {
        RenderDialog(FolderEditorMode.Move, initialParent: Root, excludeSubtreeOf: Child);

        _provider.Find("#folder-editor-parent-select").Change(string.Empty);
        _provider.Find("#folder-editor-save").Click();

        Assert.Null(Assert.Single(_saved).ParentId);
    }

    [Fact]
    public void Move_PreselectsTheCurrentParent()
    {
        RenderDialog(FolderEditorMode.Move, initialParent: Sibling, excludeSubtreeOf: Child);

        _provider.Find("#folder-editor-save").Click();

        Assert.Equal(Sibling, Assert.Single(_saved).ParentId);
    }

    [Fact]
    public async Task Reopen_ReseedsTheFieldsFromTheInitialValues()
    {
        var cut = RenderDialog(FolderEditorMode.Rename, initialName: "First");
        TypeName("Typed but cancelled");

        cut.Render(parameters => parameters.Add(dialog => dialog.Open, false));
        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#folder-editor-dialog")));
        await WaitForClosedDialogsToSettleAsync();
        cut.Render(parameters => parameters
            .Add(dialog => dialog.Open, true)
            .Add(dialog => dialog.InitialName, "Second"));

        Assert.Equal("Second", _provider.WaitForElement("#folder-editor-name-input").GetAttribute("value"));
    }

    [Fact]
    public async Task Reopen_CreateAfterTyping_StartsEmptyAgain()
    {
        var cut = RenderDialog();
        TypeName("Leftover");

        cut.Render(parameters => parameters.Add(dialog => dialog.Open, false));
        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#folder-editor-dialog")));
        await WaitForClosedDialogsToSettleAsync();
        cut.Render(parameters => parameters.Add(dialog => dialog.Open, true));

        _provider.WaitForElement("#folder-editor-name-input");
        Assert.True(SaveDisabled());
        Assert.Equal(string.Empty, _provider.Find("#folder-editor-name-input").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void ParameterChangeWhileOpen_DoesNotOverwriteWhatWasTyped()
    {
        var cut = RenderDialog(FolderEditorMode.Rename, initialName: "First");
        TypeName("Typing in progress");

        cut.Render(parameters => parameters.Add(dialog => dialog.Busy, true));

        Assert.Equal("Typing in progress", _provider.Find("#folder-editor-name-input").GetAttribute("value"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void NameWithMarkup_RendersEncoded()
    {
        const string hostile = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        RenderDialog(FolderEditorMode.Rename, initialName: hostile);

        Assert.Empty(_provider.FindAll("img"));
        Assert.Empty(_provider.FindAll("script"));
        Assert.Empty(_provider.FindAll("[onerror]"));
        Assert.Equal(hostile, _provider.Find("#folder-editor-name-input").GetAttribute("value"));
    }

    [Fact]
    public void Busy_DisablesSaveAndCancel()
    {
        RenderDialog(FolderEditorMode.Rename, initialName: "Name", busy: true);

        Assert.True(SaveDisabled());
        Assert.True(_provider.Find("#folder-editor-cancel").HasAttribute("disabled"));
    }

    [Fact]
    public void Cancel_RaisesOnCancelWithoutSaving()
    {
        RenderDialog();

        _provider.Find("#folder-editor-cancel").Click();

        Assert.Equal(1, _cancelled);
        Assert.Empty(_saved);
    }

    [Fact]
    public void ErrorText_IsShownInTheDialog()
    {
        RenderDialog(errorText: "A folder with that name exists.");

        var error = _provider.Find("#folder-editor-error");
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Contains("A folder with that name exists.", error.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Closed_RendersNothing()
    {
        RenderDialog(open: false);

        Assert.Empty(_provider.FindAll("#folder-editor-dialog"));
    }
}
