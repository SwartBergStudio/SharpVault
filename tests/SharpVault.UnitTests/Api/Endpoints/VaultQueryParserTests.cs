// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Api.Endpoints.Vault;
using SharpVault.Dtos.Vault;

namespace SharpVault.UnitTests.Api.Endpoints;

public sealed class VaultQueryParserTests
{
    [Theory]
    [InlineData("MoveToParent", FolderDeleteModeDto.MoveToParent)]
    [InlineData("DeleteContents", FolderDeleteModeDto.DeleteContents)]
    public void ParseMode_ExactName_ReturnsTheMode(string value, FolderDeleteModeDto expected)
    {
        Assert.Equal(expected, VaultQueryParser.ParseMode(value));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("movetoparent")]
    [InlineData("MoveToParent,DeleteContents")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void ParseMode_AnythingElse_ReturnsTheUndefinedDefault(string value)
    {
        var mode = VaultQueryParser.ParseMode(value);

        Assert.Equal(default, mode);
        Assert.False(Enum.IsDefined(mode));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ParseMode_NullValue_ReturnsTheUndefinedDefault()
    {
        Assert.Equal(default, VaultQueryParser.ParseMode(null!));
    }
}
