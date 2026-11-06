// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;

namespace SharpVault.UnitTests.Application.Models;

public sealed class ResultTests
{
    [Fact]
    public void Success_Value_IsSuccessWithNoErrors()
    {
        var result = Result<int>.Success(7);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
        Assert.Null(result.Error);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Failure_Message_KeepsInvalid()
    {
        var result = Result<int>.Failure("bad");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureKind.Invalid, result.FailureKind);
        Assert.Equal("bad", result.Error);
        Assert.Equal(["bad"], result.Errors);
    }

    [Fact]
    public void Failure_ManyMessages_KeepsInvalidAndFirstError()
    {
        var result = Result<string>.Failure(["first", "second"]);

        Assert.Equal(ResultFailureKind.Invalid, result.FailureKind);
        Assert.Equal("first", result.Error);
        Assert.Equal(["first", "second"], result.Errors);
    }

    [Fact]
    public void Failure_EmptyList_HasNoError()
    {
        var result = Result<string>.Failure([]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NotFound_Always_UsesConstantMessage()
    {
        var result = Result<Done>.NotFound();

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(ResultMessages.NotFound, result.Error);
        Assert.Equal([ResultMessages.NotFound], result.Errors);
    }

    [Fact]
    public void Conflict_Always_UsesConstantMessage()
    {
        var result = Result<Done>.Conflict();

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Equal(ResultMessages.Conflict, result.Error);
        Assert.Equal([ResultMessages.Conflict], result.Errors);
    }

    [Fact]
    public void Rejected_Message_KeepsMessageAndKind()
    {
        var result = Result<Done>.Rejected("too deep");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal("too deep", result.Error);
        Assert.Equal(["too deep"], result.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejected_MissingMessage_Throws(string? message)
    {
        Assert.ThrowsAny<ArgumentException>(() => Result<Done>.Rejected(message!));
    }

    [Fact]
    public void FailureKind_Values_AreStable()
    {
        Assert.Equal(0, (int)ResultFailureKind.Invalid);
        Assert.Equal(1, (int)ResultFailureKind.NotFound);
        Assert.Equal(2, (int)ResultFailureKind.Conflict);
        Assert.Equal(3, (int)ResultFailureKind.Rejected);
    }

    [Fact]
    public void Done_Value_IsSingleton()
    {
        Assert.Same(Done.Value, Done.Value);
    }
}
