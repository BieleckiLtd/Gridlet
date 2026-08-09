using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Tests.AspNetCore.Fakes;

/// <summary>Deterministic, offline agent service used to exercise the provider-neutral HTTP boundary.</summary>
public sealed class FakeGridletAgentService : IGridletAgentService
{
    public const string ProfileId = "fake-remote";
    public const string CredentialHandle = "credential-handle-1";
    public const string ConfiguredApiKey = "sk-configured-secret-never-return";
    public const string ProviderEndpoint = "https://private-agent-provider.invalid";

    public GridletAgentInfo Info { get; } = new(
    [
        new GridletAgentProfileInfo(
            ProfileId,
            "Fake remote model",
            "fake-model-v1",
            IsLocal: false,
            AllowsUserApiKey: true,
            RequiresUserApiKey: true,
            ReasoningEfforts: ["low", "medium", "high"],
            DefaultReasoningEffort: "medium"),
        new GridletAgentProfileInfo(
            "fake-local",
            "Fake local model",
            "fake-local-v1",
            IsLocal: true,
            AllowsUserApiKey: false,
            RequiresUserApiKey: false),
    ]);

    public List<GridletAgentRequest> Requests { get; } = [];

    public List<(string ProfileId, string ApiKey, GridletAgentUserContext User)> StoredCredentials { get; } = [];

    public List<(string Handle, GridletAgentUserContext User)> RemovedCredentials { get; } = [];

    public List<(string ConversationId, GridletAgentUserContext User)> ClosedConversations { get; } = [];

    public Exception? CredentialException { get; set; }

    public Exception? ChatException { get; set; }

    public Task<GridletAgentCredential> StoreCredentialAsync(
        string profileId,
        string apiKey,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CredentialException is not null) throw CredentialException;
        StoredCredentials.Add((profileId, apiKey, user));
        return Task.FromResult(new GridletAgentCredential(
            CredentialHandle, DateTimeOffset.UtcNow.AddMinutes(30)));
    }

    public Task RemoveCredentialAsync(
        string credentialHandle,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemovedCredentials.Add((credentialHandle, user));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<GridletAgentStreamEvent> ChatAsync(
        GridletAgentRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (ChatException is not null) throw ChatException;
        await Task.Yield();

        yield return new GridletAgentStreamEvent("started");
        if (request.Message.Contains("markdown", StringComparison.OrdinalIgnoreCase))
        {
            yield return new GridletAgentStreamEvent(
                "reasoning",
                "Inspected the available schema metadata and chose a compact tabular answer.");
            yield return new GridletAgentStreamEvent("reasoning-section");
            yield return new GridletAgentStreamEvent(
                "reasoning",
                "Prepared the join explanation.");
            yield return new GridletAgentStreamEvent(
                "reasoning-raw",
                "Optional model-supplied raw reasoning.");
            yield return new GridletAgentStreamEvent(
                "reasoning-final",
                "Authoritative completed reasoning summary.");
            yield return new GridletAgentStreamEvent(
                "reasoning-raw-final",
                "Authoritative completed raw reasoning.");
            yield return new GridletAgentStreamEvent(
                "tool",
                """{"arguments":{"schema":"dbo","name":"Orders"}}""",
                "describe_table");
            yield return new GridletAgentStreamEvent(
                "tool-result",
                request.Message.Contains("failed tool", StringComparison.OrdinalIgnoreCase)
                    ? """{"result":"{\"error\":{\"code\":\"GridletQueryException\",\"message\":\"SQLite Error 1: 'no such table: missing_table'.\",\"recoverable\":true}}"}"""
                    : """{"result":{"columns":["orderId","customerId"]}}""",
                "describe_table");
            yield return new GridletAgentStreamEvent(
                "content",
                """
                ### Query plan

                *Prepared safely.*

                **Explanation of the join logic:**

                * `Orders` supplies the order identifier.
                * `OrderLines` supplies the matching products.

                ***

                Delta symbol: $\bigtriangleup$

                | Step | Tables Joined Via Column | Purpose |
                |------|---------------------------|---------|
                | 1. | `Orders` + `OrderLines`.orderId | Get line items for each order |
                | 2. | `OrderLines` + `Products`.productId | Map products to their names |

                ```sql
                SELECT o.orderId
                FROM Orders AS o;
                ```

                ```json
                {"request":{"method":"GET"},"rows":2,"ok":true}
                ```
                """,
                "assistant");
            yield return new GridletAgentStreamEvent("completed");
            yield break;
        }

        yield return new GridletAgentStreamEvent(
            "content",
            $"Fake {(request.Access.Data ? "data" : "schema")} response",
            "assistant");
        yield return new GridletAgentStreamEvent("completed");
    }

    public Task CloseConversationAsync(
        string conversationId,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClosedConversations.Add((conversationId, user));
        return Task.CompletedTask;
    }
}
