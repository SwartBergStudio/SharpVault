// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using SharpVault.Infrastructure.Storage;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Storage;

public sealed class TableStorageTableSetTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void Constructor_ValidNames_ExposesNamesInOrder()
    {
        var names = _fixture.CreateMany<Guid>(5)
            .Select((id, index) => "T" + index + id.ToString("N")[..10])
            .ToList();

        var tableSet = new TableStorageTableSet(names);

        Assert.Equal(names, tableSet.Names);
    }

    [Fact]
    public void Constructor_OrderIsNotAlphabetical_KeepsInputOrder()
    {
        var tableSet = new TableStorageTableSet(["Zebra", "Apple", "Mango"]);

        Assert.Equal(["Zebra", "Apple", "Mango"], tableSet.Names);
    }

    [Fact]
    public void Constructor_EmptyList_ExposesNoNames()
    {
        var tableSet = new TableStorageTableSet([]);

        Assert.Empty(tableSet.Names);
    }

    [Fact]
    public void Constructor_NullNames_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new TableStorageTableSet(null!));

        Assert.Equal("names", exception.ParamName);
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
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("A1")]
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
        var shortest = "Abc";
        var longest = "T" + new string('a', 62);

        var tableSet = new TableStorageTableSet([shortest, longest]);

        Assert.Equal(3, tableSet.Names[0].Length);
        Assert.Equal(63, tableSet.Names[1].Length);
    }

    [Theory]
    [InlineData("1Secrets")]
    [InlineData("123")]
    [InlineData("9abc")]
    public void Constructor_NameStartingWithDigit_Throws(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet([name]));

        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(exception));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Bad-Name")]
    [InlineData("Bad Name")]
    [InlineData("Bad/Name")]
    [InlineData("Bad\\Name")]
    [InlineData("Bad#Name")]
    [InlineData("Bad?Name")]
    [InlineData("Bad\u0000Name")]
    [InlineData("Bad\tName")]
    [InlineData("Tablé" + "1")]
    [InlineData("' or PartitionKey ne '")]
    [InlineData("Bad_Name")]
    [InlineData("Bad.Name")]
    [InlineData("Bad٠١٢")]
    [InlineData("١Bad")]
    [InlineData(" Secrets")]
    [InlineData("Secrets ")]
    [InlineData("Secr​ets")]
    public void Constructor_NameWithIllegalCharacter_Throws(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet([name]));

        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(exception));
        Assert.DoesNotContain(name, ExtractMessage(exception), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Secrets\n")]
    [InlineData("Secrets\r\n")]
    [InlineData("Secrets\n\n")]
    public void Constructor_NameWithTrailingLineBreak_Throws(string name)
    {
        var exception = Record.Exception(() => new TableStorageTableSet([name]));

        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(argumentException));
    }

    [Fact]
    public void Constructor_NameWithTrailingLineBreakAtMaximumLength_Throws()
    {
        var name = "T" + new string('a', 62) + "\n";

        var exception = Record.Exception(() => new TableStorageTableSet([name]));

        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void Constructor_NullElement_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet(["Secrets", null!]));

        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(exception));
    }

    [Theory]
    [InlineData("tables")]
    [InlineData("Tables")]
    [InlineData("TABLES")]
    [InlineData("tAbLeS")]
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
    public void Constructor_ExactDuplicateNames_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet(["Secrets", "Audit", "Secrets"]));

        Assert.Equal(TableStorageMessageText.DuplicateTableName, ExtractMessage(exception));
    }

    [Fact]
    public void Constructor_InvalidNameAfterValidNames_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableStorageTableSet(["Secrets", "Audit", "bad-name"]));

        Assert.Equal(TableStorageMessageText.InvalidTableName, ExtractMessage(exception));
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
