// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.E2ETests.Hosting;

public static class RepositoryLocator
{
    public const string SolutionFileName = "SharpVault.slnx";

    public static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"{SolutionFileName} was not found above {AppContext.BaseDirectory}.");
    }

    public static string BuildConfiguration() => new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
}
