using Gridlet;
using Gridlet.AspNetCore.Extensibility;
using Gridlet.Components;
using Gridlet.Components.Endpoints;
using Gridlet.Components.Storage;
using Gridlet.Components.UI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace; conventional namespace for DI extensions.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration extensions for the Gridlet components designer.</summary>
public static class GridletComponentsBuilderExtensions
{
    /// <summary>
    /// Adds the components designer: its API endpoints, its browser assets, and a file-backed store for
    /// component documents. The designer appears in the Gridlet UI only when this is registered.
    /// </summary>
    /// <param name="builder">The Gridlet builder returned by <c>AddGridlet()</c>.</param>
    /// <param name="configure">Optional callback that overrides where component documents are stored.</param>
    public static GridletBuilder AddComponents(
        this GridletBuilder builder,
        Action<GridletComponentsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure(configure ?? (_ => { }));
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GridletComponentsOptions>, GridletComponentsOptionsValidator>());
        builder.Services.TryAddSingleton<IComponentStore, GridletComponentFileStore>();
        builder.Services.TryAddSingleton<IComponentScriptStore, GridletComponentScriptFileStore>();
        builder.Services.AddSingleton<IGridletEndpointContributor, GridletComponentEndpoints>();
        builder.Services.AddSingleton<IGridletRuntimeContributor, GridletComponentEndpoints>();
        builder.Services.AddSingleton<IGridletRootRuntimeContributor, GridletComponentEndpoints>();
        builder.Services.AddSingleton<IGridletRuntimeRouteMetadata, GridletComponentEndpoints>();
        builder.Services.AddSingleton<IGridletUiAssetProvider, GridletComponentsAssetProvider>();
        return builder;
    }
}
