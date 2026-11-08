// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;

namespace SharpVault.E2ETests.Pages;

public abstract class PageObject(IPage page)
{
    public const float TimeoutMilliseconds = 15000;
    private const int UrlPollMilliseconds = 50;
    private const string IdSelectorPrefix = "#";
    private const string OptionRoleName = "Option";

    protected IPage Page { get; } = page;

    protected ILocator Element(string id) => Page.Locator(IdSelectorPrefix + id);

    protected async Task ShowsAsync(string id, float timeoutMilliseconds = TimeoutMilliseconds)
    {
        try
        {
            await Microsoft.Playwright.Assertions.Expect(Element(id)).ToBeVisibleAsync(new() { Timeout = timeoutMilliseconds });
        }
        catch (Exception exception) when (exception is Microsoft.Playwright.PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"#{id} did not show at {Page.Url}: {exception.Message}", exception);
        }
    }

    protected async Task HidesAsync(string id)
    {
        try
        {
            await Microsoft.Playwright.Assertions.Expect(Element(id)).ToBeHiddenAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is Microsoft.Playwright.PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"#{id} did not hide at {Page.Url}: {exception.Message}", exception);
        }
    }

    protected async Task HasExactTextAsync(string id, string text)
    {
        try
        {
            await Microsoft.Playwright.Assertions.Expect(Element(id)).ToHaveTextAsync(text, new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is Microsoft.Playwright.PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"#{id} did not read exactly \"{text}\" at {Page.Url}: {exception.Message}", exception);
        }
    }

    protected async Task ContainsTextAsync(string id, string text)
    {
        try
        {
            await Microsoft.Playwright.Assertions.Expect(Element(id)).ToContainTextAsync(text, new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is Microsoft.Playwright.PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"#{id} did not contain \"{text}\" at {Page.Url}: {exception.Message}", exception);
        }
    }

    protected async Task ClickAsync(string id)
    {
        try
        {
            await Element(id).ClickAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is Microsoft.Playwright.PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"Could not click #{id} at {Page.Url}: {exception.Message}", exception);
        }
    }

    protected async Task FillAsync(string id, string value)
    {
        try
        {
            await Element(id).Or(Element(id).Locator("input, textarea")).First.FillAsync(value, new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is Microsoft.Playwright.PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"Could not fill #{id} at {Page.Url}: {exception.Message}", exception);
        }
    }

    protected async Task ChooseOptionAsync(string selectId, string optionText)
    {
        try
        {
            await Element(selectId).SelectOptionAsync(new SelectOptionValue { Label = optionText }, new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is Microsoft.Playwright.PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"Could not choose \"{optionText}\" in #{selectId} at {Page.Url}: {exception.Message}", exception);
        }
    }

    public static async Task WaitForUrlAsync(IPage page, Func<string, bool> matches, string expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(TimeoutMilliseconds);
        while (!matches(page.Url))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new InvalidOperationException($"The page did not reach {expected}. URL: {page.Url}.");
            }

            await Task.Delay(UrlPollMilliseconds, TestContext.Current.CancellationToken);
        }
    }

    protected Task WaitForUrlAsync(Func<string, bool> matches, string expected) => WaitForUrlAsync(Page, matches, expected);

    protected Task WaitForUrlNotAsync(string fragment) =>
        WaitForUrlAsync(url => !url.Contains(fragment, StringComparison.Ordinal), $"a URL without \"{fragment}\"");

    protected async Task GoToAsync(string url)
    {
        await Page.GotoAsync(url, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded, Timeout = TimeoutMilliseconds });
    }
}
