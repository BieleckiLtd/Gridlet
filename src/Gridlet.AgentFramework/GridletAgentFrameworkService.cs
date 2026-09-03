using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Gridlet.Abstractions;
using Gridlet.Auditing;
using Gridlet.Models;
using GitHub.Copilot;
using Microsoft.Agents.AI.GitHub.Copilot;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

namespace Gridlet.AgentFramework;

internal sealed class GridletAgentFrameworkService : IGridletAgentService, IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly GridletAgentFrameworkSettings settings;
    private readonly EphemeralCredentialStore credentials;
    private readonly IGridletConnectionResolver connectionResolver;
    private readonly IGridletAuditSink auditSink;
    private readonly GridletAgentPermissionRegistry permissions;
    private readonly IServiceProvider services;
    private readonly ConcurrentDictionary<string, CliConversation> cliConversations =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim conversationCreationGate = new(1, 1);
    private readonly CliRuntimeLimiter cliRuntimeLimiter;
    private readonly OllamaContextWindowProbe ollamaContextWindows = new();
    private readonly CopilotContextUsageReader copilotContextUsage = new();
    private readonly CancellationTokenSource cleanupCancellation = new();
    private readonly Task cleanupTask;
    private int disposeState;

    public GridletAgentFrameworkService(
        GridletAgentFrameworkSettings settings,
        EphemeralCredentialStore credentials,
        IGridletConnectionResolver connectionResolver,
        IGridletAuditSink auditSink,
        GridletAgentPermissionRegistry permissions,
        IServiceProvider services)
    {
        this.settings = settings;
        this.credentials = credentials;
        this.connectionResolver = connectionResolver;
        this.auditSink = auditSink;
        this.permissions = permissions;
        this.services = services;
        cliRuntimeLimiter = new CliRuntimeLimiter(settings.MaxActiveConversations);
        cleanupTask = CleanupExpiredConversationsAsync(cleanupCancellation.Token);
    }

    /// <summary>The opening of every system prompt. Its wording lives in Prompts/Instructions/base.md.</summary>
    private static string BaseInstructions => GridletPrompts.Text("Instructions/base");

    public GridletAgentInfo Info => settings.Info;

    public Task<GridletAgentCredential> StoreCredentialAsync(
        string profileId,
        string apiKey,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = GetProfile(profileId);
        if (!profile.AllowsUserApiKey)
        {
            throw new GridletAgentException(
                $"Agent profile '{profile.Id}' does not accept user-supplied API keys.");
        }
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 8_192)
        {
            throw new GridletAgentException("A valid API key is required.");
        }

        return Task.FromResult(credentials.Store(profile.Id, apiKey, user));
    }

    public Task RemoveCredentialAsync(
        string credentialHandle,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(credentialHandle) || credentialHandle.Length > 256 ||
            !credentials.Remove(credentialHandle, user))
        {
            throw new GridletAgentException("The credential handle is invalid or expired.");
        }
        return Task.CompletedTask;
    }

    public async Task CloseConversationAsync(
        string conversationId,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!cliConversations.TryGetValue(conversationId, out var conversation) ||
            !conversation.IsOwnedBy(user))
        {
            return;
        }

        if (cliConversations.TryRemove(
                new KeyValuePair<string, CliConversation>(conversationId, conversation)))
        {
            // Once ownership has been removed from the dictionary, disposal must complete even if
            // the HTTP close request is subsequently cancelled; otherwise the runtime is orphaned.
            await conversation.DisposeAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Streams one turn. Events are produced onto a channel rather than yielded directly, because a
    /// tool call can now block while it waits for the person to answer an access prompt. A tool runs
    /// inside the provider's own streaming enumeration, so anything yielded from that loop would sit
    /// behind the blocked call - and the prompt the person must answer to unblock it would never
    /// reach the browser. The producer writes from wherever it happens to be running; the consumer
    /// keeps flushing.
    /// </summary>
    public async IAsyncEnumerable<GridletAgentStreamEvent> ChatAsync(
        GridletAgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var channel = Channel.CreateUnbounded<GridletAgentStreamEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        using var turnCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var turn = RunTurnAsync(request, channel.Writer, turnCancellation.Token);
        try
        {
            // A fault inside the turn completes the channel with that exception, so it surfaces
            // here on the caller's thread exactly as it did when this method yielded directly.
            await foreach (var streamEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return streamEvent;
            }
        }
        finally
        {
            await turnCancellation.CancelAsync();
            await turn;
        }
    }

    /// <summary>
    /// Runs one turn to completion, reporting everything through <paramref name="writer"/>. It never
    /// throws: a failure completes the channel with that exception instead, which is what lets the
    /// consumer above await it from a <c>finally</c> block.
    /// </summary>
    private async Task RunTurnAsync(
        GridletAgentRequest request,
        ChannelWriter<GridletAgentStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await ProduceTurnAsync(request, writer, cancellationToken);
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
            return;
        }
        writer.TryComplete();
    }

    private async Task ProduceTurnAsync(
        GridletAgentRequest request,
        ChannelWriter<GridletAgentStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        var profile = GetProfile(request.ProfileId);
        ValidateRequest(request);
        ValidateReasoningEffort(request, profile);
        var resolved = connectionResolver.Resolve(request.ConnectionName, request.Database);
        var hostAllows = EnsureAccessAllowed(request.Access, resolved.Context.Connection);
        var apiKey = ResolveApiKey(request, profile);

        var gate = new GridletAgentAccessGate(
            hostAllows,
            request.Access,
            request.User,
            permissions,
            settings.AccessPromptTimeout,
            streamEvent => writer.WriteAsync(streamEvent, cancellationToken));
        var systemInfo = await GetDatabaseSystemInfoAsync(resolved, cancellationToken);
        var instructions = CreateInstructions(
            BaseInstructions, systemInfo, gate.Current, hostAllows, request.Environment);
        CliConversation? cliConversation = null;
        if (profile.Provider is GridletAgentProvider.Codex or GridletAgentProvider.ClaudeCode &&
            request.ConversationId is not null)
        {
            cliConversation = await GetCliConversationAsync(
                request, profile, instructions, cancellationToken);
        }

        var completedNormally = false;
        try
        {
            using var transientCliLease =
                profile.Provider is GridletAgentProvider.Codex or GridletAgentProvider.ClaudeCode &&
                cliConversation is null
                    ? await cliRuntimeLimiter.AcquireAsync(cancellationToken)
                    : null;

            var pendingToolEvents = new ConcurrentQueue<GridletAgentStreamEvent>();
            var databaseTools = new GridletDatabaseAgentTools(
                resolved, request.User.DisplayName, settings, auditSink, gate,
                services.GetService<ISavedQueryStore>(),
                services.GetService<IPublishedEndpointStore>(),
                services.GetService<IGridletPublishedEndpointInvoker>(),
                request.Environment,
                services.GetService<IOptionsMonitor<GridletOptions>>()?.CurrentValue
                    .Limits.MaxQueryResultRows ?? 0,
                (name, result) => pendingToolEvents.Enqueue(new GridletAgentStreamEvent(
                    "tool-result", SerializeToolPayload(new { result }), name)));
            var tools = databaseTools.Create();
            await using var copilotClient = profile.Provider == GridletAgentProvider.GitHubCopilot
                ? await StartCopilotClientAsync(cancellationToken)
                : null;
            await using var transientClaudeRuntime = profile.Provider == GridletAgentProvider.ClaudeCode &&
                                                     cliConversation is null
                ? new ClaudeCodeRuntime(
                    settings.ClaudeExecutablePath, profile.Model, instructions,
                    profile.ClaudeCodeEffort, profile.AllowsReasoningEffortSelection)
                : null;
            using var chatClient = profile.Provider is
                    GridletAgentProvider.Codex or GridletAgentProvider.ClaudeCode or
                    GridletAgentProvider.GitHubCopilot
                ? null
                : CreateChatClient(profile, apiKey)
                    .AsBuilder()
                    .UseFunctionInvocation(configure: client =>
                        client.MaximumIterationsPerRequest =
                            settings.MaxToolIterations ?? int.MaxValue)
                    .Build();

            AIAgent agent = profile.Provider switch
            {
                GridletAgentProvider.Codex => new CodexAppServerAgent(
                    settings.CodexExecutablePath,
                    profile.Model,
                    instructions,
                    tools.OfType<AIFunction>().ToArray(),
                    settings.MaxToolIterations,
                    GetCodexReasoningEffort(request, profile),
                    cliConversation?.CodexRuntime),
                GridletAgentProvider.ClaudeCode => new ClaudeCodeAgent(
                    cliConversation?.ClaudeRuntime ?? transientClaudeRuntime!,
                    tools.OfType<AIFunction>().ToArray(),
                    settings.MaxToolIterations,
                    GetClaudeCodeEffort(request, profile)),
                GridletAgentProvider.GitHubCopilot => CreateGitHubCopilotAgent(
                    copilotClient!, profile, instructions, tools, settings.MaxToolIterations,
                    GetCopilotReasoningEffort(request, profile)),
                _ => new ChatClientAgent(
                    chatClient!,
                    new ChatClientAgentOptions
                    {
                        Name = "GridletDatabaseAgent",
                        Description = "A bounded database schema and read-only data assistant.",
                        ChatOptions = new ChatOptions
                        {
                            Instructions = instructions,
                            Tools = tools,
                            MaxOutputTokens = settings.MaxOutputTokens,
                            Reasoning = new ReasoningOptions
                            {
                                Output = ReasoningOutput.Summary,
                            },
                        },
                        UseProvidedChatClientAsIs = true,
                    }),
            };

            var messages = CreateMessages(request);
            var observedCalls = new HashSet<string>(StringComparer.Ordinal);
            var contextWindowTokens = await ResolveContextWindowAsync(profile, cancellationToken);
            // Copilot exposes context usage only for a named session, so the turn gets an explicit
            // one. It starts empty, which keeps the existing per-turn session behavior.
            var copilotSession = profile.Provider == GridletAgentProvider.GitHubCopilot
                ? await agent.CreateSessionAsync(cancellationToken)
                : null;
            await writer.WriteAsync(new GridletAgentStreamEvent("started"), cancellationToken);

            await foreach (var update in agent.RunStreamingAsync(
                               messages,
                               session: cliConversation?.CodexAgentSession ?? copilotSession,
                               cancellationToken: cancellationToken))
            {
                foreach (var functionCall in update.Contents.OfType<FunctionCallContent>())
                {
                    var callKey = functionCall.CallId ?? functionCall.Name;
                    if (!string.IsNullOrWhiteSpace(functionCall.Name) && observedCalls.Add(callKey))
                    {
                        await writer.WriteAsync(
                            new GridletAgentStreamEvent(
                                "tool",
                                SerializeToolPayload(new
                                {
                                    arguments = functionCall.Arguments,
                                }),
                                functionCall.Name),
                            cancellationToken);
                    }
                }

                foreach (var reasoning in update.Contents.OfType<TextReasoningContent>())
                {
                    var eventType = reasoning.RawRepresentation is CodexReasoningEvent codexReasoning
                        ? codexReasoning.Kind
                        : "reasoning";
                    if (!string.IsNullOrEmpty(reasoning.Text) || eventType == "reasoning-section")
                    {
                        await writer.WriteAsync(
                            new GridletAgentStreamEvent(eventType, reasoning.Text),
                            cancellationToken);
                    }
                }

                foreach (var functionResult in update.Contents.OfType<FunctionResultContent>())
                {
                    if (TryReadFailedToolResult(functionResult, out var toolName, out var result))
                    {
                        await writer.WriteAsync(
                            new GridletAgentStreamEvent(
                                "tool-result",
                                SerializeToolPayload(new { result, success = false }),
                                toolName),
                            cancellationToken);
                    }
                }

                foreach (var usage in update.Contents.OfType<UsageContent>())
                {
                    var contextUsage = GridletContextUsage.TryCreateContextUsage(
                        usage.Details, contextWindowTokens);
                    if (contextUsage is not null)
                    {
                        await writer.WriteAsync(
                            new GridletAgentStreamEvent(
                                "usage", SerializeToolPayload(contextUsage)),
                            cancellationToken);
                    }
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    await writer.WriteAsync(
                        new GridletAgentStreamEvent("delta", update.Text), cancellationToken);
                }

                foreach (var toolEvent in DrainPendingToolEvents(pendingToolEvents))
                {
                    await writer.WriteAsync(toolEvent, cancellationToken);
                }
            }

            foreach (var finalToolEvent in DrainPendingToolEvents(pendingToolEvents))
            {
                await writer.WriteAsync(finalToolEvent, cancellationToken);
            }

            if (copilotSession is GitHubCopilotAgentSession { SessionId: { } copilotSessionId })
            {
                var copilotUsage = await copilotContextUsage.TryReadAsync(
                    copilotClient!, copilotSessionId, profile.Model, cancellationToken);
                if (copilotUsage is not null)
                {
                    await writer.WriteAsync(
                        new GridletAgentStreamEvent(
                            "usage", SerializeToolPayload(copilotUsage)),
                        cancellationToken);
                }
            }

            await writer.WriteAsync(new GridletAgentStreamEvent("completed"), cancellationToken);
            completedNormally = true;
        }
        finally
        {
            if (cliConversation is not null)
            {
                cliConversation.Touch();
                cliConversation.Gate.Release();
                if (!completedNormally &&
                    cliConversations.TryRemove(new KeyValuePair<string, CliConversation>(
                        request.ConversationId!, cliConversation)))
                {
                    await cliConversation.DisposeAsync(CancellationToken.None);
                }
            }
        }
    }

    internal static string CreateInstructions(
        string baseInstructions,
        GridletDatabaseSystemInfo systemInfo,
        GridletAgentAccess sharedAccess,
        GridletAgentAccess hostAllows,
        GridletAgentEnvironment? environment = null)
    {
        var technology = NormalizeSystemInfoValue(systemInfo.Technology, "unknown");
        var version = string.IsNullOrWhiteSpace(systemInfo.Version)
            ? "not available"
            : NormalizeSystemInfoValue(systemInfo.Version, "not available");
        return string.Concat(
            baseInstructions,
            "\n\n", GridletPrompts.Text("Instructions/product-briefing"),
            "\n\n", GridletPrompts.Text("Instructions/access"),
            "\n", GridletPrompts.Text(
                "Instructions/access-state",
                ("schema", DescribeScope(sharedAccess.Schema, hostAllows.Schema)),
                ("data", DescribeScope(sharedAccess.Data, hostAllows.Data)),
                ("api", DescribeScope(sharedAccess.Api, hostAllows.Api))),
            "\n\n", GridletPrompts.Text(
                "Instructions/database-environment",
                ("technology", technology),
                ("version", version)),
            "\n",
            DescribeEnvironment(environment));
    }

    /// <summary>
    /// Tells the agent where it is actually running. Without this it falls back to the placeholder
    /// addresses in its own documentation and hands people URLs that resolve to nothing.
    /// </summary>
    private static string DescribeEnvironment(GridletAgentEnvironment? environment)
    {
        if (environment is null) return string.Empty;

        var baseAddress = NormalizeSystemInfoValue(environment.BaseAddress, string.Empty);
        var mountPath = NormalizeSystemInfoValue(environment.MountPath, string.Empty);
        if (baseAddress.Length == 0 || mountPath.Length == 0) return string.Empty;

        var mount = baseAddress.TrimEnd('/') + mountPath;
        var segment = NormalizeSystemInfoValue(environment.PublishedApiSegment, "pub").Trim('/');
        return string.Concat(
            "\n",
            GridletPrompts.Text(
                "Instructions/installation",
                ("base_address", baseAddress),
                ("mount", mount),
                ("published_pattern", $"{mount}/{segment}/{{route}}")),
            "\n");
    }

    private static string DescribeScope(bool shared, bool hostAllows) => hostAllows
        ? shared
            ? GridletPrompts.Section("Instructions/access-state", "shared")
            : GridletPrompts.Section("Instructions/access-state", "not-shared")
        : GridletPrompts.Section("Instructions/access-state", "host-disabled");

    private static string NormalizeSystemInfoValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        const int maxLength = 256;
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static async Task<GridletDatabaseSystemInfo> GetDatabaseSystemInfoAsync(
        ResolvedConnection resolved,
        CancellationToken cancellationToken)
    {
        var technology = resolved.Provider.ProviderName switch
        {
            GridletProviderNames.SqlServer => "Microsoft SQL Server",
            GridletProviderNames.Sqlite => "SQLite",
            _ => resolved.Provider.ProviderName.ToString(),
        };

        if (resolved.Provider is not IGridletDatabaseSystemInfoProvider infoProvider)
        {
            return new GridletDatabaseSystemInfo(technology);
        }

        try
        {
            var info = await infoProvider.GetDatabaseSystemInfoAsync(
                resolved.Context, cancellationToken);
            return string.IsNullOrWhiteSpace(info.Technology)
                ? new GridletDatabaseSystemInfo(technology, info.Version)
                : info;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Engine/version context improves model accuracy but must not make chat unavailable.
            return new GridletDatabaseSystemInfo(technology);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0) return;

        await cleanupCancellation.CancelAsync();
        try
        {
            await cleanupTask;
        }
        catch (OperationCanceledException)
        {
            // Normal singleton shutdown.
        }

        var conversations = cliConversations.ToArray();
        cliConversations.Clear();
        foreach (var conversation in conversations)
        {
            await conversation.Value.DisposeAsync(CancellationToken.None);
        }
        conversationCreationGate.Dispose();
        cleanupCancellation.Dispose();
        ollamaContextWindows.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task<CliConversation> GetCliConversationAsync(
        GridletAgentRequest request,
        GridletAgentProfileSettings profile,
        string instructions,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!cliConversations.TryGetValue(request.ConversationId!, out var conversation))
            {
                await conversationCreationGate.WaitAsync(cancellationToken);
                try
                {
                    if (!cliConversations.TryGetValue(request.ConversationId!, out conversation))
                    {
                        if (cliConversations.Count >= settings.MaxActiveConversations)
                        {
                            throw new GridletAgentException(
                                "The maximum number of active agent conversations has been reached. " +
                                "Close an existing Ask tab or wait for an inactive conversation to expire.");
                        }
                        var runtimeLease = await cliRuntimeLimiter.AcquireAsync(cancellationToken);
                        try
                        {
                            conversation = new CliConversation(
                                request, profile, settings, instructions, runtimeLease);
                        }
                        catch
                        {
                            runtimeLease.Dispose();
                            throw;
                        }
                        if (!cliConversations.TryAdd(request.ConversationId!, conversation))
                        {
                            await conversation.DisposeAsync(CancellationToken.None);
                            continue;
                        }
                    }
                }
                finally
                {
                    conversationCreationGate.Release();
                }
            }
            if (!conversation.Matches(request, profile))
            {
                throw new GridletAgentException(
                    "The agent conversation does not belong to this user or database context.");
            }

            await conversation.Gate.WaitAsync(cancellationToken);
            if (cliConversations.TryGetValue(request.ConversationId!, out var current) &&
                ReferenceEquals(current, conversation))
            {
                conversation.Touch();
                return conversation;
            }
            conversation.Gate.Release();
        }
    }

    private async Task CleanupExpiredConversationsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var expiresBefore = DateTimeOffset.UtcNow - settings.ConversationIdleTimeout;
            foreach (var entry in cliConversations)
            {
                if (entry.Value.LastAccessed > expiresBefore || !entry.Value.Gate.Wait(0))
                {
                    continue;
                }

                try
                {
                    if (entry.Value.LastAccessed <= expiresBefore &&
                        cliConversations.TryRemove(entry))
                    {
                        await entry.Value.DisposeRuntimeAsync();
                    }
                }
                finally
                {
                    entry.Value.Gate.Release();
                }
            }
        }
    }

    private GridletAgentProfileSettings GetProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !settings.TryGetProfile(profileId, out var profile))
        {
            throw new GridletAgentException("The selected agent profile is not configured.");
        }
        return profile;
    }

    private void ValidateRequest(GridletAgentRequest request)
    {
        if (request.ConversationId is { } conversationId &&
            (conversationId.Length is < 1 or > 100 ||
             conversationId.Any(character =>
                 !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')))
        {
            throw new GridletAgentException("The agent conversation id is invalid.");
        }
        if (request.Access is null)
        {
            throw new GridletAgentException("The requested agent access is invalid.");
        }
        if (string.IsNullOrWhiteSpace(request.Message) ||
            request.Message.Length > settings.MaxMessageCharacters)
        {
            throw new GridletAgentException(
                $"The agent message must contain 1-{settings.MaxMessageCharacters:N0} characters.");
        }
        if (request.History is null || request.History.Count > settings.MaxHistoryMessages)
        {
            throw new GridletAgentException(
                $"Conversation history may contain at most {settings.MaxHistoryMessages:N0} messages.");
        }

        long historyCharacters = 0;
        foreach (var message in request.History)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.Content) ||
                message.Content.Length > settings.MaxMessageCharacters ||
                message.Role is null ||
                !(message.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                  message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)))
            {
                throw new GridletAgentException("Conversation history contains an invalid message.");
            }
            historyCharacters += message.Content.Length;
        }
        if (historyCharacters > settings.MaxHistoryCharacters)
        {
            throw new GridletAgentException(
                $"Conversation history may contain at most {settings.MaxHistoryCharacters:N0} characters.");
        }
    }

    private static void ValidateReasoningEffort(
        GridletAgentRequest request,
        GridletAgentProfileSettings profile)
    {
        if (request.ReasoningEffort is null) return;
        if (!profile.AllowsReasoningEffortSelection)
        {
            throw new GridletAgentException(
                "Reasoning effort selection is not enabled for this agent profile.");
        }

        var valid = profile.Provider switch
        {
            GridletAgentProvider.ClaudeCode => request.ReasoningEffort is
                "low" or "medium" or "high" or "xhigh" or "max",
            GridletAgentProvider.Codex or GridletAgentProvider.GitHubCopilot =>
                request.ReasoningEffort is "low" or "medium" or "high" or "xhigh",
            _ => false,
        };
        if (!valid)
        {
            throw new GridletAgentException(
                "The selected reasoning effort is not allowed for this agent profile.");
        }
    }

    /// <summary>
    /// Confirms the person is not asking to share something the host disabled, and returns the
    /// ceiling that bounds anything the agent may go on to request during the turn.
    /// </summary>
    private static GridletAgentAccess EnsureAccessAllowed(
        GridletAgentAccess access,
        GridletConnectionOptions connection)
    {
        if (access.Schema && !connection.AllowAgentSchemaAccess)
        {
            throw new GridletAgentException(
                $"Schema agent access is disabled for connection '{connection.Name}'.");
        }
        if (access.Data && !connection.AllowAgentDataAccess)
        {
            throw new GridletAgentException(
                $"Data agent access is disabled for connection '{connection.Name}'.");
        }
        if (access.Api && !connection.AllowAgentApiAccess)
        {
            throw new GridletAgentException(
                $"Published API agent access is disabled for connection '{connection.Name}'.");
        }

        return new GridletAgentAccess(
            connection.AllowAgentSchemaAccess,
            connection.AllowAgentDataAccess,
            connection.AllowAgentApiAccess);
    }

    private string? ResolveApiKey(
        GridletAgentRequest request,
        GridletAgentProfileSettings profile)
    {
        if (!string.IsNullOrWhiteSpace(request.CredentialHandle))
        {
            if (!profile.AllowsUserApiKey || request.CredentialHandle.Length > 256)
            {
                throw new GridletAgentException("The credential handle is invalid or expired.");
            }

            return credentials.Resolve(
                       request.CredentialHandle,
                       profile.Id,
                       request.User)
                   ?? throw new GridletAgentException(
                       "The credential handle is invalid or expired.");
        }

        if (profile.ServerApiKey is not null)
        {
            return profile.ServerApiKey;
        }
        if (profile.RequiresUserApiKey)
        {
            throw new GridletAgentException(
                $"Agent profile '{profile.Id}' requires a user API key.");
        }

        return null;
    }

    /// <summary>
    /// Resolves the context window used when a provider reports token usage without one. A running
    /// Ollama server knows the window it actually loaded the model with, which is more reliable
    /// than any host declaration; every other provider falls back to the configured value.
    /// </summary>
    private async Task<int?> ResolveContextWindowAsync(
        GridletAgentProfileSettings profile,
        CancellationToken cancellationToken)
    {
        if (profile.Provider != GridletAgentProvider.Ollama || profile.Endpoint is null)
        {
            return profile.ContextWindowTokens;
        }

        var loaded = await ollamaContextWindows.TryGetContextWindowAsync(
            profile.Endpoint, profile.Model, cancellationToken);
        return loaded ?? profile.ContextWindowTokens;
    }

    private static List<ChatMessage> CreateMessages(GridletAgentRequest request)
    {
        var messages = new List<ChatMessage>(request.History.Count + 1);
        messages.AddRange(request.History.Select(message => new ChatMessage(
            message.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant,
            message.Content)));
        messages.Add(new ChatMessage(ChatRole.User, request.Message));
        return messages;
    }

    private static string SerializeToolPayload(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return json.Length <= 8_000 ? json : string.Concat(json.AsSpan(0, 8_000), "… [truncated]");
    }

    internal static IEnumerable<GridletAgentStreamEvent> DrainPendingToolEvents(
        ConcurrentQueue<GridletAgentStreamEvent> pendingToolEvents)
    {
        while (pendingToolEvents.TryDequeue(out var toolEvent))
        {
            yield return toolEvent;
        }
    }

    internal static bool TryReadFailedToolResult(
        FunctionResultContent functionResult,
        out string? toolName,
        out string? result)
    {
        toolName = null;
        result = null;
        if (functionResult.Result is AgentToolInvocationResult providerResult)
        {
            if (providerResult.Success) return false;
            toolName = providerResult.ToolName;
            result = providerResult.Result;
            return true;
        }

        if (functionResult.Result is not JsonElement item ||
            item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("type", out var type) ||
            type.GetString() != "dynamicToolCall" ||
            !item.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        toolName = item.TryGetProperty("tool", out var tool) ? tool.GetString() : null;
        if (item.TryGetProperty("contentItems", out var contentItems) &&
            contentItems.ValueKind == JsonValueKind.Array)
        {
            result = contentItems.EnumerateArray()
                .Select(content => content.TryGetProperty("text", out var text)
                    ? text.GetString()
                    : null)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        }
        return true;
    }

    private IChatClient CreateChatClient(
        GridletAgentProfileSettings profile,
        string? apiKey)
        => profile.Provider switch
        {
            GridletAgentProvider.Codex => throw new InvalidOperationException(
                "Codex profiles use the local app-server rather than an API chat client."),
            GridletAgentProvider.GitHubCopilot => throw new InvalidOperationException(
                "GitHub Copilot profiles use the local CLI rather than an API chat client."),
            GridletAgentProvider.OpenAI => CreateOpenAIChatClient(profile, apiKey!),
            GridletAgentProvider.OpenAICompatible => CreateOpenAIChatClient(
                profile, apiKey ?? "gridlet-no-api-key"),
            GridletAgentProvider.Anthropic => new global::Anthropic.AnthropicClient
            {
                ApiKey = apiKey,
            }.AsIChatClient(profile.Model, settings.MaxOutputTokens),
            GridletAgentProvider.Ollama => CreateOllamaChatClient(profile),
            _ => throw new InvalidOperationException("The configured agent provider is not supported."),
        };

    private async Task<CopilotClient> StartCopilotClientAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = new CopilotClient(new CopilotClientOptions
        {
            Connection = RuntimeConnection.ForStdio(settings.CopilotExecutablePath),
        });

        try
        {
            await client.StartAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return client;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await client.DisposeAsync();
            throw;
        }
        catch (Exception exception)
        {
            await client.DisposeAsync();
            throw new GridletAgentException(
                $"Could not start GitHub Copilot CLI using '{settings.CopilotExecutablePath}'. " +
                "Install GitHub Copilot CLI, run 'copilot login', or configure " +
                $"{nameof(GridletAgentFrameworkOptions.CopilotExecutablePath)}. {exception.Message}");
        }
    }

    private static AIAgent CreateGitHubCopilotAgent(
        CopilotClient client,
        GridletAgentProfileSettings profile,
        string instructions,
        IList<AITool> tools,
        int? maxToolCalls,
        GridletCopilotReasoningEffort? reasoningEffort)
    {
        var copilotTools = tools.OfType<AIFunction>().ToArray();
        var toolCallCount = 0;
        var toolCallLimit = maxToolCalls.GetValueOrDefault();
        var sessionConfig = new SessionConfig
        {
            Model = profile.Model,
            ReasoningEffort = ToCopilotReasoningEffort(reasoningEffort),
            ReasoningSummary = ReasoningSummary.Concise,
            Streaming = true,
            Tools = copilotTools,
            AvailableTools = new ToolSet().AddCustom("*"),
            EnableConfigDiscovery = false,
            // Copilot also treats its working directory as a project. See GridletCliWorkspace.
            WorkingDirectory = GridletCliWorkspace.Path,
            SkipCustomInstructions = true,
            Hooks = maxToolCalls.HasValue
                ? new SessionHooks
                {
                    OnPreToolUse = (_, _) =>
                    {
                        var currentToolCall = Interlocked.Increment(ref toolCallCount);
                        return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput
                        {
                            PermissionDecision = currentToolCall <= toolCallLimit
                                ? "allow"
                                : "deny",
                            AdditionalContext = currentToolCall <= toolCallLimit
                                ? null
                                : GridletPrompts.Section(
                                    "Notes/tool-call-limit", "copilot",
                                    ("limit", toolCallLimit.ToString(CultureInfo.InvariantCulture))),
                        });
                    },
                }
                : null,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = instructions,
            },
        };

        return client.AsAIAgent(
            sessionConfig,
            ownsClient: false,
            id: "GridletDatabaseAgent",
            name: "GridletDatabaseAgent",
            description: "A bounded database schema and read-only data assistant.");
    }

    private static string? ToCopilotReasoningEffort(GridletCopilotReasoningEffort? effort)
        => effort switch
        {
            null => null,
            GridletCopilotReasoningEffort.Low => "low",
            GridletCopilotReasoningEffort.Medium => "medium",
            GridletCopilotReasoningEffort.High => "high",
            GridletCopilotReasoningEffort.ExtraHigh => "xhigh",
            _ => throw new ArgumentOutOfRangeException(nameof(effort)),
        };

    private static GridletCodexReasoningEffort? GetCodexReasoningEffort(
        GridletAgentRequest request,
        GridletAgentProfileSettings profile) => request.ReasoningEffort switch
        {
            null => profile.ReasoningEffort,
            "low" => GridletCodexReasoningEffort.Low,
            "medium" => GridletCodexReasoningEffort.Medium,
            "high" => GridletCodexReasoningEffort.High,
            "xhigh" => GridletCodexReasoningEffort.ExtraHigh,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private static GridletClaudeCodeEffort? GetClaudeCodeEffort(
        GridletAgentRequest request,
        GridletAgentProfileSettings profile) => request.ReasoningEffort switch
        {
            null => profile.ClaudeCodeEffort,
            "low" => GridletClaudeCodeEffort.Low,
            "medium" => GridletClaudeCodeEffort.Medium,
            "high" => GridletClaudeCodeEffort.High,
            "xhigh" => GridletClaudeCodeEffort.ExtraHigh,
            "max" => GridletClaudeCodeEffort.Maximum,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private static GridletCopilotReasoningEffort? GetCopilotReasoningEffort(
        GridletAgentRequest request,
        GridletAgentProfileSettings profile) => request.ReasoningEffort switch
        {
            null => profile.CopilotReasoningEffort,
            "low" => GridletCopilotReasoningEffort.Low,
            "medium" => GridletCopilotReasoningEffort.Medium,
            "high" => GridletCopilotReasoningEffort.High,
            "xhigh" => GridletCopilotReasoningEffort.ExtraHigh,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private static IChatClient CreateOllamaChatClient(GridletAgentProfileSettings profile)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = profile.Endpoint,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new OllamaApiClient(httpClient, profile.Model);
    }

    private static IChatClient CreateOpenAIChatClient(
        GridletAgentProfileSettings profile,
        string apiKey)
    {
        var options = new OpenAIClientOptions();
        if (profile.Endpoint is not null)
        {
            options.Endpoint = profile.Endpoint;
        }
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        return client.GetChatClient(profile.Model).AsIChatClient();
    }

    private sealed class CliConversation : IAsyncDisposable
    {
        private readonly string connectionName;
        private readonly string? database;
        private readonly string profileId;
        private readonly bool ownerIsAuthenticated;
        private readonly string? ownerSubject;
        private readonly CliRuntimeLimiter.Lease runtimeLease;
        private long lastAccessedUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
        private int runtimeDisposed;

        public CliConversation(
            GridletAgentRequest request,
            GridletAgentProfileSettings profile,
            GridletAgentFrameworkSettings settings,
            string instructions,
            CliRuntimeLimiter.Lease runtimeLease)
        {
            connectionName = request.ConnectionName;
            database = request.Database;
            profileId = profile.Id;
            ownerIsAuthenticated = request.User.IsAuthenticated;
            ownerSubject = request.User.Subject;
            this.runtimeLease = runtimeLease;
            if (profile.Provider == GridletAgentProvider.Codex)
            {
                CodexRuntime = new CodexAppServerRuntime(settings.CodexExecutablePath);
                CodexAgentSession = CodexAppServerAgent.CreateEphemeralSession();
            }
            else if (profile.Provider == GridletAgentProvider.ClaudeCode)
            {
                ClaudeRuntime = new ClaudeCodeRuntime(
                    settings.ClaudeExecutablePath, profile.Model, instructions,
                    profile.ClaudeCodeEffort, profile.AllowsReasoningEffortSelection);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(profile));
            }
        }

        public SemaphoreSlim Gate { get; } = new(1, 1);
        public CodexAppServerRuntime? CodexRuntime { get; }
        public ClaudeCodeRuntime? ClaudeRuntime { get; }
        public AgentSession? CodexAgentSession { get; }
        public DateTimeOffset LastAccessed =>
            new(Interlocked.Read(ref lastAccessedUtcTicks), TimeSpan.Zero);

        public void Touch() =>
            Interlocked.Exchange(ref lastAccessedUtcTicks, DateTimeOffset.UtcNow.UtcTicks);

        public bool IsOwnedBy(GridletAgentUserContext user) =>
            ownerIsAuthenticated == user.IsAuthenticated &&
            (!ownerIsAuthenticated ||
             string.Equals(ownerSubject, user.Subject, StringComparison.Ordinal));

        public bool Matches(
            GridletAgentRequest request,
            GridletAgentProfileSettings profile) =>
            IsOwnedBy(request.User) &&
            string.Equals(connectionName, request.ConnectionName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(database, request.Database, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profileId, profile.Id, StringComparison.OrdinalIgnoreCase);

        public async ValueTask DisposeAsync() =>
            await DisposeAsync(CancellationToken.None);

        public async ValueTask DisposeAsync(CancellationToken cancellationToken)
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                await DisposeRuntimeAsync();
            }
            finally
            {
                Gate.Release();
            }
        }

        public async ValueTask DisposeRuntimeAsync()
        {
            if (Interlocked.Exchange(ref runtimeDisposed, 1) != 0) return;
            try
            {
                if (CodexRuntime is not null)
                {
                    await CodexRuntime.DisposeAsync();
                }
                else if (ClaudeRuntime is not null)
                {
                    await ClaudeRuntime.DisposeAsync();
                }
            }
            finally
            {
                runtimeLease.Dispose();
            }
        }
    }
}
