// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections;
using AutoFixture;
using AutoFixture.Kernel;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Folders.Commands;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.PasswordGeneration;

namespace SharpVault.UnitTests.Application.Vault;

[Trait("Category", "Security")]
public sealed class SecretBearingRecordsTests
{
    public static TheoryData<Type> SecretBearingTypes() =>
    [
        typeof(CreateSecretCommand),
        typeof(UpdateSecretCommand),
        typeof(SecretMetadata),
        typeof(CreateFolderCommand),
        typeof(RenameFolderCommand),
        typeof(GeneratedSecret),
    ];

    [Theory]
    [MemberData(nameof(SecretBearingTypes))]
    public void SecretBearingRecords_ToString_ReturnTypeNameOnly(Type type)
    {
        var fixture = new Fixture();
        var instance = new SpecimenContext(fixture).Resolve(type);

        var text = instance.ToString();

        Assert.Equal(type.Name, text);
        foreach (var plaintext in PlaintextOf(instance))
        {
            Assert.DoesNotContain(plaintext, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(SecretBearingTypes))]
    public void SecretBearingRecords_ToString_DoesNotLeakEvenWhenEveryTextIsAMarker(Type type)
    {
        var fixture = new Fixture();
        fixture.Register(() => "SV-MARKER-" + Guid.NewGuid().ToString("N"));
        var instance = new SpecimenContext(fixture).Resolve(type);

        var text = instance.ToString();

        Assert.DoesNotContain("SV-MARKER-", text, StringComparison.Ordinal);
        Assert.NotEmpty(PlaintextOf(instance));
    }

    private static List<string> PlaintextOf(object instance)
    {
        var values = new List<string>();
        foreach (var property in instance.GetType().GetProperties().Where(property => property.GetIndexParameters().Length == 0))
        {
            switch (property.GetValue(instance))
            {
                case string text when text.Length > 0:
                    values.Add(text);
                    break;
                case IEnumerable items and not string:
                    values.AddRange(items.OfType<string>().Where(item => item.Length > 0));
                    break;
            }
        }

        return values;
    }
}
