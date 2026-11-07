// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Infrastructure.Storage;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Storage;

public sealed class TableStorageTableSetTests
{
    [Fact]
    public void Constructor_ValidNames_KeepsInputOrder()
    {
        var tableSet = new TableStorageTableSet(["Zebra", "Apple", "Mango"]);

        Assert.Equal(["Zebra", "Apple", "Mango"], tableSet.Names);
    }

    [Fact]
    public void Constructor_SourceListChangedAfterConstruction_DoesNotChangeNames()
    {
        var source = new List<string> { "Secrets", "Audit" };
        var tableSet = new TableStorageTableSet(source);

        source.Add("Injected");
        source.Clear();

        Assert.Equal(["Secrets", "Audit"], tableSet.Names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Constructor_NameShorterThanThreeCharacters_Throws(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet([name]));

        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(exception));
        Assert.Equal("names", exception.ParamName);
    }

    [Fact]
    public void Constructor_NameLongerThanSixtyThreeCharacters_Throws()
    {
        var name = "T" + new string('a', 63);

        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet([name]));

        Assert.Equal(64, name.Length);
        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(exception));
    }

    [Fact]
    public void Constructor_NameOfExactlyThreeAndSixtyThreeCharacters_IsAccepted()
    {
        var tableSet = new TableStorageTableSet(["Abc", "T" + new string('a', 62)]);

        Assert.Equal(2, tableSet.Names.Count);
    }

    [Fact]
    public void Constructor_NameStartingWithDigit_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet(["1Secrets"]));

        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(exception));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Bad-Name")]
    [InlineData("Bad Name")]
    [InlineData("Bad\u0000Name")]
    [InlineData("' or PartitionKey ne '")]
    [InlineData(" Secrets")]
    [InlineData("Secrets ")]
    public void Constructor_NameWithIllegalCharacter_ThrowsWithoutEchoingIt(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet([name]));

        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(exception));
        Assert.DoesNotContain(name, ExtractMessage(exception), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Constructor_NameWithTrailingLineBreak_Throws()
    {
        var exception = Assert.IsType<ArgumentException>(Record.Exception(() => new TableStorageTableSet(["Secrets\n"])));

        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(exception));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Constructor_NameWithTrailingLineBreakAtMaximumLength_Throws()
    {
        var name = "T" + new string('a', 62) + "\n";

        var exception = Record.Exception(() => new TableStorageTableSet([name]));

        Assert.IsType<ArgumentException>(exception);
    }

    [Theory]
    [InlineData("tables")]
    [InlineData("TABLES")]
    public void Constructor_ReservedNameTables_Throws(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet([name]));

        Assert.Equal(TableStorageMessageText.ReservedTableName, ExtractMessage(exception));
        Assert.Equal("names", exception.ParamName);
    }

    [Fact]
    public void Constructor_NameContainingTablesButNotEqual_IsAccepted()
    {
        var tableSet = new TableStorageTableSet(["MyTables", "Tables1", "tablesx"]);

        Assert.Equal(3, tableSet.Names.Count);
    }

    [Fact]
    public void Constructor_DuplicateNamesDifferingOnlyInCase_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet(["Secrets", "SECRETS"]));

        Assert.Equal(TableStorageMessageText.DuplicateTableName, ExtractMessage(exception));
        Assert.Equal("names", exception.ParamName);
    }

    [Fact]
    public void TableNames_All_PassesTableSetValidation()
    {
        var exception = Record.Exception(() => new TableStorageTableSet(TableNames.All));

        Assert.Null(exception);
    }

    private static string ExtractMessage(ArgumentException exception) =>
        exception.Message.Split(" (Parameter", StringSplitOptions.None)[0];
}
