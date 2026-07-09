namespace Gridlet;

/// <summary>Base type for all Gridlet-specific exceptions.</summary>
public class GridletException : Exception
{
    public GridletException(string message) : base(message) { }
    public GridletException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>Thrown when a request references a connection name that is not configured.</summary>
public sealed class GridletUnknownConnectionException(string connectionName)
    : GridletException($"No Gridlet connection named '{connectionName}' is configured.")
{
    public string ConnectionName { get; } = connectionName;
}

/// <summary>Thrown when a connection references a provider that is not registered.</summary>
public sealed class GridletUnknownProviderException(string providerName)
    : GridletException($"No Gridlet provider named '{providerName}' is registered. Did you forget to call Add{providerName}() on the Gridlet builder?")
{
    public string ProviderName { get; } = providerName;
}

/// <summary>Thrown when a requested database object does not exist.</summary>
public sealed class GridletObjectNotFoundException(string objectName)
    : GridletException($"Database object '{objectName}' was not found.")
{
    public string ObjectName { get; } = objectName;
}

/// <summary>Thrown when request input is invalid (bad sort column, malformed identifier, ...).</summary>
public sealed class GridletValidationException(string message) : GridletException(message);

/// <summary>Thrown when the database rejects a user-authored query. Carries a user-presentable message.</summary>
public sealed class GridletQueryException(string message, Exception? innerException = null)
    : GridletException(message, innerException);
