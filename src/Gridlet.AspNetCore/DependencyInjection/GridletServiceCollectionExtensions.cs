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
    /// <c>services.AddGridlet(o =&gt; o.AddConnection("Default", cs, GridletProviderNames.SqlServer)).AddSqlServer();</c>
    /// then map the UI and API with <c>app.MapGridlet()</c>.
    /// </summary>
    /// <param name="services">The host application's service collection.</param>
    /// <param name="configure">
    /// Optional callback that defines the connection allow-list, security, limits, and storage.
    /// Configuration is validated when <c>MapGridlet</c> resolves it at startup.
    /// </param>
    /// <returns>A builder used to chain provider registrations such as <c>AddSqlServer()</c>.</returns>
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
