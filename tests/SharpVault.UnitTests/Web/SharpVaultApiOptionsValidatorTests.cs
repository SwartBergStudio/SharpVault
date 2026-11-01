// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using WebApp::SharpVault.Web.Api;

namespace SharpVault.UnitTests.Web;

public sealed class SharpVaultApiOptionsValidatorTests
{
    private readonly SharpVaultApiOptionsValidator _validator = new();

    private static SharpVaultApiOptions Valid() =>
        new() { BaseAddress = new Uri("https://api.example.test/"), Scopes = ["api://a1b2c3d4/access_as_user"] };

    [Fact]
    public void Validate_Valid_Succeeds()
    {
        Assert.True(_validator.Validate(null, Valid()).Succeeded);
    }

    [Fact]
    public void Validate_ServiceDiscoveryAddress_Succeeds()
    {
        var options = Valid();
        options.BaseAddress = new Uri("https+http://api");

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void Validate_MissingBaseAddress_Fails()
    {
        var options = Valid();
        options.BaseAddress = null;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("BaseAddress", Assert.Single(result.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RelativeBaseAddress_Fails()
    {
        var options = Valid();
        options.BaseAddress = new Uri("/api/", UriKind.Relative);

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Validate_NoScopes_Fails()
    {
        var options = Valid();
        options.Scopes = [];

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Scopes", Assert.Single(result.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NullScopes_Fails()
    {
        var options = Valid();
        options.Scopes = null!;

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_WhitespaceScope_Fails(string scope)
    {
        var options = Valid();
        options.Scopes = [scope];

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Validate_ValidScopeAlongsideWhitespaceScope_Fails()
    {
        var options = Valid();
        options.Scopes = ["api://a1b2c3d4/access_as_user", " "];

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Validate_BothInvalid_ReportsBoth()
    {
        var result = _validator.Validate(null, new SharpVaultApiOptions());

        Assert.Equal(2, result.Failures!.Count());
    }

    [Fact]
    public void Validate_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(null, null!));
    }
}
