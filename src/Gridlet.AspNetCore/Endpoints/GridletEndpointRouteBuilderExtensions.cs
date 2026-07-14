using Gridlet;
using Gridlet.Abstractions;
using Gridlet.AspNetCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace — conventional namespace for endpoint extensions.
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
        ArgumentNullException.ThrowIfNull(endpoints);
        pattern = "/" + pattern.Trim('/');

        // Resolving options here validates the configuration at startup rather than on first request.
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<GridletOptions>>().Value;
        // The agent integration is optional, but when present its provider profiles should also
        // fail fast during endpoint mapping rather than on the first chat request.
        _ = endpoints.ServiceProvider.GetService<IGridletAgentService>()?.Info;

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

        GridletUiEndpoints.Map(group, pattern);
        GridletApiEndpoints.Map(group.MapGroup("/api"), options);
        GridletPublishedEndpoints.Map(group);

        return group;
    }
}
