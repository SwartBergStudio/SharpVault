// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;

namespace SharpVault.UI.Shared.Components.Vault;

public static class StampText
{
    public const string Format = "yyyy-MM-dd HH:mm 'UTC'";

    public static string For(DateTimeOffset value) =>
        value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);
}
