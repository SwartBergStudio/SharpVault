// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using FluentValidation.Results;
using Mediator;
using NSubstitute;
using SharpVault.Application.Common.Behaviors;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Dtos;
using SharpVault.Application.Features.Users.Queries;

namespace SharpVault.UnitTests.Application.Behaviors;

public sealed class ValidationBehaviorTests
{
    private static readonly Result<UserPageDto> Expected = Result<UserPageDto>.Success(new UserPageDto([], null));

    private int _nextCalls;

    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<ListUsersQuery, Result<UserPageDto>>([]);

        var response = await behavior.Handle(new ListUsersQuery(0, "garbage"), Next(), TestContext.Current.CancellationToken);

        Assert.Same(Expected, response);
        Assert.Equal(1, _nextCalls);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        var behavior = new ValidationBehavior<ListUsersQuery, Result<UserPageDto>>([new ListUsersQueryValidator()]);

        var response = await behavior.Handle(new ListUsersQuery(10, null), Next(), TestContext.Current.CancellationToken);

        Assert.Same(Expected, response);
        Assert.Equal(1, _nextCalls);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_InvalidRequest_ThrowsValidationExceptionWithoutCallingNext()
    {
        var behavior = new ValidationBehavior<ListUsersQuery, Result<UserPageDto>>([new ListUsersQueryValidator()]);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new ListUsersQuery(0, "' or 1 eq 1"), Next(), TestContext.Current.CancellationToken));

        Assert.Equal(2, exception.Errors.Count());
        Assert.Equal(0, _nextCalls);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SecondValidatorFails_ThrowsAndMergesFailures()
    {
        var failing = Substitute.For<IValidator<ListUsersQuery>>();
        failing.ValidateAsync(Arg.Any<IValidationContext>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("Other", "Other is not valid.")]));
        var behavior = new ValidationBehavior<ListUsersQuery, Result<UserPageDto>>([new ListUsersQueryValidator(), failing]);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new ListUsersQuery(10, null), Next(), TestContext.Current.CancellationToken));

        Assert.Equal("Other", Assert.Single(exception.Errors).PropertyName);
        Assert.Equal(0, _nextCalls);
    }

    private RequestHandlerDelegate<Result<UserPageDto>> Next() =>
        () =>
        {
            _nextCalls++;
            return Task.FromResult(Expected);
        };
}
