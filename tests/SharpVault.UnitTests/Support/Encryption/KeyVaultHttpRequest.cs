// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support.Encryption;

internal sealed record KeyVaultHttpRequest(
    string Method,
    string Scheme,
    string Host,
    string Path,
    bool Authorized,
    string? Algorithm,
    string? RequestValue,
    string? ResponseValue);
