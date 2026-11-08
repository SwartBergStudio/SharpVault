// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpVault.Application.Features.OneTimeLinks.Commands;
using SharpVault.UI.Shared.Services.OneTimeLinks;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed partial class OneTimeLinkSourceRulesTests
{
    private const string BrowserOnlyInputTag = "<SvBrowserOnlyInput";
    private const string CreateViewFile = "OneTimeLinkCreateView.razor";
    private const string GatewayKeyProperty = "GatewayKey";
    private const string WebGatewaySection = "WebGateway";
    private const string KeyProperty = "Key";

    private static readonly string UiSharedRoot = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.UI.Shared");
    private static readonly string WebRoot = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Web");

    [Fact]
    public void BrowserOnlyInput_IsUsedOnlyInTheCreateViewAndOpenFlow()
    {
        var users = RepositoryRoot.EnumerateFiles("*.razor")
            .Where(path => File.ReadAllText(path).Contains(BrowserOnlyInputTag, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.NotEmpty(users);
        Assert.All(users, name => Assert.Contains("OneTimeLink", name!, StringComparison.Ordinal));
    }

    [Fact]
    public void BrowserOnlyInput_HasNoBindingEventOrRef()
    {
        var source = File.ReadAllText(Path.Combine(UiSharedRoot, "Components", "Common", "SvBrowserOnlyInput.razor"));

        Assert.DoesNotMatch(BindingDirective(), source);
    }

    [Fact]
    public void CreateView_HasNoDialogOrKeyHandler()
    {
        var source = File.ReadAllText(Path.Combine(UiSharedRoot, "Components", "OneTimeLinks", CreateViewFile))
            + File.ReadAllText(Path.Combine(UiSharedRoot, "Components", "OneTimeLinks", CreateViewFile + ".cs"));

        Assert.DoesNotContain("SvDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@onkey", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KeyboardEventArgs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenPage_IsExcludedFromInteractiveRoutingAndAllowsAnonymous()
    {
        var source = File.ReadAllText(Path.Combine(WebRoot, "Components", "Pages", "OneTimeLinkOpen.razor"));

        Assert.Contains("@attribute [AllowAnonymous]", source, StringComparison.Ordinal);
        Assert.Contains("@attribute [ExcludeFromInteractiveRouting]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IOneTimeLinkCrypto_NoMemberReturnsKeyMaterial()
    {
        var names = typeof(IOneTimeLinkCrypto).GetMethods()
            .SelectMany(method => new[] { method.Name, method.ReturnType.Name }
                .Concat(method.ReturnType.GetGenericArguments().SelectMany(argument => argument.GetProperties().Select(property => property.Name))))
            .Concat(typeof(OneTimeLinkSeal).GetProperties().Select(property => property.Name));

        Assert.DoesNotContain(names, name => name.Contains("Key", StringComparison.OrdinalIgnoreCase) || name.Contains("Fragment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpenHandler_DoesNotDependOnICurrentUserOrIEnvelopeEncryptor()
    {
        var dependencies = typeof(OpenOneTimeLinkCommandHandler)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .ToList();

        Assert.NotEmpty(dependencies);
        Assert.DoesNotContain("ICurrentUser", dependencies);
        Assert.DoesNotContain("IEnvelopeEncryptor", dependencies);
    }

    [Fact]
    public void AppSettings_NeverContainTheGatewayKey()
    {
        var files = RepositoryRoot.EnumerateFiles("appsettings*.json");

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));

            Assert.False(ContainsProperty(document.RootElement, GatewayKeyProperty), file);
            Assert.False(HasWebGatewayKey(document.RootElement), file);
        }
    }

    private static bool ContainsProperty(JsonElement element, string name)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Any(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) || ContainsProperty(property.Value, name)),
            JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsProperty(item, name)),
            _ => false,
        };
    }

    private static bool HasWebGatewayKey(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, WebGatewaySection, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Object
                && property.Value.EnumerateObject().Any(child => string.Equals(child.Name, KeyProperty, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (HasWebGatewayKey(property.Value))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"@(bind|on|ref)", RegexOptions.IgnoreCase)]
    private static partial Regex BindingDirective();
}
