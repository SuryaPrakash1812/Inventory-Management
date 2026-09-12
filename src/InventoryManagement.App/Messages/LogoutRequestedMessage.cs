namespace InventoryManagement.App.Messages;

/// <summary>
/// Sent by <c>ShellViewModel</c>'s logout command. App.xaml.cs listens for
/// this to close the main shell and show the login window again, without
/// the ShellViewModel needing to know anything about window lifetimes.
/// </summary>
public sealed class LogoutRequestedMessage
{
}
