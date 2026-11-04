namespace InventoryManagement.Infrastructure.Common;

/// <summary>
/// Builds the pattern for case-insensitive "contains" searches via
/// EF.Functions.Like(column, SearchPattern.Contains(term), SearchPattern.Escape).
///
/// Plain string.Contains is translated by the SQLite provider to instr(),
/// which is CASE-SENSITIVE - typing "radha" would not find "Radha steel".
/// SQLite's LIKE ignores case (for ASCII letters). The user's own % and _
/// characters are escaped so they are matched literally rather than acting
/// as wildcards.
/// </summary>
public static class SearchPattern
{
    public const string Escape = "\\";

    public static string Contains(string term) =>
        "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
