using Gridlet;
using Gridlet.Abstractions;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace — conventional namespace for DI extensions.
namespace Microsoft.Extensions.DependencyInjection;

public static class GridletSqliteBuilderExtensions
{
    /// <summary>Registers the SQLite provider under <see cref="GridletProviderNames.Sqlite"/>.</summary>
    public static GridletBuilder AddSqlite(this GridletBuilder builder)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IGridletProvider, SqliteGridletProvider>());
        return builder;
    }

    /// <summary>
    /// Adds a SQLite connection and registers its provider. The connection label is derived from
    /// the database filename in <paramref name="connectionString"/>.
    /// </summary>
    public static GridletBuilder AddSqlite(
        this GridletBuilder builder,
        string? connectionString,
        Action<GridletConnectionOptions>? configure = null,
        string? relativePathBase = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new GridletValidationException("A SQLite connection string is required.");
        }

        builder.AddSqlite();
        builder.Services.Configure<GridletOptions>(options =>
        {
            var parsed = new SqliteConnectionStringBuilder(connectionString);
            if (!string.IsNullOrWhiteSpace(relativePathBase) &&
                !string.Equals(parsed.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) &&
                !Path.IsPathRooted(parsed.DataSource))
            {
                parsed.DataSource = Path.Combine(relativePathBase, parsed.DataSource);
            }

            var normalizedConnectionString = parsed.ConnectionString;
            var dataSource = parsed.DataSource;
            var filename = Path.GetFileName(dataSource);
            var label = !string.IsNullOrWhiteSpace(filename)
                ? filename
                : !string.IsNullOrWhiteSpace(dataSource) ? dataSource : "SQLite";
            options.AddConnection(UniqueLabel(options, label), normalizedConnectionString,
                GridletProviderNames.Sqlite, connection =>
                {
                    connection.DefaultDatabase = "main";
                    configure?.Invoke(connection);
                });
        });
        return builder;
    }

    /// <summary>Adds a SQLite connection from the standard ConnectionStrings section.</summary>
    public static GridletBuilder AddSqlite(
        this GridletBuilder builder,
        IConfiguration configuration,
        string connectionStringName,
        Action<GridletConnectionOptions>? configure = null,
        string? relativePathBase = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(connectionStringName))
        {
            throw new GridletValidationException("A SQLite connection-string name is required.");
        }

        return builder.AddSqlite(
            configuration.GetConnectionString(connectionStringName)
                ?? throw new GridletValidationException(
                    $"ConnectionStrings:{connectionStringName} is not configured."),
            configure,
            relativePathBase);
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
