// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using SharpVault.Application.Features.OneTimeLinks.Commands;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class CreateOneTimeLinkCommandValidatorTests
{
    private const int MinimumPayloadBytes = 30;
    private const int MaximumPayloadBytes = 131072;
    private const int MinimumLifetimeMinutes = 5;
    private const int MaximumLifetimeMinutes = 10080;

    private static readonly CreateOneTimeLinkCommandValidator Validator = new();

    [Fact]
    public void Validate_EmptyOwnerId_Fails()
    {
        Assert.False(Validator.Validate(Command(ownerId: Guid.Empty)).IsValid);
    }

    [Fact]
    public void Validate_EmptySecretId_Fails()
    {
        Assert.False(Validator.Validate(Command(secretId: Guid.Empty)).IsValid);
    }

    [Fact]
    public void Validate_ValidIds_Pass()
    {
        Assert.True(Validator.Validate(Command()).IsValid);
    }

    [Fact]
    public void Validate_EmptyPayload_Fails()
    {
        Assert.False(Validator.Validate(Command(payload: string.Empty)).IsValid);
    }

    [Fact]
    public void Validate_InvalidBase64_Fails()
    {
        Assert.False(Validator.Validate(Command(payload: "not base64!!")).IsValid);
    }

    [Fact]
    public void Validate_FirstByteBelow0x80_Fails()
    {
        Assert.False(Validator.Validate(Command(payload: Payload(MinimumPayloadBytes, 0x7F))).IsValid);
    }

    [Fact]
    public void Validate_FirstByteAt0x80_Passes()
    {
        Assert.True(Validator.Validate(Command(payload: Payload(MinimumPayloadBytes, 0x80))).IsValid);
    }

    [Fact]
    public void Validate_PayloadOneByteBelowTheMinimum_Fails()
    {
        Assert.False(Validator.Validate(Command(payload: Payload(MinimumPayloadBytes - 1))).IsValid);
    }

    [Fact]
    public void Validate_PayloadAtMinimumAndMaximum_Passes()
    {
        Assert.True(Validator.Validate(Command(payload: Payload(MinimumPayloadBytes))).IsValid);
        Assert.True(Validator.Validate(Command(payload: Payload(MaximumPayloadBytes))).IsValid);
    }

    [Fact]
    public void Validate_PayloadOneByteOverTheMaximum_Fails()
    {
        Assert.False(Validator.Validate(Command(payload: Payload(MaximumPayloadBytes + 1))).IsValid);
    }

    [Fact]
    public void Validate_LifetimeAbsent_Passes()
    {
        Assert.True(Validator.Validate(Command(lifetimeMinutes: null)).IsValid);
    }

    [Theory]
    [InlineData(MinimumLifetimeMinutes)]
    [InlineData(MaximumLifetimeMinutes)]
    public void Validate_LifetimeAtBounds_Passes(int minutes)
    {
        Assert.True(Validator.Validate(Command(lifetimeMinutes: minutes)).IsValid);
    }

    [Theory]
    [InlineData(MinimumLifetimeMinutes - 1)]
    [InlineData(MaximumLifetimeMinutes + 1)]
    public void Validate_LifetimeOutsideBounds_Fails(int minutes)
    {
        Assert.False(Validator.Validate(Command(lifetimeMinutes: minutes)).IsValid);
    }

    private static CreateOneTimeLinkCommand Command(
        Guid? ownerId = null,
        Guid? secretId = null,
        string? payload = null,
        int? lifetimeMinutes = null) =>
        new(ownerId ?? Guid.NewGuid(), secretId ?? Guid.NewGuid(), payload ?? Payload(MinimumPayloadBytes), lifetimeMinutes);

    private static string Payload(int length, byte firstByte = 0x80)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        bytes[0] = firstByte;
        return Convert.ToBase64String(bytes);
    }
}
