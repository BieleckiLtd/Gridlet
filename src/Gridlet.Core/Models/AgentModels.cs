namespace Gridlet.Models;

/// <summary>
/// The two deliberately separate database-agent capability families. A mode selects the HTTP route
/// and the host authorization policy that guards it; what the agent may actually reach within that
/// route is decided by <see cref="GridletAgentAccess"/>.
/// </summary>
public enum GridletAgentMode
{
    /// <summary>Inspect metadata and help a database designer understand or propose a schema.</summary>
    Schema,

    /// <summary>Inspect metadata and answer questions by running bounded, read-only SQL.</summary>
    Data,
}

/// <summary>One kind of database context a person can opt into sharing with an agent.</summary>
public enum GridletAgentAccessScope
{
    /// <summary>Object names, columns, keys, indexes, relationships, and object definitions.</summary>
    Schema,

    /// <summary>Row values returned by bounded, read-only queries.</summary>
    Data,

    /// <summary>
    /// This Gridlet's own published API endpoints: their definitions and permission to invoke GET
    /// endpoints. This grants no direct database-query access. If an endpoint is invoked, its
    /// response is shared with the agent and may contain data.
    /// </summary>
    Api,
}

/// <summary>
/// What a person has opted into sharing with the agent for one turn. Every grant starts closed and
/// is opened only by an explicit choice in the client, so an agent never reaches database context
/// nobody offered it. Grants are additionally bounded by the connection's own
/// <c>AllowAgentSchemaAccess</c>, <c>AllowAgentDataAccess</c>, and <c>AllowAgentApiAccess</c>
/// settings.
/// </summary>
public sealed record GridletAgentAccess(bool Schema, bool Data, bool Api = false)
{
    /// <summary>No database context at all. The agent can still answer from its own knowledge.</summary>
    public static GridletAgentAccess None { get; } = new(false, false, false);

    /// <summary>Reports whether one scope is currently shared.</summary>
    public bool Allows(GridletAgentAccessScope scope) => scope switch
    {
        GridletAgentAccessScope.Data => Data,
        GridletAgentAccessScope.Api => Api,
        _ => Schema,
    };
}

/// <summary>
/// An agent's mid-turn request for a scope the person has not shared. The client shows it as an
/// allow/deny prompt; the waiting tool call resumes with whichever answer arrives first.
/// </summary>
public sealed record GridletAgentPermissionRequest(
    string RequestId,
    GridletAgentAccessScope Scope,
    string Reason);

/// <summary>Safe, browser-visible information about one configured model-provider profile.</summary>
public sealed record GridletAgentProfileInfo(
    string Id,
    string DisplayName,
    string Model,
    bool IsLocal,
    bool AllowsUserApiKey,
    bool RequiresUserApiKey,
    IReadOnlyList<string>? ReasoningEfforts = null,
    string? DefaultReasoningEffort = null,
    int? ContextWindowTokens = null);

/// <summary>
/// Browser-visible agent availability and provider information. When <paramref name="DefaultProfileId"/>
/// is set, clients select that profile for every new conversation; otherwise they are free to reuse
/// whichever profile the person last used.
/// </summary>
public sealed record GridletAgentInfo(
    IReadOnlyList<GridletAgentProfileInfo> Profiles,
    string? DefaultProfileId = null);

/// <summary>One prior turn supplied by the client for an ephemeral conversation.</summary>
public sealed record GridletAgentMessage(string Role, string Content);

/// <summary>Stable ownership and display information for one agent caller.</summary>
public sealed record GridletAgentUserContext(
    string? Subject,
    string? DisplayName,
    bool IsAuthenticated);

/// <summary>An opaque, user-bound reference to an API key held only in server memory.</summary>
public sealed record GridletAgentCredential(string Handle, DateTimeOffset ExpiresAt);

/// <summary>Non-secret database-engine facts supplied to a database agent.</summary>
public sealed record GridletDatabaseSystemInfo(string Technology, string? Version = null);

/// <summary>
/// Where this Gridlet is actually reachable, as seen by the browser that opened the conversation.
/// An agent that knows only the product's defaults invents plausible addresses; with these facts it
/// can name the real URL of a published endpoint instead. Everything here is non-secret and already
/// visible in the person's address bar.
/// </summary>
/// <param name="BaseAddress">
/// Absolute origin and path base the UI was served from, with a trailing slash - for example
/// <c>https://localhost:5088/</c>.
/// </param>
/// <param name="MountPath">
/// Route prefix Gridlet is mapped under, without a trailing slash - for example <c>/gridlet</c>.
/// </param>
/// <param name="PublishedApiSegment">
/// The path beneath the mount that published endpoints answer on, from
/// <see cref="GridletOptions.PublishedApiRoutePrefix"/> - <c>pub</c> unless the host changed it.
/// It is carried here rather than assumed, because an agent that hands somebody a URL built from
/// the documented default would be handing them one that resolves to nothing.
/// </param>
/// <param name="PublishedApiPath">
/// Optional application-root path when the host configured independent public routing, for example
/// <c>/pub/api</c>. When present it takes precedence over <paramref name="PublishedApiSegment"/>.
/// </param>
public sealed record GridletAgentEnvironment(
    string BaseAddress,
    string MountPath,
    string PublishedApiSegment = "pub",
    string? PublishedApiPath = null);

/// <summary>A provider-neutral request passed to the configured database agent service.</summary>
public sealed record GridletAgentRequest(
    string ConnectionName,
    string? Database,
    GridletAgentAccess Access,
    string ProfileId,
    string Message,
    IReadOnlyList<GridletAgentMessage> History,
    string? CredentialHandle,
    GridletAgentUserContext User,
    string? ConversationId = null,
    string? ReasoningEffort = null,
    GridletAgentEnvironment? Environment = null);


/// <summary>
/// Context-window consumption reported by a provider for one conversation. Providers that do not
/// report token usage never produce this information, and a provider that reports usage without a
/// window size leaves <see cref="ContextWindowTokens"/> unset.
/// </summary>
public sealed record GridletAgentContextUsage(
    long UsedTokens,
    long? ContextWindowTokens = null,
    long? InputTokens = null,
    long? CachedInputTokens = null,
    long? OutputTokens = null);

/// <summary>A progressive event emitted by a database-agent response.</summary>
public sealed record GridletAgentStreamEvent(
    string Type,
    string? Content = null,
    string? Name = null);
