using Gridlet;
using Gridlet.AspNetCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace — conventional namespace for endpoint extensions.
namespace Microsoft.AspNetCore.Builder;

public static class GridletEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Gridlet UI and API under <paramref name="pattern"/>. Unless
    /// <see cref="GridletSecurityOptions.AllowAnonymous"/> is set, every endpoint requires
    /// authorization (the configured policy, or the host's default policy).
    /// </summary>
    public static IEndpointConventionBuilder MapGridlet(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/gridlet")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        pattern = "/" + pattern.Trim('/');

        // Resolving options here validates the configuration at startup rather than on first request.
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<GridletOptions>>().Value;

        var group = endpoints.MapGroup(pattern);

        if (!options.Security.AllowAnonymous)
        {
            if (options.Security.AuthorizationPolicy is { Length: > 0 } policy)
            {
                group.RequireAuthorization(policy);
            }
            else
            {
                group.RequireAuthorization();
            }
        }

        GridletUiEndpoints.Map(group, pattern);
        GridletApiEndpoints.Map(group.MapGroup("/api"));

        return group;
    }
}
