using Gridlet;

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
        => services.AddGridletCore(configure);
}
