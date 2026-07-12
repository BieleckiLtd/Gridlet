using Gridlet;
using Gridlet.Abstractions;
using Gridlet.Sqlite;
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
}
