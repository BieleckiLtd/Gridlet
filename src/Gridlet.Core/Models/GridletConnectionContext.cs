namespace Gridlet.Models;

/// <summary>
/// Everything a provider needs to execute one operation: the configured connection
/// plus the database the operation targets (<c>null</c> = the connection string's default).
/// </summary>
public sealed record GridletConnectionContext(GridletConnectionOptions Connection, string? Database)
{
    public string ConnectionName => Connection.Name;

    public string ConnectionString => Connection.ConnectionString;
}
