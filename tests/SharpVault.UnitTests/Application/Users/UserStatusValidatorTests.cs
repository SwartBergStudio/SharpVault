// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation.Results;
using SharpVault.Application.Features.Users.Commands;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Domain.Users;

namespace SharpVault.UnitTests.Application.Users;

[Trait("Category", "Security")]
public sealed class UserStatusValidatorTests
{
    private const string ValidReason = "Left the company, access removed by HR";

    private readonly LockUserCommandValidator _lock = new();
    private readonly UnlockUserCommandValidator _unlock = new();

    private static void AssertFailsOn(ValidationResult result, string property)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == property);
    }

    private static void AssertFailsWithMessage(ValidationResult result, string message)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == message);
    }

    [Fact]
    public void LockUser_ValidCommand_Passes() =>
        Assert.True(_lock.Validate(new LockUserCommand(Guid.NewGuid(), ReasonCategory.LeftCompany, ValidReason)).IsValid);

    [Fact]
    public void LockUser_EmptyUserId_FailsOnUserId() =>
        AssertFailsOn(_lock.Validate(new LockUserCommand(Guid.Empty, ReasonCategory.LeftCompany, ValidReason)), nameof(LockUserCommand.UserId));

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public void LockUser_UndefinedCategory_FailsWithCategoryMessage(int category) =>
        AssertFailsWithMessage(
            _lock.Validate(new LockUserCommand(Guid.NewGuid(), (ReasonCategory)category, ValidReason)),
            UserAdminMessages.CategoryInvalid);

    [Fact]
    public void LockUser_ShortReason_FailsOnReason() =>
        AssertFailsOn(_lock.Validate(new LockUserCommand(Guid.NewGuid(), ReasonCategory.Other, "short")), nameof(LockUserCommand.Reason));

    [Fact]
    public void LockUser_ReasonWithNul_FailsNotAllowed() =>
        AssertFailsWithMessage(
            _lock.Validate(new LockUserCommand(Guid.NewGuid(), ReasonCategory.Other, "Left the company\u0000 today, access removed")),
            UserAdminMessages.ReasonNotAllowed);

    [Fact]
    public void UnlockUser_ValidCommand_Passes() =>
        Assert.True(_unlock.Validate(new UnlockUserCommand(Guid.NewGuid(), ReasonCategory.RoleChange, ValidReason)).IsValid);

    [Fact]
    public void UnlockUser_EmptyUserId_FailsOnUserId() =>
        AssertFailsOn(_unlock.Validate(new UnlockUserCommand(Guid.Empty, ReasonCategory.RoleChange, ValidReason)), nameof(UnlockUserCommand.UserId));

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public void UnlockUser_UndefinedCategory_FailsWithCategoryMessage(int category) =>
        AssertFailsWithMessage(
            _unlock.Validate(new UnlockUserCommand(Guid.NewGuid(), (ReasonCategory)category, ValidReason)),
            UserAdminMessages.CategoryInvalid);

    [Fact]
    public void UnlockUser_ShortReason_FailsOnReason() =>
        AssertFailsOn(_unlock.Validate(new UnlockUserCommand(Guid.NewGuid(), ReasonCategory.Other, "short")), nameof(UnlockUserCommand.Reason));
}
