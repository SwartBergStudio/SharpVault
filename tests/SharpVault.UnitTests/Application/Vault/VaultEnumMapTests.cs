// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class VaultEnumMapTests
{
    [Fact]
    public void ToDto_ToDomain_RoundTripEveryMember()
    {
        foreach (var type in Enum.GetValues<SecretType>())
        {
            Assert.Equal(type, VaultEnumMap.ToDomain(VaultEnumMap.ToDto(type)));
            Assert.Equal(type.ToString(), VaultEnumMap.ToDto(type).ToString());
        }

        foreach (var dto in Enum.GetValues<SecretTypeDto>())
        {
            Assert.Equal(dto, VaultEnumMap.ToDto(VaultEnumMap.ToDomain(dto)));
        }
    }

    [Fact]
    public void Enums_HaveTheSameNumberOfMembers()
    {
        Assert.Equal(Enum.GetValues<SecretType>().Length, Enum.GetValues<SecretTypeDto>().Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(-1)]
    public void ToDomain_UnknownValue_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VaultEnumMap.ToDomain((SecretTypeDto)value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void ToDto_UnknownValue_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VaultEnumMap.ToDto((SecretType)value));
    }
}
