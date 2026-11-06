// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Authentication.Local;

internal static class LocalAuthenticationMessages
{
    public const string KeyFileInvalid = "The local authentication key file is empty, not valid base64, or the wrong length. Delete the file and restart both hosts: ";
    public const string KeyFileTooOpen = "The local authentication key file is readable by other users. Run: chmod 600 ";
    public const string AccountNotFound = "The principal does not identify a known local account.";
    public const string StartupNotice = "Local authentication is enabled. Anyone who can reach this host can sign in as a built-in development account. Never enable it outside local development.";
}
