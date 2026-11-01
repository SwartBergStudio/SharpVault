// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support.Theme;

internal static class ThrownExceptions
{
    public static ArgumentException ArgumentFrom(Action action)
    {
        var thrown = Record.Exception(action);
        Assert.NotNull(thrown);
        return Assert.IsAssignableFrom<ArgumentException>(Unwrap(thrown));
    }

    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current is not ArgumentException && current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }
}
