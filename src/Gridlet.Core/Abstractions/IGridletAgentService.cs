using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>
/// Optional provider-neutral database conversation service. Gridlet's Agent Framework package
/// supplies the default implementation; hosts may replace it without taking an AI dependency in
/// <c>Gridlet.Core</c> or <c>Gridlet.AspNetCore</c>.
/// </summary>
public interface IGridletAgentService
{
    /// <summary>Safe profile metadata. API keys and provider endpoints must never be included.</summary>
    GridletAgentInfo Info { get; }

    /// <summary>
    /// Stores a user-supplied API key ephemerally and returns an opaque, user-bound handle. The secret
    /// must not be persisted or returned by later calls.
    /// </summary>
    Task<GridletAgentCredential> StoreCredentialAsync(
        string profileId,
        string apiKey,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a previously stored ephemeral credential, if it exists.</summary>
    Task RemoveCredentialAsync(
        string credentialHandle,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default);

    /// <summary>Runs one conversational turn and streams assistant response events.</summary>
    IAsyncEnumerable<GridletAgentStreamEvent> ChatAsync(
        GridletAgentRequest request,
        CancellationToken cancellationToken = default);
}
