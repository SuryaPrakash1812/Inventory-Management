using InventoryManagement.Infrastructure.Auth;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Auth;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _sut = new();

    [Fact]
    public void HashPassword_NeverReturnsThePlaintextPassword()
    {
        const string password = "CorrectHorseBatteryStaple1";

        var hash = _sut.HashPassword(password);

        Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
    }

    [Fact]
    public void HashPassword_ProducesADifferentHashEachTime()
    {
        const string password = "CorrectHorseBatteryStaple1";

        var hash1 = _sut.HashPassword(password);
        var hash2 = _sut.HashPassword(password);

        // Different random salts each time, even for the same password -
        // otherwise two users with the same password would have identical
        // hash rows, leaking that fact to anyone with database access.
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ReturnsTrue()
    {
        var hash = _sut.HashPassword("CorrectHorseBatteryStaple1");

        Assert.True(_sut.VerifyPassword("CorrectHorseBatteryStaple1", hash));
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_ReturnsFalse()
    {
        var hash = _sut.HashPassword("CorrectHorseBatteryStaple1");

        Assert.False(_sut.VerifyPassword("WrongPassword", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-hash")]
    [InlineData("v1:not-a-number:salt:hash")]
    [InlineData("v2:210000:c2FsdA==:aGFzaA==")]
    public void VerifyPassword_WithMalformedOrUnknownFormatHash_ReturnsFalseRatherThanThrowing(string malformedHash)
    {
        Assert.False(_sut.VerifyPassword("anything", malformedHash));
    }
}
