// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;

namespace SharpVault.UI.Shared.Navigation;

public sealed class RoutedAssemblies(IReadOnlyList<Assembly> assemblies)
{
    public IReadOnlyList<Assembly> Assemblies { get; } = assemblies;
}
