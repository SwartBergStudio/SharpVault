// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Text.RegularExpressions;
using SharpVault.Dtos;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UnitTests.UiShared.Services;

[Trait("Category", "Security")]
public sealed partial class ApiRouteTemplateTests
{
    [Fact]
    public void ApiRouteTemplate_Bind_ReplacesThePlaceholder()
    {
        var id = Guid.Parse("3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b");

        var route = ApiRoutes.VaultSecretReveal.Bind("secretId", id);

        Assert.Equal("/api/vault/secrets/3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b/reveal", route);
    }

    [Fact]
    public void Bind_Chained_ReplacesBothPlaceholders()
    {
        var secretId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        var route = ApiRoutes.VaultSecretVersionRestore.Bind("secretId", secretId).Bind("versionId", versionId);

        Assert.Equal($"/api/vault/secrets/{secretId:D}/history/{versionId:D}/restore", route);
        Assert.DoesNotContain('{', route);
    }

    [Theory]
    [InlineData("folderId")]
    [InlineData("SecretId")]
    [InlineData("secretId:guid")]
    [InlineData("")]
    [InlineData(" ")]
    public void ApiRouteTemplate_Bind_MissingPlaceholder_Throws(string parameterName)
    {
        Assert.ThrowsAny<ArgumentException>(() => ApiRoutes.VaultSecret.Bind(parameterName, Guid.NewGuid()));
    }

    [Fact]
    public void Bind_AlreadyBoundTemplate_Throws()
    {
        var bound = ApiRoutes.VaultSecret.Bind("secretId", Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => bound.Bind("secretId", Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Bind_BlankTemplate_Throws(string template)
    {
        Assert.ThrowsAny<ArgumentException>(() => template.Bind("secretId", Guid.NewGuid()));
    }

    [Fact]
    public void Bind_EveryApiRoute_CanBeFullyBoundFromItsOwnPlaceholders()
    {
        var routes = typeof(ApiRoutes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(routes);
        foreach (var template in routes)
        {
            var route = template;
            foreach (Match placeholder in PlaceholderPattern().Matches(template))
            {
                route = route.Bind(placeholder.Groups["name"].Value, Guid.NewGuid());
            }

            Assert.DoesNotContain('{', route);
            Assert.DoesNotContain('}', route);
        }
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z]+):guid\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex PlaceholderPattern();
}
