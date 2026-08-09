using Gridlet;
using Gridlet.Abstractions;
using Gridlet.AgentFramework;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace; conventional namespace for DI extensions.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration extensions for Gridlet's Microsoft Agent Framework integration.</summary>
public static class GridletAgentFrameworkBuilderExtensions
{
    /// <summary>
    /// Registers the optional database conversation service and its host-controlled model profiles.
    /// </summary>
    public static GridletBuilder AddAgentFramework(
        this GridletBuilder builder,
        Action<GridletAgentFrameworkOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GridletAgentFrameworkOptions();
        configure(options);
        var settings = options.Build();

        builder.Services.AddSingleton(settings);
        builder.Services.TryAddSingleton<EphemeralCredentialStore>();
        // One registry serves every conversation: the browser answers an access prompt on a
        // separate request from the turn that is waiting for it.
        builder.Services.TryAddSingleton<GridletAgentPermissionRegistry>();
        builder.Services.TryAddSingleton<IGridletAgentPermissionBroker>(provider =>
            provider.GetRequiredService<GridletAgentPermissionRegistry>());
        builder.Services.TryAddSingleton<IGridletAgentService, GridletAgentFrameworkService>();
        return builder;
    }
}
