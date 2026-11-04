namespace InventoryManagement.Domain.Common;

/// <summary>Records why a document was cancelled in its notes, for Purchases, Sales and Stock Adjustments alike.</summary>
public static class CancellationNote
{
    /// <summary>Matches the Notes column length on all three documents.</summary>
    private const int MaxNotesLength = 2000;

    public static string Append(string? notes, string reason, DateTimeOffset cancelledAt)
    {
        var note = $"Cancelled {cancelledAt.ToLocalTime():dd MMM yyyy}: {reason}";
        var combined = string.IsNullOrWhiteSpace(notes) ? note : $"{notes}\n{note}";
        return combined.Length <= MaxNotesLength ? combined : combined[..MaxNotesLength];
    }
}
