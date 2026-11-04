namespace InventoryManagement.App.Messages;

/// <summary>
/// Sent by a page after it reloads data that can change stock levels, so the
/// shell's low-stock alerts (the bell) stay current without a page change.
/// </summary>
public sealed record StockDataChangedMessage;
