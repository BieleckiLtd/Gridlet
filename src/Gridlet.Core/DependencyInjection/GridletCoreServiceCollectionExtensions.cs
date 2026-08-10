using Gridlet;
using Gridlet.Abstractions;
using Gridlet.Auditing;
using Gridlet.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace; conventional namespace for DI extensions.
namespace Microsoft.Extensions.DependencyInjection;

public static class GridletCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers Gridlet's core services (options, provider registry, connection resolver,
    /// audit sink). Host-facing packages such as Gridlet.AspNetCore call this internally;
    /// use their <c>AddGridlet(...)</c> entry point instead.
    /// </summary>
    public static GridletBuilder AddGridletCore(
        this IServiceCollection services,
        Action<GridletOptions>? configure = null)
    {
        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GridletOptions>, GridletOptionsValidator>());
        services.TryAddSingleton<IGridletProviderRegistry, GridletProviderRegistry>();
        services.TryAddSingleton<IGridletConnectionResolver, GridletConnectionResolver>();
        services.TryAddSingleton<IGridletAuditSink, LoggingGridletAuditSink>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<GridletQuerySessionManager>();

        return new GridletBuilder(services);
    }
}
