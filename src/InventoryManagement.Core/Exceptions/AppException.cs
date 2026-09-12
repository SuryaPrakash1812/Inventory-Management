namespace InventoryManagement.Core.Exceptions;

/// <summary>
/// Base type for all deliberately-thrown application exceptions. Distinguishing
/// these from framework/BCL exceptions lets the UI layer decide, at a glance,
/// whether an exception is "expected" (show a friendly message) or "unexpected"
/// (log with full detail and show a generic error).
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message) : base(message)
    {
    }

    protected AppException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a requested entity does not exist.
/// </summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string entityName, object key)
        : base($"{entityName} with key '{key}' was not found.")
    {
    }
}

/// <summary>
/// Thrown when an operation would violate a business rule.
/// </summary>
public sealed class BusinessRuleException : AppException
{
    public BusinessRuleException(string message) : base(message)
    {
    }
}
