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
    {
        var builder = new SqlConnectionStringBuilder(context.ConnectionString);
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
