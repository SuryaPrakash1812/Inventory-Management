namespace InventoryManagement.Application.Auth;

/// <summary>
/// Hashes and verifies passwords. This is the only place in the application
/// allowed to know how that hashing actually works - nothing else should
/// implement its own hashing, and a plaintext password must never be stored
/// or logged anywhere.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Produces a self-describing hash string (algorithm + parameters + salt + hash), safe to store as-is.</summary>
    string HashPassword(string password);

    /// <summary>Verifies a password against a hash previously produced by <see cref="HashPassword"/>.</summary>
    bool VerifyPassword(string password, string hashedPassword);
}
