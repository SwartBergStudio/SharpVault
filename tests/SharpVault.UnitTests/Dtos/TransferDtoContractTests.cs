// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;

namespace SharpVault.UnitTests.Dtos;

[Trait("Category", "Security")]
public sealed class TransferDtoContractTests
{
    private static readonly string[] ForbiddenMemberNames = ["Value", "Notes"];

    [Fact]
    public void TransferDtos_AreSealedClassesWithoutMethods()
    {
        foreach (var type in typeof(TransferDto).Assembly.GetTypes().Where(type => type.IsClass && type.Namespace is "SharpVault.Dtos.Transfers" or "SharpVault.Dtos.Users"))
        {
            Assert.True(type.IsSealed, $"{type.Name} must be sealed.");
            Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), method => !method.IsSpecialName);
        }
    }

    [Fact]
    public void TransferDtos_HaveNoValueOrNotesMember()
    {
        var names = typeof(TransferDto).Assembly.GetTypes()
            .Where(type => type.Namespace is "SharpVault.Dtos.Transfers")
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain(names, name => ForbiddenMemberNames.Contains(name));
    }

    [Fact]
    public void TransferPreviewFolderDto_HasNoNameMember()
    {
        var names = typeof(TransferPreviewFolderDto).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name);

        Assert.DoesNotContain("Name", names);
    }
}
