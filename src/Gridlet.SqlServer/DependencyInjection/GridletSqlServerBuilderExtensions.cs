using Gridlet;
using Gridlet.Abstractions;
using Gridlet.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace — conventional namespace for DI extensions.
namespace Microsoft.Extensions.DependencyInjection;

public static class GridletSqlServerBuilderExtensions
{
    /// <summary>
    /// Registers the SQL Server provider under <see cref="GridletProviderNames.SqlServer"/>.
    /// Chain this after <c>AddGridlet</c> when any configured connection uses the SQL Server
    /// provider name.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddGridlet</c>.</param>
    /// <returns>The same builder for further registration chaining.</returns>
    public static GridletBuilder AddSqlServer(this GridletBuilder builder)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IGridletProvider, SqlServerGridletProvider>());
        return builder;
    }

    /// <summary>
    /// Adds a SQL Server connection and registers its provider. The connection label is derived
    /// from the server, and the initial database becomes the UI default.
    /// </summary>
    public static GridletBuilder AddSqlServer(
        this GridletBuilder builder,
        string? connectionString,
        Action<GridletConnectionOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new GridletValidationException("A SQL Server connection string is required.");
        }

        builder.AddSqlServer();
        builder.Services.Configure<GridletOptions>(options =>
        {
            var parsed = new SqlConnectionStringBuilder(connectionString);
            var server = string.IsNullOrWhiteSpace(parsed.DataSource) ? "SQL Server" : parsed.DataSource;
            options.AddConnection(UniqueLabel(options, server), connectionString,
                GridletProviderNames.SqlServer, connection =>
                {
                    connection.DefaultDatabase = string.IsNullOrWhiteSpace(parsed.InitialCatalog)
                        ? null
                        : parsed.InitialCatalog;
                    configure?.Invoke(connection);
                });
        });
        return builder;
    }

    /// <summary>Adds a SQL Server connection from the standard ConnectionStrings section.</summary>
    public static GridletBuilder AddSqlServer(
        this GridletBuilder builder,
        IConfiguration configuration,
        string connectionStringName,
        Action<GridletConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(connectionStringName))
        {
            throw new GridletValidationException("A SQL Server connection-string name is required.");
        }

        return builder.AddSqlServer(
            configuration.GetConnectionString(connectionStringName)
                ?? throw new GridletValidationException(
                    $"ConnectionStrings:{connectionStringName} is not configured."),
            configure);
    }

    private static string UniqueLabel(GridletOptions options, string label)
    {
        var candidate = label;
        for (var suffix = 2; options.Connections.Any(connection =>
                 string.Equals(connection.Name, candidate, StringComparison.OrdinalIgnoreCase)); suffix++)
        {
            candidate = $"{label} ({suffix})";
        }

        return candidate;
    }
}
