// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Authentication.WebGateway;

public static class WebGatewayMessages
{
    public const string KeyRequired = "WebGateway:Key is required outside Development. Set it to at least 32 characters on both the API and the Web host. The value is not shown.";
    public const string KeyTooShort = "WebGateway:Key must be at least 32 characters after trimming. The value is not shown.";
    public const string FieldContainsNewLine = "A signed field must not contain a new line.";
}
