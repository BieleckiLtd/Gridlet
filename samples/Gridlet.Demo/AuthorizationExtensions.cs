namespace Gridlet.Demo;

/// <summary>Authorization policies used by the demo host.</summary>
internal static class AuthorizationExtensions
{
    internal const string OddSecondPolicy = "OddSecond";
    internal const string GridletAccessPolicy = "ExampleGridletAccess";

    /// <summary>
    /// Registers example host policies that Gridlet can reference by name. Gridlet does not create
    /// users or sign them in; the host application's authentication handler supplies the user and
    /// their claims or roles before these authorization policies are evaluated.
    /// </summary>
    internal static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(OddSecondPolicy, policy =>
            {
                // Demo policy used by the sample published endpoint. It deliberately grants access
                // only during odd-numbered UTC seconds so its allow/deny behaviour is easy to test.
                policy.RequireAssertion(_ => DateTimeOffset.UtcNow.Second % 2 == 1);
            })
            .AddPolicy(GridletAccessPolicy, policy =>
            {
                // Policy and claim names are entirely host-defined; these are only examples.
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("permission", "gridlet:manage");
            });

        return services;
    }
}
