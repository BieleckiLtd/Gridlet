using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

internal static class SqlServerConnectionFactory
{
    /// <summary>
    /// Opens a connection for the context, retargeting the initial catalog when the
    /// context names a database. The database name travels through
    /// <see cref="SqlConnectionStringBuilder"/>, never through string concatenation.
    /// </summary>
    public static async Task<SqlConnection> OpenAsync(GridletConnectionContext context, CancellationToken cancellationToken)
        => await OpenAsync(context, pooling: true, cancellationToken);

    /// <summary>
    /// Opens a connection that is physically closed on disposal. Use this when a short-lived
    /// operation changes session state that SQL Server connection-pool reset does not restore.
    /// </summary>
    public static async Task<SqlConnection> OpenUnpooledAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken)
        => await OpenAsync(context, pooling: false, cancellationToken);

    private static async Task<SqlConnection> OpenAsync(
        GridletConnectionContext context,
        bool pooling,
        CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(context.ConnectionString);
        builder.Pooling = pooling;
        if (!string.IsNullOrEmpty(context.Database))
        {
            builder.InitialCatalog = context.Database;
        }

        var connection = new SqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
