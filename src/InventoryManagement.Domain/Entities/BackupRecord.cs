using InventoryManagement.Domain.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// A record of one backup attempt (local or cloud), written by the backup
/// engine (Stage 11-12). Like other ledger-style entities, history here is
/// never edited after the fact.
/// </summary>
public class BackupRecord : BaseEntity
{
    public DateTimeOffset CreatedAtUtc { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public BackupDestination Destination { get; set; }

    public BackupStatus Status { get; set; }

    public bool IsAutomatic { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }

    public string? Notes { get; set; }
}
