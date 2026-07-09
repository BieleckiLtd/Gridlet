namespace Gridlet;

/// <summary>Authentication/authorization behaviour of the Gridlet endpoints.</summary>
public sealed class GridletSecurityOptions
{
    /// <summary>
    /// When <c>false</c> (the default) every Gridlet endpoint requires authorization:
    /// either the host's default policy or <see cref="AuthorizationPolicy"/> when set.
    /// Set to <c>true</c> only for local development or visual testing.
    /// </summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    /// Name of the authorization policy applied to all Gridlet endpoints.
    /// When <c>null</c>, the host's default authorization policy (authenticated user) is used.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }
}
