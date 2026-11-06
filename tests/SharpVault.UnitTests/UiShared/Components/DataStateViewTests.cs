// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class DataStateViewTests : MudTestContext
{
    private const string Prefix = "users";

    private static RenderFragment<string> Content => value => builder => builder.AddMarkupContent(0, $"<p id=\"loaded-value\">{value}</p>");

    private IRenderedComponent<DataStateView<string>> RenderView(LoadState state, Action<ComponentParameterCollectionBuilder<DataStateView<string>>>? extra = null) =>
        Render<DataStateView<string>>(parameters =>
        {
            parameters
                .Add(view => view.IdPrefix, Prefix)
                .Add(view => view.State, state)
                .Add(view => view.Value, "alpha")
                .Add(view => view.ErrorText, "It broke")
                .Add(view => view.ChildContent, Content);
            extra?.Invoke(parameters);
        });

    [Fact]
    public void Loading_Default_RendersBusyIndicator()
    {
        var cut = RenderView(LoadState.Loading);

        Assert.Equal("status", cut.Find($"#{Prefix}-loading").GetAttribute("role"));
        Assert.Empty(cut.FindAll("#loaded-value"));
    }

    [Fact]
    public void Empty_Default_RendersEmptyStatePanel()
    {
        var cut = RenderView(LoadState.Empty);

        Assert.NotNull(cut.Find($"#{Prefix}-empty"));
        Assert.Equal("Nothing here yet", cut.Find($"#{Prefix}-empty-title").TextContent);
        Assert.Empty(cut.FindAll("#loaded-value"));
    }

    [Fact]
    public void Empty_WithDescription_PassesItToTheEmptyState()
    {
        var cut = RenderView(LoadState.Empty, parameters => parameters.Add(view => view.EmptyDescription, "Do this next."));

        Assert.Equal("Do this next.", cut.Find($"#{Prefix}-empty-description").TextContent);
    }

    [Fact]
    public void Empty_WithoutDescription_RendersNoDescription()
    {
        var cut = RenderView(LoadState.Empty);

        Assert.Empty(cut.FindAll($"#{Prefix}-empty-description"));
    }

    [Fact]
    public void Error_Default_RendersAlertTextAndRetryButton()
    {
        var cut = RenderView(LoadState.Error);

        Assert.Equal("alert", cut.Find($"#{Prefix}-error").GetAttribute("role"));
        Assert.Contains("It broke", cut.Find($"#{Prefix}-error").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#{Prefix}-retry"));
        Assert.Empty(cut.FindAll("#loaded-value"));
    }

    [Fact]
    public void Loaded_Always_RendersChildContentWithTheValue()
    {
        var cut = RenderView(LoadState.Loaded);

        Assert.Equal("alpha", cut.Find("#loaded-value").TextContent);
        Assert.Empty(cut.FindAll($"#{Prefix}-loading"));
        Assert.Empty(cut.FindAll($"#{Prefix}-error"));
    }

    [Fact]
    public void Error_Retry_RaisesCallback()
    {
        var retried = 0;

        var cut = RenderView(LoadState.Error, parameters => parameters.Add(view => view.OnRetry, EventCallback.Factory.Create(this, () => retried++)));
        cut.Find($"#{Prefix}-retry").Click();

        Assert.Equal(1, retried);
    }

    [Fact]
    public void Overrides_Replace_Defaults()
    {
        var loading = RenderView(LoadState.Loading, p => p.Add(v => v.Loading, "<i id=\"custom-loading\">wait</i>"));
        var empty = RenderView(LoadState.Empty, p => p.Add(v => v.Empty, "<i id=\"custom-empty\">none</i>"));
        var error = RenderView(LoadState.Error, p => p.Add(v => v.Error, "<i id=\"custom-error\">bad</i>"));

        Assert.NotNull(loading.Find("#custom-loading"));
        Assert.Empty(loading.FindAll($"#{Prefix}-loading"));
        Assert.NotNull(empty.Find("#custom-empty"));
        Assert.Empty(empty.FindAll($"#{Prefix}-empty"));
        Assert.NotNull(error.Find("#custom-error"));
        Assert.Empty(error.FindAll($"#{Prefix}-retry"));
    }

    [Fact]
    public void EmptyTitle_Custom_IsRendered()
    {
        var cut = RenderView(LoadState.Empty, p => p.Add(v => v.EmptyTitle, "No users"));

        Assert.Equal("No users", cut.Find($"#{Prefix}-empty-title").TextContent);
    }

    [Fact]
    public void Ids_Always_UseThePrefix()
    {
        foreach (var state in new[] { LoadState.Loading, LoadState.Empty, LoadState.Error })
        {
            var cut = RenderView(state);

            var ids = cut.FindAll("[id]").Select(element => element.GetAttribute("id")).Where(id => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(ids);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void InvalidPrefix_Throws(string prefix)
    {
        ThrownExceptions.ArgumentFrom(() => Render<DataStateView<string>>(parameters => parameters
            .Add(view => view.IdPrefix, prefix)
            .Add(view => view.State, LoadState.Loading)));
    }

    [Fact]
    public void ErrorText_WithMarkup_IsEncoded()
    {
        var cut = Render<DataStateView<string>>(parameters => parameters
            .Add(view => view.IdPrefix, Prefix)
            .Add(view => view.State, LoadState.Error)
            .Add(view => view.ErrorText, "<img src=x onerror=alert(1)>"));

        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void DefaultState_IsLoading()
    {
        Assert.Equal(LoadState.Loading, default(LoadState));
    }
}
