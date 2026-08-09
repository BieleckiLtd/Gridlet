namespace Gridlet.Demo;

/// <summary>Authorization policies used by the demo host.</summary>
internal static class AuthorizationExtensions
{
    internal const string DeliveryHoursPolicy = "BytePizzaDeliveryHours";
    internal const string AfterHoursCollectionPolicy = "BytePizzaAfterHoursCollection";
    internal const string GridletAccessPolicy = "ExampleGridletAccess";

    private const int DeliveryOpensAtHour = 11;
    private const int DeliveryClosesAtHour = 22;

    /// <summary>
    /// Registers example host policies that Gridlet can reference by name. Gridlet does not create
    /// users or sign them in; the host application's authentication handler supplies the user and
    /// their claims or roles before these authorization policies are evaluated.
    /// </summary>
    internal static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(DeliveryHoursPolicy, policy =>
            {
                // Byte Pizza delivers from 11:00 until 22:00 in the host's local time zone.
                policy.RequireAssertion(_ => IsDeliveryHours(DateTimeOffset.Now));
            })
            .AddPolicy(AfterHoursCollectionPolicy, policy =>
            {
                // Outside delivery hours, the tiny collection-only menu is available instead.
                policy.RequireAssertion(_ => !IsDeliveryHours(DateTimeOffset.Now));
            })
            .AddPolicy(GridletAccessPolicy, policy =>
            {
                // Policy and claim names are entirely host-defined; these are only examples.
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("permission", "gridlet:manage");
            });

        return services;
    }

    private static bool IsDeliveryHours(DateTimeOffset now)
        => now.Hour is >= DeliveryOpensAtHour and < DeliveryClosesAtHour;
}
