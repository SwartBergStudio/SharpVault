// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text;
using Microsoft.Playwright;

namespace SharpVault.E2ETests.Support;

public sealed class PageErrors
{
    public const string ConsoleErrorType = "error";

    private readonly List<string> _consoleErrors = [];
    private readonly List<string> _pageErrors = [];
    private readonly List<string> _cspViolations = [];
    private readonly List<string> _ignoredConsoleFragments = [];
    private readonly Lock _gate = new();

    public void AddCspViolation(string violation) => Record(_cspViolations, violation);

    public void IgnoreConsoleErrorsContaining(string fragment)
    {
        lock (_gate)
        {
            _ignoredConsoleFragments.Add(fragment);
        }
    }

    public void Attach(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        page.Console += (_, message) =>
        {
            if (message.Type == ConsoleErrorType && !IsIgnored(message.Text))
            {
                Record(_consoleErrors, message.Text);
            }
        };
        page.PageError += (_, error) => Record(_pageErrors, error);
    }

    public void AssertClean(string url)
    {
        List<string> consoleErrors;
        List<string> pageErrors;
        List<string> cspViolations;
        lock (_gate)
        {
            consoleErrors = [.. _consoleErrors];
            pageErrors = [.. _pageErrors];
            cspViolations = [.. _cspViolations];
        }

        var problems = new StringBuilder();
        AppendAll(problems, "Content Security Policy violation", cspViolations);
        AppendAll(problems, "Console error", consoleErrors);
        AppendAll(problems, "Page error", pageErrors);
        if (problems.Length > 0)
        {
            throw new Xunit.Sdk.XunitException($"The browser reported problems at {url}:{Environment.NewLine}{problems}");
        }
    }

    private static void AppendAll(StringBuilder builder, string label, IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            builder.Append(label).Append(": ").AppendLine(item);
        }
    }

    private void Record(List<string> target, string text)
    {
        lock (_gate)
        {
            target.Add(text);
        }
    }

    private bool IsIgnored(string text)
    {
        lock (_gate)
        {
            return _ignoredConsoleFragments.Exists(fragment => text.Contains(fragment, StringComparison.Ordinal));
        }
    }
}
