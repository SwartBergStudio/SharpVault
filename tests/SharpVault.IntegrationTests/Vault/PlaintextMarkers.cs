// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Vault;

public sealed class PlaintextMarkers
{
    public PlaintextMarkers()
    {
        var token = Guid.NewGuid().ToString("N");
        Name = $"SV-NAME-{token}";
        Username = $"SV-USER-{token}";
        Url = $"https://sv-url-{token}.test/";
        Tag = $"SV-TAG-{token}";
        Value = $"SV-VALUE-{token}";
        Notes = $"SV-NOTES-{token}";
        Folder = $"SV-FOLDER-{token}";
    }

    public string Name { get; }

    public string Username { get; }

    public string Url { get; }

    public string Tag { get; }

    public string Value { get; }

    public string Notes { get; }

    public string Folder { get; }

    public IReadOnlyList<string> All => [Name, Username, Url, Tag, Value, Notes, Folder];

    public void AssertAbsentFrom(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var marker in All)
        {
            Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
        }
    }
}
