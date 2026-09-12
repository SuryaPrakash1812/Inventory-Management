using System.Security.Cryptography;
using InventoryManagement.Application.Auth;

namespace InventoryManagement.Infrastructure.Auth;

/// <summary>
/// Hashes passwords with PBKDF2-SHA256 (built into .NET, no extra dependency
/// needed). The stored string is self-describing -
/// "v1:{iterations}:{base64 salt}:{base64 hash}" - so the iteration count
/// can be increased later for new passwords without breaking verification
/// of passwords hashed under the old count, and so a future algorithm change
/// only needs a new version prefix, not a data migration.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string FormatVersion = "v1";
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    // OWASP's current minimum recommendation for PBKDF2-HMAC-SHA256.
    private const int Iterations = 210_000;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSizeBytes);

        return string.Join(
            ':',
            FormatVersion,
            Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        var parts = hashedPassword.Split(':');
        if (parts.Length != 4 || parts[0] != FormatVersion)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expectedHash.Length);

        // Constant-time comparison - a plain array/string equality check here
        // would leak how many leading bytes matched via response timing.
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
