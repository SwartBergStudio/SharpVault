// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Web.Security;

public static class ReturnUrls
{
    public const int MaxLength = 2048;

    private const string Root = "/";
    private const char Slash = '/';
    private const char Backslash = '\\';

    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl.Length > MaxLength)
        {
            return Root;
        }

        if (returnUrl[0] != Slash)
        {
            return Root;
        }

        if (returnUrl.Length > 1 && returnUrl[1] is Slash or Backslash)
        {
            return Root;
        }

        if (returnUrl.Contains(Backslash) || returnUrl.Any(char.IsControl))
        {
            return Root;
        }

        return Uri.TryCreate(returnUrl, UriKind.Relative, out _) ? returnUrl : Root;
    }
}
