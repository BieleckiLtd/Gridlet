using Gridlet;
using Gridlet.Abstractions;
using Gridlet.AspNetCore;
using Gridlet.AspNetCore.Agents;
using Gridlet.AspNetCore.Extensibility;
using Microsoft.AspNetCore.Http;
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
        MapPublishedEndpoints(endpoints, group, options, normalizedPattern);
        MapRuntimeEndpoints(endpoints, group);
        MapRootRuntimeEndpoints(endpoints, options, normalizedPattern);

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
        MapPublishedEndpoints(endpoints, group, options, normalizedPattern: "/" + pattern.Trim('/'));

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
    /// <see cref="GridletOptions.PublishedApiRoutePrefix"/> beneath it, unless
    /// <see cref="GridletOptions.PublishedApiPath"/> selects an application-root path. Defaults
    /// to <c>/gridlet</c>.
    /// </param>
    /// <returns>The mapped route group, allowing additional endpoint conventions to be applied.</returns>
    public static IEndpointConventionBuilder MapGridletPublished(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/gridlet")
    {
        var (group, options, _) = CreateGroup(
            endpoints, pattern, validateAgentService: false);

        MapPublishedEndpoints(endpoints, group, options, normalizedPattern: "/" + pattern.Trim('/'));

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

    private static void MapRuntimeEndpoints(IEndpointRouteBuilder endpoints, RouteGroupBuilder group)
    {
        foreach (var contributor in endpoints.ServiceProvider.GetServices<IGridletRuntimeContributor>())
        {
            contributor.Map(group);
        }
    }

    private static void MapRootRuntimeEndpoints(
        IEndpointRouteBuilder endpoints, GridletOptions options, string normalizedMount)
    {
        if (!GridletRoutePath.TryNormalize(normalizedMount, out var managementPath, allowEmpty: true))
        {
            throw new InvalidOperationException(
                "The Gridlet management mount must be a safe route path.");
        }

        foreach (var contributor in endpoints.ServiceProvider
                     .GetServices<IGridletRootRuntimeContributor>())
        {
            if (contributor.RootPath is not { Length: > 0 } configuredPath)
            {
                continue;
            }

            if (!GridletRoutePath.TryNormalize(configuredPath, out var path))
            {
                throw new InvalidOperationException(
                    "A root runtime contributor must expose a safe, non-empty absolute route path.");
            }

            if (GridletRoutePath.IsEqualOrAncestor(path, managementPath) ||
                GridletRoutePath.IsEqualOrAncestor(managementPath, path))
            {
                throw new InvalidOperationException(
                    $"Root runtime path '/{path}' collides with Gridlet's management mount " +
                    $"'/{managementPath}'.");
            }

            var root = CreateAuthorizedGroup(endpoints, path, options);
            contributor.MapAtRoot(root);
        }
    }

    private static void MapPublishedEndpoints(
        IEndpointRouteBuilder endpoints,
        RouteGroupBuilder group,
        GridletOptions options,
        string normalizedPattern)
    {
        if (options.PublishedApiPath is null)
        {
            GridletPublishedEndpoints.Map(group, options.PublishedApiSegment);
            return;
        }

        if (!GridletRoutePath.TryNormalize(normalizedPattern, out var managementPath, allowEmpty: true))
        {
            throw new InvalidOperationException(
                "The Gridlet management mount must be a safe route path.");
        }

        if (!GridletRoutePath.TryNormalize(options.PublishedApiPath, out var path))
        {
            throw new InvalidOperationException(
                "PublishedApiPath must be a safe absolute route path.");
        }

        if (GridletRoutePath.IsEqualOrAncestor(path, managementPath) ||
            GridletRoutePath.IsEqualOrAncestor(managementPath, path))
        {
            throw new InvalidOperationException(
                $"PublishedApiPath '/{path}' collides with Gridlet's management mount " +
                $"'/{managementPath}'.");
        }

        var root = CreateAuthorizedGroup(endpoints, path, options);
        GridletPublishedEndpoints.Map(root, string.Empty);
    }

    private static RouteGroupBuilder CreateAuthorizedGroup(
        IEndpointRouteBuilder endpoints, string path, GridletOptions options)
    {
        var root = endpoints.MapGroup("/" + path.Trim('/'));
        root.AddEndpointFilter(GridletEndpointHelpers.PublishRequestLogger);
        ApplyAuthorization(root, options);
        return root;
    }

    private static void ApplyAuthorization(RouteGroupBuilder group, GridletOptions options)
    {
        if (options.Security.AuthorizationPolicy is { Length: > 0 } policy)
        {
            group.RequireAuthorization(policy);
        }
        else if (!options.Security.AllowAnonymous)
        {
            group.RequireAuthorization();
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

        // Unexpected endpoint failures are returned to the caller and logged as well. The filter
        // runs for every endpoint in the group and supplies the request's own logger factory.
        group.AddEndpointFilter(GridletEndpointHelpers.PublishRequestLogger);
        ApplyAuthorization(group, options);

        return (group, options, pattern);
    }
}
