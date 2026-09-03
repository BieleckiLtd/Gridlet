using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>
/// Calls one of this Gridlet's own published endpoints over HTTP on behalf of an agent turn, so the
/// agent can show a person the real response rather than a plausible-looking invention.
///
/// Implementations are the security boundary for that capability. They must reach only endpoints
/// published in this installation - never an arbitrary address - must not widen what the caller
/// could already reach themselves, and must bound the response they return.
/// </summary>
public interface IGridletPublishedEndpointInvoker
{
    /// <summary>
    /// Calls the published endpoint named <paramref name="name"/> with <paramref name="query"/> as
    /// its query-string values. Returns a failure result rather than throwing when the endpoint is
    /// unknown, disabled, or not eligible to be called this way.
    /// </summary>
    Task<GridletPublishedEndpointInvocation> InvokeAsync(
        string name,
        IReadOnlyDictionary<string, string?> query,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What one agent-initiated call to a published endpoint produced. <paramref name="Body"/> is the
/// response as text, truncated when the endpoint returned more than the invoker is willing to put
/// in front of a model.
/// </summary>
public sealed record GridletPublishedEndpointInvocation(
    bool Succeeded,
    string Method,
    string Url,
    int? StatusCode,
    string? ContentType,
    string? Body,
    bool Truncated,
    long ElapsedMilliseconds,
    string? ErrorCode = null,
    string? ErrorMessage = null);
