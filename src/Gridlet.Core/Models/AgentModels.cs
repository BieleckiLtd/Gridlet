namespace Gridlet.Models;

/// <summary>The two deliberately separate database-agent capability sets.</summary>
public enum GridletAgentMode
{
    /// <summary>Inspect metadata and help a database designer understand or propose a schema.</summary>
    Schema,

    /// <summary>Inspect metadata and answer questions by running bounded, read-only SQL.</summary>
    Data,
}

/// <summary>Safe, browser-visible information about one configured model-provider profile.</summary>
public sealed record GridletAgentProfileInfo(
    string Id,
    string DisplayName,
    string Model,
    bool IsLocal,
    bool AllowsUserApiKey,
    bool RequiresUserApiKey);

/// <summary>Browser-visible agent availability and provider information.</summary>
public sealed record GridletAgentInfo(IReadOnlyList<GridletAgentProfileInfo> Profiles);

/// <summary>One prior turn supplied by the client for an ephemeral conversation.</summary>
public sealed record GridletAgentMessage(string Role, string Content);

/// <summary>Stable ownership and display information for one agent caller.</summary>
public sealed record GridletAgentUserContext(
    string? Subject,
    string? DisplayName,
    bool IsAuthenticated);

/// <summary>An opaque, user-bound reference to an API key held only in server memory.</summary>
public sealed record GridletAgentCredential(string Handle, DateTimeOffset ExpiresAt);

/// <summary>A provider-neutral request passed to the configured database agent service.</summary>
public sealed record GridletAgentRequest(
    string ConnectionName,
    string? Database,
    GridletAgentMode Mode,
    string ProfileId,
    string Message,
    IReadOnlyList<GridletAgentMessage> History,
    string? CredentialHandle,
    GridletAgentUserContext User);

/// <summary>A progressive event emitted by a database-agent response.</summary>
public sealed record GridletAgentStreamEvent(
    string Type,
    string? Content = null,
    string? Name = null);
