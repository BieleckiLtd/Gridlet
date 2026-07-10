using Gridlet;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace — conventional namespace for DI extensions.
namespace Microsoft.Extensions.DependencyInjection;

public static class GridletServiceCollectionExtensions
{
    /// <summary>
    /// Adds Gridlet to the host. Chain a provider registration afterwards, e.g.
    /// <c>services.AddGridlet(o =&gt; o.AddConnection("Default", cs)).AddSqlServer();</c>
    /// then map the UI and API with <c>app.MapGridlet()</c>.
    /// </summary>
    public static GridletBuilder AddGridlet(
        this IServiceCollection services,
        Action<GridletOptions>? configure = null)
    {
        var builder = services.AddGridletCore(configure);

        // Default file-backed store for saved queries and published endpoints; both interfaces
        // share one instance so they share one state file.
        services.TryAddSingleton<GridletFileStore>();
        services.TryAddSingleton<ISavedQueryStore>(sp => sp.GetRequiredService<GridletFileStore>());
        services.TryAddSingleton<IPublishedEndpointStore>(sp => sp.GetRequiredService<GridletFileStore>());

        return builder;
    }
}
