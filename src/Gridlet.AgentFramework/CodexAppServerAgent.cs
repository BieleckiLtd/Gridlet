using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Gridlet;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Gridlet.AgentFramework;

/// <summary>
/// Microsoft Agent Framework adapter for the local Codex app-server. The Codex process owns
/// ChatGPT authentication; this adapter never receives or persists its credentials.
/// </summary>
internal sealed class CodexAppServerAgent(
    string executablePath,
    string model,
    string instructions,
    IReadOnlyList<AIFunction> tools,
    int? maxToolIterations,
    GridletCodexReasoningEffort? reasoningEffort) : AIAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string CapabilityInstructions = """

        The host exposes the only tools you may use as dynamic tools. Do not use shell commands,
        filesystem tools, web search, MCP tools, apps, skills, subagents, or request additional
        permissions. Do not inspect the host computer. Answer only from the user's messages and
        results returned by the host-provided dynamic tools.
        """;

    public override string Name => "GridletCodexAppServerAgent";

    public override string Description =>
        "A bounded database assistant backed by a locally authenticated Codex app-server.";

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<AgentSession>(new CodexAppServerSession());
    }

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var codexSession = GetSession(session);
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(
            new CodexSessionState(codexSession.ThreadId), jsonSerializerOptions ?? JsonOptions));
    }

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedSession,
        JsonSerializerOptions? jsonSerializerOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = serializedSession.Deserialize<CodexSessionState>(jsonSerializerOptions ?? JsonOptions)
            ?? throw new JsonException("The Codex agent session is invalid.");
        return ValueTask.FromResult<AgentSession>(new CodexAppServerSession(state.ThreadId));
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        await foreach (var update in RunCoreStreamingAsync(
                           messages, session, options, cancellationToken))
        {
            text.Append(update.Text);
        }

        return new AgentResponse(new ChatMessage(ChatRole.Assistant, text.ToString()));
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = options;
        var messageList = messages.ToList();
        if (messageList.Count == 0 || string.IsNullOrWhiteSpace(messageList[^1].Text))
        {
            throw new ArgumentException("At least one non-empty message is required.", nameof(messages));
        }

        // A null Agent Framework session is intentionally ephemeral. Gridlet supplies the
        // browser-held history on every request and must not persist database conversations in
        // Codex's local thread store. Explicit Agent Framework sessions remain resumable.
        var codexSession = session as CodexAppServerSession ?? new CodexAppServerSession();
        await using var client = await CodexAppServerClient.StartAsync(
            executablePath, cancellationToken);

        await client.InitializeAsync(cancellationToken);
        var account = await client.RequestAsync(
            "account/read", new { refreshToken = false }, cancellationToken);
        var accountType = account.TryGetProperty("account", out var accountElement) &&
                          accountElement.ValueKind == JsonValueKind.Object &&
                          accountElement.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;
        if (!string.Equals(accountType, "chatgpt", StringComparison.Ordinal))
        {
            throw new GridletAgentException(
                "The local Codex runtime is not signed in with ChatGPT. Run 'codex login' " +
                "as the operating-system user that hosts this application, then try again.");
        }

        if (string.IsNullOrWhiteSpace(codexSession.ThreadId))
        {
            var started = await client.RequestAsync(
                "thread/start",
                new
                {
                    model,
                    ephemeral = session is null,
                    approvalPolicy = "never",
                    sandbox = "read-only",
                    baseInstructions = string.Concat(instructions, CapabilityInstructions),
                    serviceName = "gridlet",
                    dynamicTools = tools.Select(tool => new
                    {
                        type = "function",
                        name = tool.Name,
                        description = tool.Description ?? string.Empty,
                        inputSchema = tool.JsonSchema,
                    }).ToArray(),
                },
                cancellationToken);
            codexSession.ThreadId = started.GetProperty("thread").GetProperty("id").GetString()
                ?? throw new GridletAgentException("Codex app-server returned an invalid thread id.");

            if (messageList.Count > 1)
            {
                await client.RequestAsync(
                    "thread/inject_items",
                    new
                    {
                        threadId = codexSession.ThreadId,
                        items = messageList.Take(messageList.Count - 1)
                            .Select(CreateHistoryItem).ToArray(),
                    },
                    cancellationToken);
            }
        }
        else
        {
            await client.RequestAsync(
                "thread/resume",
                new { threadId = codexSession.ThreadId },
                cancellationToken);
        }

        await client.RequestAsync(
            "turn/start",
            new
            {
                threadId = codexSession.ThreadId,
                input = new[] { new { type = "text", text = messageList[^1].Text } },
                approvalPolicy = "never",
                sandboxPolicy = new { type = "readOnly", networkAccess = false },
                model,
                effort = ToWireValue(reasoningEffort),
                summary = "concise",
            },
            cancellationToken);

        string? turnError = null;
        var toolIterations = 0;
        var refusedToolCalls = 0;
        var reasoningItems = new Dictionary<string, CodexReasoningItemState>(StringComparer.Ordinal);
        while (true)
        {
            using var message = await client.ReadMessageAsync(cancellationToken);
            var root = message.RootElement;
            var method = root.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString()
                : null;

            if (root.TryGetProperty("id", out _) && method is not null)
            {
                if (method == "item/tool/call" &&
                    maxToolIterations is int toolLimit &&
                    toolIterations >= toolLimit)
                {
                    await client.RespondAsync(root.GetProperty("id").Clone(), new
                    {
                        contentItems = new[]
                        {
                            new
                            {
                                type = "inputText",
                                text = "The tool-call limit was reached. Do not call another tool. " +
                                       "Finish the answer using the information already collected, " +
                                       "and clearly state any remaining uncertainty.",
                            },
                        },
                        success = false,
                    }, cancellationToken);

                    if (++refusedToolCalls >= 3)
                    {
                        throw new GridletAgentException(
                            $"The Codex agent continued requesting tools after reaching the " +
                            $"limit of {toolLimit} tool calls.");
                    }
                    continue;
                }
                if (method == "item/tool/call")
                {
                    toolIterations++;
                }
                await HandleServerRequestAsync(client, root, cancellationToken);
                continue;
            }

            if (method == "item/agentMessage/delta")
            {
                var delta = root.GetProperty("params").GetProperty("delta").GetString();
                if (!string.IsNullOrEmpty(delta))
                {
                    yield return new AgentResponseUpdate(ChatRole.Assistant, delta);
                }
            }
            else if (method == "item/reasoning/summaryTextDelta")
            {
                var parameters = root.GetProperty("params");
                var delta = parameters.GetProperty("delta").GetString();
                if (!string.IsNullOrEmpty(delta))
                {
                    GetReasoningState(reasoningItems, parameters)
                        .GetSummary(parameters.GetProperty("summaryIndex").GetInt32())
                        .Append(delta);
                    yield return new AgentResponseUpdate(
                        ChatRole.Assistant, [CreateReasoningContent(delta, "reasoning")]);
                }
            }
            else if (method == "item/reasoning/summaryPartAdded")
            {
                var parameters = root.GetProperty("params");
                GetReasoningState(reasoningItems, parameters)
                    .GetSummary(parameters.GetProperty("summaryIndex").GetInt32());
                yield return new AgentResponseUpdate(
                    ChatRole.Assistant, [CreateReasoningContent(string.Empty, "reasoning-section")]);
            }
            else if (method == "item/reasoning/textDelta")
            {
                var parameters = root.GetProperty("params");
                var delta = parameters.GetProperty("delta").GetString();
                if (!string.IsNullOrEmpty(delta))
                {
                    GetReasoningState(reasoningItems, parameters)
                        .GetContent(parameters.GetProperty("contentIndex").GetInt32())
                        .Append(delta);
                    yield return new AgentResponseUpdate(
                        ChatRole.Assistant, [CreateReasoningContent(delta, "reasoning-raw")]);
                }
            }
            else if (method is "item/started" or "item/completed")
            {
                if (TryCreateToolUpdate(root, out var toolUpdate))
                {
                    yield return toolUpdate;
                }
                else if (method == "item/completed")
                {
                    foreach (var update in CreateCompletedReasoningUpdates(root, reasoningItems))
                    {
                        yield return update;
                    }
                }
            }
            else if (method == "error")
            {
                turnError = ReadErrorMessage(root);
            }
            else if (method == "turn/completed")
            {
                var turn = root.GetProperty("params").GetProperty("turn");
                var status = turn.GetProperty("status").GetString();
                if (status == "completed")
                {
                    yield break;
                }

                turnError ??= turn.TryGetProperty("error", out var error) &&
                              error.ValueKind == JsonValueKind.Object &&
                              error.TryGetProperty("message", out var errorMessage)
                    ? errorMessage.GetString()
                    : null;
                throw new GridletAgentException(
                    turnError ?? $"The Codex turn ended with status '{status}'.");
            }
        }
    }

    private static TextReasoningContent CreateReasoningContent(string text, string kind)
        => new(text) { RawRepresentation = new CodexReasoningEvent(kind) };

    private static CodexReasoningItemState GetReasoningState(
        IDictionary<string, CodexReasoningItemState> states,
        JsonElement parameters)
    {
        var itemId = parameters.GetProperty("itemId").GetString()!;
        if (!states.TryGetValue(itemId, out var state))
        {
            state = new CodexReasoningItemState();
            states.Add(itemId, state);
        }
        return state;
    }

    private static IEnumerable<AgentResponseUpdate> CreateCompletedReasoningUpdates(
        JsonElement notification,
        IDictionary<string, CodexReasoningItemState> states)
    {
        var item = notification.GetProperty("params").GetProperty("item");
        if (!item.TryGetProperty("type", out var type) || type.GetString() != "reasoning")
        {
            yield break;
        }

        var itemId = item.GetProperty("id").GetString()!;
        if (!states.TryGetValue(itemId, out var state))
        {
            state = new CodexReasoningItemState();
            states.Add(itemId, state);
        }

        foreach (var update in CreateAuthoritativeReasoningUpdates(
                     item, "summary", state.Summaries, "reasoning", "reasoning-final"))
        {
            yield return update;
        }
        foreach (var update in CreateAuthoritativeReasoningUpdates(
                     item, "content", state.Contents, "reasoning-raw", "reasoning-raw-final"))
        {
            yield return update;
        }
    }

    private static IEnumerable<AgentResponseUpdate> CreateAuthoritativeReasoningUpdates(
        JsonElement item,
        string propertyName,
        IReadOnlyDictionary<int, StringBuilder> streamed,
        string deltaKind,
        string replacementKind)
    {
        if (!item.TryGetProperty(propertyName, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            var finalText = value.GetString() ?? string.Empty;
            var streamedText = streamed.TryGetValue(index++, out var builder)
                ? builder.ToString()
                : string.Empty;
            if (string.Equals(finalText, streamedText, StringComparison.Ordinal))
            {
                continue;
            }

            var kind = finalText.StartsWith(streamedText, StringComparison.Ordinal)
                ? deltaKind
                : replacementKind;
            var text = kind == deltaKind ? finalText[streamedText.Length..] : finalText;
            if (!string.IsNullOrEmpty(text))
            {
                yield return new AgentResponseUpdate(
                    ChatRole.Assistant, [CreateReasoningContent(text, kind)]);
            }
        }
    }

    private async Task HandleServerRequestAsync(
        CodexAppServerClient client,
        JsonElement request,
        CancellationToken cancellationToken)
    {
        var method = request.GetProperty("method").GetString();
        var id = request.GetProperty("id").Clone();
        if (method != "item/tool/call")
        {
            await client.RespondAsync(id, "decline", cancellationToken);
            return;
        }

        var parameters = request.GetProperty("params");
        var toolName = parameters.GetProperty("tool").GetString();
        var tool = tools.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, toolName, StringComparison.Ordinal));
        if (tool is null)
        {
            await client.RespondAsync(id, new
            {
                contentItems = new[]
                {
                    new { type = "inputText", text = "The requested tool is not available." },
                },
                success = false,
            }, cancellationToken);
            return;
        }

        try
        {
            var arguments = new AIFunctionArguments();
            if (parameters.TryGetProperty("arguments", out var argumentsElement) &&
                argumentsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in argumentsElement.EnumerateObject())
                {
                    arguments[property.Name] = property.Value.Clone();
                }
            }

            var result = await tool.InvokeAsync(arguments, cancellationToken);
            var text = result as string ?? JsonSerializer.Serialize(result, JsonOptions);
            await client.RespondAsync(id, new
            {
                contentItems = new[] { new { type = "inputText", text } },
                success = true,
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await client.RespondAsync(id, new
            {
                contentItems = new[]
                {
                    new { type = "inputText", text = $"Tool execution failed: {exception.Message}" },
                },
                success = false,
            }, cancellationToken);
        }
    }

    private static object CreateHistoryItem(ChatMessage message)
    {
        var role = message.Role == ChatRole.Assistant ? "assistant" : "user";
        var contentType = role == "assistant" ? "output_text" : "input_text";
        return new
        {
            type = "message",
            role,
            content = new[] { new { type = contentType, text = message.Text } },
        };
    }

    private static bool TryCreateToolUpdate(
        JsonElement notification,
        out AgentResponseUpdate update)
    {
        update = null!;
        var item = notification.GetProperty("params").GetProperty("item");
        if (!item.TryGetProperty("type", out var type) ||
            type.GetString() != "dynamicToolCall")
        {
            return false;
        }

        var id = item.GetProperty("id").GetString() ?? string.Empty;
        if (notification.GetProperty("method").GetString() == "item/started")
        {
            var arguments = new Dictionary<string, object?>();
            if (item.TryGetProperty("arguments", out var argumentElement) &&
                argumentElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in argumentElement.EnumerateObject())
                {
                    arguments[property.Name] = property.Value.Clone();
                }
            }
            update = new AgentResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent(id, item.GetProperty("tool").GetString()!, arguments)]);
        }
        else
        {
            update = new AgentResponseUpdate(
                ChatRole.Assistant,
                [new FunctionResultContent(id, item.Clone())]);
        }
        return true;
    }

    private static string? ReadErrorMessage(JsonElement notification)
    {
        var parameters = notification.GetProperty("params");
        return parameters.TryGetProperty("error", out var error) &&
               error.ValueKind == JsonValueKind.Object &&
               error.TryGetProperty("message", out var message)
            ? message.GetString()
            : null;
    }

    private static string? ToWireValue(GridletCodexReasoningEffort? effort) => effort switch
    {
        null => null,
        GridletCodexReasoningEffort.Low => "low",
        GridletCodexReasoningEffort.Medium => "medium",
        GridletCodexReasoningEffort.High => "high",
        GridletCodexReasoningEffort.ExtraHigh => "xhigh",
        _ => throw new ArgumentOutOfRangeException(nameof(effort)),
    };

    private static CodexAppServerSession GetSession(AgentSession? session) =>
        session as CodexAppServerSession
        ?? throw new ArgumentException("The session was not created by this Codex agent.", nameof(session));

    private sealed class CodexReasoningItemState
    {
        public Dictionary<int, StringBuilder> Summaries { get; } = [];

        public Dictionary<int, StringBuilder> Contents { get; } = [];

        public StringBuilder GetSummary(int index) => GetOrAdd(Summaries, index);

        public StringBuilder GetContent(int index) => GetOrAdd(Contents, index);

        private static StringBuilder GetOrAdd(
            IDictionary<int, StringBuilder> values,
            int index)
        {
            if (!values.TryGetValue(index, out var value))
            {
                value = new StringBuilder();
                values.Add(index, value);
            }
            return value;
        }
    }

    private sealed class CodexAppServerSession(string? threadId = null) : AgentSession
    {
        public string? ThreadId { get; set; } = threadId;
    }

    private sealed record CodexSessionState(string? ThreadId);
}

