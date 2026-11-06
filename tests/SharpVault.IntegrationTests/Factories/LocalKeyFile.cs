// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Factories;

public static class LocalKeyFile
{
    private static readonly string RootDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sharpvault-tests-" + Guid.NewGuid().ToString("N"));

    static LocalKeyFile()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteRootDirectory();
    }

    public static string Path { get; } = System.IO.Path.Combine(RootDirectory, "dev-auth-key");

    private static void DeleteRootDirectory()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
