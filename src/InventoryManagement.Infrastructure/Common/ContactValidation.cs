using System.Text.RegularExpressions;

namespace InventoryManagement.Infrastructure.Common;

/// <summary>
/// Basic format validation shared by Supplier and Customer - both have the
/// same "name required, email must look like an email if given" shape.
/// Deliberately a simple pattern (not full RFC 5322) since form-level
/// sanity-checking, not strict protocol validation, is the goal here.
/// </summary>
internal static class ContactValidation
{
    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool LooksLikeValidEmail(string email) => EmailPattern.IsMatch(email);
}
