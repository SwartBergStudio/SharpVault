// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;

namespace SharpVault.Infrastructure.Storage;

public sealed partial class TableStorageTableSet
{
    private const string ReservedName = "tables";

    public TableStorageTableSet(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var validated = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (name is null || !ValidNamePattern().IsMatch(name))
            {
                throw new ArgumentException(TableStorageMessages.InvalidTableName, nameof(names));
            }

            if (string.Equals(name, ReservedName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(TableStorageMessages.ReservedTableName, nameof(names));
            }

            if (!seen.Add(name))
            {
                throw new ArgumentException(TableStorageMessages.DuplicateTableName, nameof(names));
            }

            validated.Add(name);
        }

        Names = validated;
    }

    public IReadOnlyList<string> Names { get; }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{2,62}\\z")]
    private static partial Regex ValidNamePattern();
}
