namespace InventoryManagement.Domain.Enums;

/// <summary>Outcome of a backup attempt.</summary>
public enum BackupStatus
{
    InProgress = 0,
    Success = 1,
    Failed = 2,
}
