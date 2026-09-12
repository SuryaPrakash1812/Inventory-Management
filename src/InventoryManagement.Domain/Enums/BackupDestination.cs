namespace InventoryManagement.Domain.Enums;

/// <summary>Where a backup was (or should be) written to.</summary>
public enum BackupDestination
{
    Local = 0,
    GoogleDrive = 1,
    OneDrive = 2,
}