internal sealed record CodexReasoningEvent(string Kind);

internal sealed class CodexAppServerClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly Process _process;
    private readonly StreamWriter _input;
    private readonly StreamReader _output;
    private readonly Task<string> _stderr;
    private long _nextId;

    private CodexAppServerClient(Process process)
    {
        _process = process;
        _input = process.StandardInput;
        _output = process.StandardOutput;
        _stderr = process.StandardError.ReadToEndAsync();
    }

    public static Task<CodexAppServerClient> StartAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedExecutablePath = ResolveExecutablePath(executablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");
        // A Gridlet Codex profile is a database agent, not a coding agent. Disable the local
        // Codex capabilities that could reach outside the bounded dynamic-tool bridge.
        foreach (var feature in new[]
                 {
                     "apps", "browser_use", "computer_use", "image_generation",
                     "in_app_browser", "multi_agent", "plugins", "shell_tool",
                 })
        {
            startInfo.ArgumentList.Add("--disable");
            startInfo.ArgumentList.Add(feature);
        }
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("mcp_servers={}");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("web_search=\"disabled\"");

        try
        {
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The Codex process did not start.");
            return Task.FromResult(new CodexAppServerClient(process));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new GridletAgentException(
                $"Could not start the local Codex CLI using '{executablePath}'. " +
                $"Install Codex or configure CodexExecutablePath. {exception.Message}");
        }
    }

    internal static string ResolveExecutablePath(string executablePath)
    {
        if (Path.IsPathFullyQualified(executablePath) ||
            executablePath.Contains(Path.DirectorySeparatorChar) ||
            executablePath.Contains(Path.AltDirectorySeparatorChar))
        {
            return executablePath;
        }

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"'))
            .Where(directory => directory.Length > 0)
            .ToList();

        if (OperatingSystem.IsWindows())
        {
            var applicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(applicationData))
            {
                directories.Add(Path.Combine(applicationData, "npm"));
            }
        }

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, executablePath + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return executablePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await RequestAsync("initialize", new
        {
            clientInfo = new { name = "gridlet", title = "Gridlet", version = "1.0.0" },
            capabilities = new { experimentalApi = true },
        }, cancellationToken);
        await SendAsync(new { method = "initialized", @params = new { } }, cancellationToken);
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        await SendAsync(new { method, id, @params = parameters }, cancellationToken);
        while (true)
        {
            using var message = await ReadMessageAsync(cancellationToken);
            var root = message.RootElement;
            if (!root.TryGetProperty("id", out var responseId) ||
                responseId.ValueKind != JsonValueKind.Number ||
                responseId.GetInt64() != id ||
                root.TryGetProperty("method", out _))
            {
                continue;
            }
            if (root.TryGetProperty("error", out var error))
            {
                var text = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : error.GetRawText();
                throw new GridletAgentException($"Codex app-server rejected '{method}': {text}");
            }
            return root.GetProperty("result").Clone();
        }
    }

    public Task RespondAsync(
        JsonElement id,
        object result,
        CancellationToken cancellationToken) =>
        SendAsync(new { id, result }, cancellationToken);

    public async Task<JsonDocument> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var line = await _output.ReadLineAsync(cancellationToken);
        if (line is not null)
        {
            try
            {
                return JsonDocument.Parse(line);
            }
            catch (JsonException exception)
            {
                throw new GridletAgentException(
                    $"Codex app-server wrote an invalid JSON-RPC message. {exception.Message}");
            }
        }

        await _process.WaitForExitAsync(cancellationToken);
        var stderr = await _stderr;
        throw new GridletAgentException(
            $"Codex app-server exited unexpectedly with code {_process.ExitCode}. " +
            (string.IsNullOrWhiteSpace(stderr) ? string.Empty : stderr.Trim()));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _input.Close();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
            await _process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // The process exited between the checks above.
        }
        finally
        {
            _process.Dispose();
        }
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, SerializerOptions);
        await _input.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _input.FlushAsync(cancellationToken);
    }
}
