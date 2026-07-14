namespace Gridlet;

/// <summary>Authentication/authorization behaviour of the Gridlet endpoints.</summary>
public sealed class GridletSecurityOptions
{
    /// <summary>
    /// When <c>false</c> (the default) every Gridlet endpoint requires authorization:
    /// either the host's default policy or <see cref="AuthorizationPolicy"/> when set.
    /// Set to <c>true</c> only for local development or visual testing. An explicitly configured
    /// <see cref="AuthorizationPolicy"/> takes precedence and still requires authorization.
    /// </summary>
    public bool AllowAnonymous { get; set; } = false;

    /// <summary>
    /// Name of the authorization policy applied to all Gridlet endpoints.
    /// When <c>null</c>, the host's default authorization policy (authenticated user) is used.
    /// The named policy must already be registered with ASP.NET Core authorization. When set, it
    /// takes precedence over <see cref="AllowAnonymous"/>.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Optional additional policy required by data-mode agent chat. The main Gridlet policy still
    /// applies. When <c>null</c>, access is governed by the main policy and the connection's
    /// <see cref="GridletConnectionOptions.AllowAgentDataAccess"/> gate.
    /// </summary>
    public string? AgentDataAuthorizationPolicy { get; set; }

    /// <summary>
    /// Optional additional policy required by schema-design agent chat. The main Gridlet policy
    /// still applies. When <c>null</c>, access is governed by the main policy and the connection's
    /// <see cref="GridletConnectionOptions.AllowAgentSchemaAccess"/> gate.
    /// </summary>
    public string? AgentSchemaAuthorizationPolicy { get; set; }

    /// <summary>
    /// Optional additional policy required to create or remove ephemeral user API-key handles.
    /// Use this when Gridlet itself allows anonymous access or credential management should be
    /// narrower than database chat access.
    /// </summary>
    public string? AgentCredentialAuthorizationPolicy { get; set; }

    /// <summary>
    /// Allows anonymous callers to create ephemeral API-key handles. Defaults to <c>false</c>;
    /// authenticated, stable user ownership is strongly preferred even when Gridlet itself is
    /// intentionally anonymous for local development.
    /// </summary>
    public bool AllowAnonymousAgentCredentials { get; set; } = false;
}
