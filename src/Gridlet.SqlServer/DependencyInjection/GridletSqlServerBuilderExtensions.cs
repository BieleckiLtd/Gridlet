using Gridlet;
using Gridlet.Abstractions;
using Gridlet.SqlServer;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace — conventional namespace for DI extensions.
namespace Microsoft.Extensions.DependencyInjection;

public static class GridletSqlServerBuilderExtensions
{
    /// <summary>Registers the SQL Server provider with Gridlet.</summary>
    public static GridletBuilder AddSqlServer(this GridletBuilder builder)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IGridletProvider, SqlServerGridletProvider>());
        return builder;
    }
}
