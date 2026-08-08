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
            c.AllowAgentDataWithPrimaryConnection = true;
        });
}

gridlet.AddAgentFramework(agents =>
    {
        agents.AddOllama("local-qwen3.5-4b", new Uri("http://127.0.0.1:11434"), "qwen3.5:4b");
        agents.AddOllama("local-qwen3.5-2b", new Uri("http://127.0.0.1:11434"), "qwen3.5:2b");
        agents.AddOllama("local-gemma4", new Uri("http://127.0.0.1:11434"), "gemma4:latest");
        agents.AddOllama("local-gemma4-12b", new Uri("http://127.0.0.1:11434"), "gemma4:12b");
        agents.AddOllama("local-qwen3.6-35b-a3b", new Uri("http://127.0.0.1:11434"), "qwen3.6:35b-a3b");
        agents.AddCodex("codex-subscription", "gpt-5.6-luna")
            .WithReasoningEffort(GridletCodexReasoningEffort.Medium)
            .AllowReasoningEffortSelection();
        // Each profile becomes a runtime-selectable entry in the Ask model dropdown.
        agents.AddClaudeCode("claude-sonnet", "sonnet", "Claude Code — Sonnet")
            .WithReasoningEffort(GridletClaudeCodeEffort.Medium)
            .AllowReasoningEffortSelection();
        agents.AddClaudeCode("claude-opus", "opus", "Claude Code — Opus")
            .WithReasoningEffort(GridletClaudeCodeEffort.High)
            .AllowReasoningEffortSelection();
        agents.AddClaudeCode("claude-haiku", "haiku", "Claude Code — Haiku")
            .WithReasoningEffort(GridletClaudeCodeEffort.Low);
        agents.AddGitHubCopilot("github-copilot", "gpt-5-mini")
            .WithReasoningEffort(GridletCopilotReasoningEffort.Medium)
            .AllowReasoningEffortSelection();
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
