// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;

namespace SharpVault.Authentication.Local;

public static class LocalSigningKeyStore
{
    private const string VendorFolder = "SharpVault";
    private const string KeyFileName = "dev-auth-key";
    private const string TempSuffix = ".tmp";
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode GroupAndOtherBits =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), VendorFolder, KeyFileName);

    public static LocalSigningKey LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            Create(path);
        }

        return Read(path);
    }

    private static void Create(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        CreateDirectory(directory);

        var temporary = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}{TempSuffix}");
        var material = RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength);
        var encoded = Convert.ToBase64String(material);
        CryptographicOperations.ZeroMemory(material);
        try
        {
            WriteNew(temporary, encoded);
            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void CreateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        Directory.CreateDirectory(directory, OwnerOnlyDirectory);
    }

    private static void WriteNew(string path, string content)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFile;
        }

        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string ReadText(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read | FileShare.Delete,
        };

        using var stream = new FileStream(path, options);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static LocalSigningKey Read(string path)
    {
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & GroupAndOtherBits) != 0)
        {
            throw new InvalidOperationException(LocalAuthenticationMessages.KeyFileTooOpen + path);
        }

        var text = ReadText(path).Trim();
        var bytes = new byte[LocalAuthenticationDefaults.KeyLength + 1];
        try
        {
            if (text.Length == 0
                || !Convert.TryFromBase64String(text, bytes, out var written)
                || written != LocalAuthenticationDefaults.KeyLength)
            {
                throw new InvalidOperationException(LocalAuthenticationMessages.KeyFileInvalid + path);
            }

            var slice = bytes[..written];
            try
            {
                return new LocalSigningKey(slice);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(slice);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
