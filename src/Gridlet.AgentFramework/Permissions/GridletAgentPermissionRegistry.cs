using System.Collections.Concurrent;
using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.AgentFramework;

/// <summary>
/// The process-wide set of access requests currently waiting for a browser answer. Entries live
/// only for as long as the turn that raised them: the waiting tool call removes its own entry when
/// it is answered, times out, or is cancelled.
/// </summary>
internal sealed class GridletAgentPermissionRegistry : IGridletAgentPermissionBroker
{
    private readonly ConcurrentDictionary<string, PendingRequest> pending =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Publishes one request and returns a handle that removes it again. The caller owns the
    /// completion source and is the only party that observes the answer.
    /// </summary>
    public IDisposable Register(
        string requestId,
        GridletAgentAccessScope scope,
        GridletAgentUserContext user,
        TaskCompletionSource<bool> completion)
    {
        var entry = new PendingRequest(scope, user, completion);
        if (!pending.TryAdd(requestId, entry))
        {
            throw new InvalidOperationException(
                "An access request with this identifier is already outstanding.");
        }
        return new Registration(this, requestId, entry);
    }

    public bool TryResolve(
        string requestId,
        GridletAgentAccessScope scope,
        bool granted,
        GridletAgentUserContext user)
    {
        if (string.IsNullOrWhiteSpace(requestId) ||
            !pending.TryGetValue(requestId, out var entry) ||
            entry.Scope != scope ||
            !entry.IsOwnedBy(user))
        {
            return false;
        }

        // Removing first keeps a second click from resolving the same request twice. The waiting
        // tool call also removes its own entry, so a lost race simply means the answer arrived
        // after the turn stopped caring about it.
        if (!pending.TryRemove(new KeyValuePair<string, PendingRequest>(requestId, entry)))
        {
            return false;
        }
        return entry.Completion.TrySetResult(granted);
    }

    private sealed record PendingRequest(
        GridletAgentAccessScope Scope,
        GridletAgentUserContext Owner,
        TaskCompletionSource<bool> Completion)
    {
        public bool IsOwnedBy(GridletAgentUserContext user) =>
            Owner.IsAuthenticated == user.IsAuthenticated &&
            (!Owner.IsAuthenticated ||
             string.Equals(Owner.Subject, user.Subject, StringComparison.Ordinal));
    }

    private sealed class Registration(
        GridletAgentPermissionRegistry registry,
        string requestId,
        PendingRequest entry) : IDisposable
    {
        public void Dispose() =>
            registry.pending.TryRemove(new KeyValuePair<string, PendingRequest>(requestId, entry));
    }
}
