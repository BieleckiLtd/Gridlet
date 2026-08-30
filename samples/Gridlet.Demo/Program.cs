using Gridlet;
using Gridlet.Demo;
using Microsoft.Extensions.Options;
using Gridlet.AgentFramework;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthorizationPolicies();

var gridlet = builder.Services
    .AddGridlet(options =>
    {
        // Demo only.
        options.Security.AllowAnonymous = true;
        // A configured policy takes precedence over anonymous access.
        // options.Security.AuthorizationPolicy = AuthorizationExtensions.GridletAccessPolicy;

        options.Limits.MaxQueryResultRows = 100_000;
        options.Security.AllowAnonymousAgentCredentials = true;
    })
    .AddSqlite(
        builder.Configuration,
        "SQLite",
        c =>
        {
            //relativePathBase: builder.Environment.ContentRootPath
            c.AllowAgentSchemaAccess = true;
            c.AllowAgentDataAccess = true;
            c.AllowAgentApiAccess = true;
            c.AllowAgentDataWithPrimaryConnection = true;
        });

if (OperatingSystem.IsWindows())
{
    gridlet.AddSqlServer(
        builder.Configuration,
        "SqlServerLocalDb",
        c =>
        {
            c.AllowAgentSchemaAccess = true;
            c.AllowAgentDataAccess = true;
            c.AllowAgentApiAccess = true;
            c.AllowAgentDataWithPrimaryConnection = true;
        });
}

gridlet.AddComponents();

gridlet.AddAgentFramework(agents =>
    {
        // Gridlet reads the loaded window from Ollama itself; this declaration is only the
        // fallback for when the model is not currently resident.
        agents.AddOllama("local-qwen3.5-4b", new Uri("http://127.0.0.1:11434"), "qwen3.5:4b")
            .WithContextWindow(32_768);
        agents.AddOllama("local-qwen3.5-2b", new Uri("http://127.0.0.1:11434"), "qwen3.5:2b");
        agents.AddOllama("local-gemma4", new Uri("http://127.0.0.1:11434"), "gemma4:latest");
        agents.AddOllama("local-gemma4-12b", new Uri("http://127.0.0.1:11434"), "gemma4:12b");
        agents.AddOllama("local-qwen3.6-35b-a3b", new Uri("http://127.0.0.1:11434"), "qwen3.6:35b-a3b");
        agents.AddCodex("codex-subscription", "gpt-5.6-luna")
            .WithReasoningEffort(GridletCodexReasoningEffort.Medium)
            .AllowReasoningEffortSelection();
        // Each profile becomes a runtime-selectable entry in the Ask model dropdown.
        agents.AddClaudeCode("claude-sonnet", "sonnet", "Claude Code - Sonnet")
            .WithReasoningEffort(GridletClaudeCodeEffort.Medium)
            .AllowReasoningEffortSelection();
        agents.AddClaudeCode("claude-opus", "opus", "Claude Code - Opus")
            .WithReasoningEffort(GridletClaudeCodeEffort.High)
            .AllowReasoningEffortSelection();
        agents.AddClaudeCode("claude-haiku", "haiku", "Claude Code - Haiku")
            .WithReasoningEffort(GridletClaudeCodeEffort.Low);
        agents.AddGitHubCopilot("github-copilot", "gpt-5-mini")
            .WithReasoningEffort(GridletCopilotReasoningEffort.Medium)
            .AllowReasoningEffortSelection();
    });

// Puts a speaker button on every agent response. The browser's own synthesizer speaks it, so no
// audio is generated on the server and nothing leaves the machine.
gridlet.AddVoice(voice =>
    {
        voice.Language = "en-GB";
        // Slightly quicker than the default: schema explanations are long.
        voice.Rate = 1.05;
        // Demo only. This opts into the browser's cloud voices, which sound far better than the
        // legacy voices installed on a typical Windows machine but send the text of every spoken
        // response to the browser vendor. Remove these two lines to keep speech on the device.
        voice.AllowNetworkVoices = true;
        voice.PreferredVoice = "Microsoft Sonia Online (Natural)";
    });

var app = builder.Build();

// Demo only.
var options = app.Services.GetRequiredService<IOptions<GridletOptions>>().Value;
var sqliteConnectionString = options.Connections
    .Single(connection => connection.ProviderName == GridletProviderNames.Sqlite)
    .ConnectionString;
await SampleDatabase.EnsureAsync(sqliteConnectionString, app.Logger, app.Lifetime.ApplicationStopping);

var localDbConnection = options.Connections
    .SingleOrDefault(connection => connection.ProviderName == GridletProviderNames.SqlServer);
if (localDbConnection is not null)
{
    var initialized = await SqlServerSampleDatabase.TryEnsureAsync(
        localDbConnection.ConnectionString,
        app.Logger,
        app.Lifetime.ApplicationStopping);
    if (!initialized)
    {
        // Keep the cross-platform demo usable on Windows machines without the LocalDB workload.
        options.Connections.Remove(localDbConnection);
    }
}

app.MapGet("/", () => Results.Redirect("/gridlet"));
app.MapGridlet();

app.Run();
