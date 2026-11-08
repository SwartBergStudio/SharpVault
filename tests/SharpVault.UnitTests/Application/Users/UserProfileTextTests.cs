// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using SharpVault.Application.Features.Users.Access;

namespace SharpVault.UnitTests.Application.Users;

public sealed class UserProfileTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("\t\r\n")]
    public void NormalizeDisplayName_NullOrWhitespace_ReturnsEmpty(string? value)
    {
        Assert.Equal(string.Empty, UserProfileText.NormalizeDisplayName(value));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Al\u0000ice", "Alice")]
    [InlineData("Ali\rce\n", "Alice")]
    [InlineData("Al\u0085ice", "Alice")]
    [InlineData("\u0000\u0000\u0000", "")]
    public void NormalizeDisplayName_ControlCharacters_AreRemoved(string value, string expected)
    {
        Assert.Equal(expected, UserProfileText.NormalizeDisplayName(value));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Alice‮ecila", "Aliceecila")]
    [InlineData("Ali​ce", "Alice")]
    [InlineData("﻿Alice", "Alice")]
    public void NormalizeDisplayName_UnicodeFormatCharacters_AreRemoved(string value, string expected)
    {
        var result = UserProfileText.NormalizeDisplayName(value);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(result, character => char.GetUnicodeCategory(character) == UnicodeCategory.Format);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("alice‮@example.test", "alice@example.test")]
    [InlineData("ali​ce@example.test", "alice@example.test")]
    public void NormalizeEmail_UnicodeFormatCharacters_AreRemoved(string value, string expected)
    {
        var result = UserProfileText.NormalizeEmail(value);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(result!, character => char.GetUnicodeCategory(character) == UnicodeCategory.Format);
    }

    [Theory]
    [InlineData("  Alice Example  ", "Alice Example")]
    [InlineData("Alice  Example", "Alice  Example")]
    public void NormalizeDisplayName_Whitespace_IsTrimmedAtEdgesOnly(string value, string expected)
    {
        Assert.Equal(expected, UserProfileText.NormalizeDisplayName(value));
    }

    [Theory]
    [InlineData(255)]
    [InlineData(257)]
    public void NormalizeDisplayName_AroundMaximum_IsTruncatedTo256(int length)
    {
        var result = UserProfileText.NormalizeDisplayName(new string('a', length));

        Assert.Equal(Math.Min(length, UserProfileText.DisplayNameMaxLength), result.Length);
    }

    [Fact]
    public void NormalizeDisplayName_TruncationAtSurrogatePair_DropsLoneHighSurrogate()
    {
        var value = new string('a', 255) + "\U0001F600";

        var result = UserProfileText.NormalizeDisplayName(value);

        Assert.Equal(255, result.Length);
        Assert.False(char.IsHighSurrogate(result[^1]));
    }

    [Fact]
    public void NormalizeDisplayName_SurrogatePairFittingExactly_IsKept()
    {
        var value = new string('a', 254) + "\U0001F600";

        var result = UserProfileText.NormalizeDisplayName(value);

        Assert.Equal(value, result);
        Assert.Equal(UserProfileText.DisplayNameMaxLength, result.Length);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void NormalizeDisplayName_ControlCharactersCountedAfterRemoval_KeepsFullLength()
    {
        var value = string.Concat(Enumerable.Repeat("a\u0000", 300));

        var result = UserProfileText.NormalizeDisplayName(value);

        Assert.Equal(UserProfileText.DisplayNameMaxLength, result.Length);
        Assert.All(result, character => Assert.Equal('a', character));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-at-sign")]
    public void NormalizeEmail_WithoutAtSign_ReturnsNull(string? value)
    {
        Assert.Null(UserProfileText.NormalizeEmail(value));
    }

    [Fact]
    public void NormalizeEmail_ValidAddress_IsKeptAndTrimmed()
    {
        Assert.Equal("alice@example.test", UserProfileText.NormalizeEmail("  alice@example.test "));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void NormalizeEmail_ControlCharacters_AreRemoved()
    {
        Assert.Equal("alice@example.test", UserProfileText.NormalizeEmail("ali\u0000ce@exam\r\nple.test"));
    }

    [Fact]
    public void NormalizeEmail_LongerThanMaximum_IsTruncated()
    {
        var value = new string('a', 300) + "@" + new string('b', 100);

        var result = UserProfileText.NormalizeEmail(value);

        Assert.NotNull(result);
        Assert.Equal(UserProfileText.EmailMaxLength, result.Length);
        Assert.Contains('@', result);
    }

    [Theory]
    [InlineData("10.0.0.1", "10.0.0.1")]
    [InlineData("2001:0DB8:0000:0000:0000:0000:0000:0001", "2001:db8::1")]
    [InlineData(null, null)]
    [InlineData("not-an-ip", null)]
    [InlineData("10.0.0.1:443", null)]
    [InlineData("1.2.3.4\r\nX: y", null)]
    public void NormalizeIpAddress_Values_AreParsedOrNull(string? value, string? expected)
    {
        Assert.Equal(expected, UserProfileText.NormalizeIpAddress(value));
    }

    [Fact]
    public void NormalizeIpAddress_LongerThanMaximum_ReturnsNull()
    {
        Assert.Null(UserProfileText.NormalizeIpAddress(new string('1', UserProfileText.IpAddressMaxLength + 1)));
    }
}
