using System.Text.Json;
using System.Text.Json.Serialization;
using Gridlet.Models;

namespace Gridlet.AgentFramework;

/// <summary>How one mid-turn access request ended.</summary>
internal enum GridletAgentAccessRequestOutcome
{
    /// <summary>The scope was already shared, so nothing was asked.</summary>
    AlreadyShared,

    /// <summary>The host disabled this scope for the connection; no prompt can override that.</summary>
    NotConfigured,

    /// <summary>Another request for this conversation is still waiting for an answer.</summary>
    AlreadyWaiting,

    /// <summary>The person allowed the scope, which remains revocable.</summary>
    Granted,

    /// <summary>The person denied the request.</summary>
    Denied,

    /// <summary>Nobody answered before the prompt expired, which counts as a denial.</summary>
    TimedOut,
}

/// <summary>
/// The live view of what a person is sharing with the agent during one turn, and the only way for
/// the agent to ask for more. Every database tool consults this immediately before it runs, so a
/// scope granted mid-turn takes effect at once and a scope that was never granted stays closed even
/// though the tool itself is always registered with the model.
/// </summary>
internal sealed class GridletAgentAccessGate(
    GridletAgentAccess hostAllows,
    GridletAgentAccess initialGrants,
    GridletAgentUserContext user,
    GridletAgentPermissionRegistry registry,
    TimeSpan promptTimeout,
    Func<GridletAgentStreamEvent, ValueTask> emit)
{
    // The browser matches the prompt's scope against its own share toggles by name, so the enum has
    // to go out as "schema"/"data"/"api" rather than as its ordinal.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    private readonly Lock sync = new();
    private bool schemaGranted = initialGrants.Schema && hostAllows.Schema;
    private bool dataGranted = initialGrants.Data && hostAllows.Data;
    private bool apiGranted = initialGrants.Api && hostAllows.Api;
    private bool waiting;

    /// <summary>The scopes shared right now.</summary>
    public GridletAgentAccess Current
    {
        get
        {
            lock (sync) return new GridletAgentAccess(schemaGranted, dataGranted, apiGranted);
        }
    }

    /// <summary>The scopes the host permits this connection to share at all.</summary>
    public GridletAgentAccess HostAllows => hostAllows;

    /// <summary>Reports whether a scope may be used right now.</summary>
    public bool IsShared(GridletAgentAccessScope scope)
    {
        lock (sync)
        {
            return scope switch
            {
                GridletAgentAccessScope.Data => dataGranted,
                GridletAgentAccessScope.Api => apiGranted,
                _ => schemaGranted,
            };
        }
    }

    /// <summary>
    /// Asks the person to share one scope and waits for their answer. The turn is still streaming
    /// while this runs, so the client can render the prompt and the agent continues with the
    /// decision instead of having to end its response and start over.
    /// </summary>
    public async Task<GridletAgentAccessRequestOutcome> RequestAsync(
        GridletAgentAccessScope scope,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (!hostAllows.Allows(scope)) return GridletAgentAccessRequestOutcome.NotConfigured;
        if (IsShared(scope)) return GridletAgentAccessRequestOutcome.AlreadyShared;

        lock (sync)
        {
            // One prompt at a time. Two simultaneous cards would leave the person guessing which
            // question they are answering.
            if (waiting) return GridletAgentAccessRequestOutcome.AlreadyWaiting;
            waiting = true;
        }

        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = registry.Register(requestId, scope, user, completion);
            using var timeout = new CancellationTokenSource(promptTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeout.Token);

            await emit(new GridletAgentStreamEvent(
                "permission-request",
                JsonSerializer.Serialize(
                    new GridletAgentPermissionRequest(requestId, scope, DescribeReason(reason)),
                    JsonOptions)));

            bool granted;
            using (linked.Token.Register(() => completion.TrySetCanceled()))
            {
                try
                {
                    granted = await completion.Task;
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await EmitResolvedAsync(requestId, scope, granted: false, "timed-out");
                    return GridletAgentAccessRequestOutcome.TimedOut;
                }
            }

            if (granted)
            {
                lock (sync)
                {
                    switch (scope)
                    {
                        case GridletAgentAccessScope.Data: dataGranted = true; break;
                        case GridletAgentAccessScope.Api: apiGranted = true; break;
                        default: schemaGranted = true; break;
                    }
                }
            }

            await EmitResolvedAsync(requestId, scope, granted, granted ? "granted" : "denied");
            return granted
                ? GridletAgentAccessRequestOutcome.Granted
                : GridletAgentAccessRequestOutcome.Denied;
        }
        finally
        {
            lock (sync) waiting = false;
        }
    }

    private ValueTask EmitResolvedAsync(
        string requestId,
        GridletAgentAccessScope scope,
        bool granted,
        string status)
        => emit(new GridletAgentStreamEvent(
            "permission-resolved",
            JsonSerializer.Serialize(
                new { requestId, scope = scope.ToString().ToLowerInvariant(), granted, status },
                JsonOptions)));

    /// <summary>
    /// Bounds the model-authored justification shown in the prompt. It is untrusted text that a
    /// person is about to read while deciding, so it stays short and free of control characters.
    /// </summary>
    private static string DescribeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "No reason was given.";

        const int maxLength = 400;
        var normalized = new string(reason
            .Select(character => char.IsControl(character) && character is not '\n' ? ' ' : character)
            .ToArray()).Trim();
        return normalized.Length <= maxLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, maxLength), "…");
    }
}
