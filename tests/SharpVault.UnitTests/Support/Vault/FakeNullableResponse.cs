// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class FakeNullableResponse<T>(T? value) : NullableResponse<T>
    where T : class
{
    public override bool HasValue => value is not null;

    public override T? Value => value;

    public override Response GetRawResponse() => new FakeAzureResponse(200, null);
}
