using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace InventoryManagement.Infrastructure.Security;

/// <summary>Thrown when the database key exists but this PC cannot decrypt it (e.g. the files were copied from another PC).</summary>
public sealed class DatabaseKeyUnavailableException : Exception
{
    public DatabaseKeyUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Holds the random 256-bit key the local database is encrypted with
/// (SQLCipher). The key is stored on disk only in Windows DPAPI-protected
/// form, scoped to THIS MACHINE: copying inventory.db together with its key
/// file to another PC leaves both unreadable there, because only the
/// original PC can unprotect the key.
///
/// LocalMachine scope rather than CurrentUser on purpose: a CurrentUser key
/// is lost if an administrator resets the Windows user's password, which
/// would make the customer's own data permanently unreadable. The data
/// folder is already per-user (AppData), so machine scope gives up nothing
/// that matters here.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DatabaseKeyProvider
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("InventoryManagement.DatabaseKey.v1");

    private readonly string _keyFilePath;

    public DatabaseKeyProvider(string keyFilePath)
    {
        _keyFilePath = keyFilePath;
    }

    public bool KeyExists => File.Exists(_keyFilePath);

    /// <summary>Returns the database password, creating (and persisting) a new key on first use.</summary>
    public string GetOrCreatePassword()
    {
        if (KeyExists)
        {
            return ReadPassword();
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.LocalMachine);

        Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath)!);
        var tempPath = _keyFilePath + ".tmp";
        File.WriteAllBytes(tempPath, protectedKey);
        File.Move(tempPath, _keyFilePath, overwrite: true);

        return Convert.ToHexString(key);
    }

    private string ReadPassword()
    {
        try
        {
            var key = ProtectedData.Unprotect(File.ReadAllBytes(_keyFilePath), Entropy, DataProtectionScope.LocalMachine);
            return Convert.ToHexString(key);
        }
        catch (CryptographicException ex)
        {
            throw new DatabaseKeyUnavailableException(
                "The database key on this PC could not be unlocked. The data files appear to have been copied from another PC.",
                ex);
        }
    }
}
