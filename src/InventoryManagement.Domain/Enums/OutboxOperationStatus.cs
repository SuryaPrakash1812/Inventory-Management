namespace InventoryManagement.Domain.Enums;

/// <summary>Lifecycle of a single Outbox entry, from local creation through to server acknowledgement.</summary>
public enum OutboxOperationStatus
{
    Pending = 0,
    Processing = 1,
    Synced = 2,
    Failed = 3,
}
