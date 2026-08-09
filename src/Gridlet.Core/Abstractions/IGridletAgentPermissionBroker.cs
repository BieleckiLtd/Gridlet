using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>
/// Carries an allow/deny answer from the browser back to the agent turn that is waiting for it.
/// A turn stays on the wire while it waits, so an answered request lets the same response continue
/// rather than forcing the person to ask their question again.
/// </summary>
public interface IGridletAgentPermissionBroker
{
    /// <summary>
    /// Completes one pending access request. Returns <see langword="false"/> when the request is
    /// unknown, already answered, raised for a different scope, or owned by somebody else; callers
    /// treat every one of those the same way so the endpoint cannot be used to probe for live
    /// request ids.
    /// </summary>
    bool TryResolve(
        string requestId,
        GridletAgentAccessScope scope,
        bool granted,
        GridletAgentUserContext user);
}
