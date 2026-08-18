using Gridlet;
using Gridlet.Abstractions;
using Gridlet.AspNetCore;
using Gridlet.AspNetCore.Agents;
using Gridlet.AspNetCore.Extensibility;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace; conventional namespace for endpoint extensions.
namespace Microsoft.AspNetCore.Builder;

public static class GridletEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Gridlet UI and API under <paramref name="pattern"/>. A configured authorization
    /// policy always applies. Otherwise, every endpoint requires the host's default authorization
    /// policy unless <see cref="GridletSecurityOptions.AllowAnonymous"/> is set.
    /// </summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <param name="pattern">
    /// Route prefix for the UI and all Gridlet APIs. Defaults to <c>/gridlet</c>. A leading or
    /// trailing slash is optional; Gridlet normalizes the value.
    /// </param>
    /// <returns>The mapped route group, allowing additional endpoint conventions to be applied.</returns>
    public static IEndpointConventionBuilder MapGridlet(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/gridlet")
    {
        var (group, options, normalizedPattern) = CreateGroup(
            endpoints, pattern, validateAgentService: true);

        GridletUiEndpoints.Map(group, normalizedPattern);
        var api = group.MapGroup("/api");
        GridletApiEndpoints.Map(api, options);
        MapModuleEndpoints(endpoints, api);
        GridletPublishedEndpoints.Map(group, options.PublishedApiSegment);

        return group;
    }

    /// <summary>
    /// Maps the Gridlet management API and published endpoint runtime without serving the embedded UI
    /// or its assets. Authorization and startup validation are identical to <see cref="MapGridlet"/>.
    /// </summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <param name="pattern">Route prefix for the API and published endpoints. Defaults to <c>/gridlet</c>.</param>
    /// <returns>The mapped route group, allowing additional endpoint conventions to be applied.</returns>
    public static IEndpointConventionBuilder MapGridletApi(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/gridlet")
    {
        var (group, options, _) = CreateGroup(
            endpoints, pattern, validateAgentService: true);

        var api = group.MapGroup("/api");
        GridletApiEndpoints.Map(api, options);
        MapModuleEndpoints(endpoints, api);
        GridletPublishedEndpoints.Map(group, options.PublishedApiSegment);

        return group;
    }

    /// <summary>
    /// Maps only the published endpoint runtime, without the management API, embedded UI, or assets.
    /// Authorization remains secure by default and a configured named policy takes precedence over
    /// <see cref="GridletSecurityOptions.AllowAnonymous"/>, matching <see cref="MapGridlet"/>.
    /// </summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <param name="pattern">
    /// Route prefix containing the published-endpoint runtime, which is served from
    /// <see cref="GridletOptions.PublishedApiRoutePrefix"/> beneath it. Defaults to <c>/gridlet</c>.
    /// </param>
    /// <returns>The mapped route group, allowing additional endpoint conventions to be applied.</returns>
    public static IEndpointConventionBuilder MapGridletPublished(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/gridlet")
    {
        var (group, options, _) = CreateGroup(
            endpoints, pattern, validateAgentService: false);

        GridletPublishedEndpoints.Map(group, options.PublishedApiSegment);

        return group;
    }

    /// <summary>
    /// Gives every installed optional package a chance to add endpoints inside Gridlet's authorized
    /// API group. Nothing happens when no module package is referenced.
    /// </summary>
    private static void MapModuleEndpoints(IEndpointRouteBuilder endpoints, RouteGroupBuilder api)
    {
        foreach (var contributor in endpoints.ServiceProvider.GetServices<IGridletEndpointContributor>())
        {
            contributor.Map(api);
        }
    }

    private static (RouteGroupBuilder Group, GridletOptions Options, string Pattern) CreateGroup(
        IEndpointRouteBuilder endpoints,
        string pattern,
        bool validateAgentService)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        pattern = "/" + pattern.Trim('/');

        // Resolving options here validates the configuration at startup rather than on first request.
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<GridletOptions>>().Value;
        // Provider instances are resolved only after the normal options pipeline has completed.
        // This keeps providers free to depend on IOptions<GridletOptions> without creating a cycle.
        var providerValidation = new GridletProviderRegistrationValidator(
            endpoints.ServiceProvider.GetRequiredService<IGridletProviderRegistry>())
            .Validate(Options.DefaultName, options);
        if (providerValidation.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(GridletOptions),
                providerValidation.Failures);
        }

        if (validateAgentService)
        {
            // Mappings that expose agent endpoints fail fast on invalid provider profiles. The
            // published-only runtime has no agent surface and deliberately avoids initializing it.
            _ = endpoints.ServiceProvider.GetService<IGridletAgentService>()?.Info;
        }

        // The prefix is a mapping-time choice, so it is published to services here rather than
        // being guessed from the documented default when an agent needs to name a real URL.
        endpoints.ServiceProvider.GetService<GridletMountPath>()?.Set(pattern);

        var group = endpoints.MapGroup(pattern);

        if (options.Security.AuthorizationPolicy is { Length: > 0 } policy)
        {
            // An explicitly selected policy is the strongest signal and always wins, even if
            // AllowAnonymous was also set (for example by a development configuration layer).
            group.RequireAuthorization(policy);
        }
        else if (!options.Security.AllowAnonymous)
        {
            group.RequireAuthorization();
        }

        return (group, options, pattern);
    }
}
