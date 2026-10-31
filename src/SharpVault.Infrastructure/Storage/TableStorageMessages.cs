// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Storage;

internal static class TableStorageMessages
{
    public const string NotConfigured = "Table Storage is not configured. Set ConnectionStrings:tables to the Table Storage service URI.";

    public const string ConnectionStringNotAllowed = "Outside the Development environment Table Storage must use an https service URI with Microsoft Entra ID authentication. Connection strings, account keys and SAS tokens are not allowed.";

    public const string ServiceUriNotAllowed = "Outside the Development environment the Table Storage service URI must be an absolute https URI without a query string or user information.";

    public const string InvalidTableName = "Table names must be 3 to 63 letters or digits and start with a letter.";

    public const string ReservedTableName = "The table name 'tables' is reserved by Azure Table Storage.";

    public const string DuplicateTableName = "Table names must be unique, ignoring case.";
}
