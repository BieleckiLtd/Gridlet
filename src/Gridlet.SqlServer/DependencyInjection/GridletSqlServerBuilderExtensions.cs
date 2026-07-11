using Gridlet;
using Gridlet.Abstractions;
using Gridlet.SqlServer;
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
}
