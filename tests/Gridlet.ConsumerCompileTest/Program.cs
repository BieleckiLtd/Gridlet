using Gridlet;
using Gridlet.AgentFramework;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration["ConnectionStrings:Default"] =
    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True";

builder.Services
    .AddGridlet()
    .AddSqlServer(builder.Configuration.GetConnectionString("Default"))
    .AddSqlite("Data Source=GridletSample.db")
    .AddAgentFramework(agents =>
    {
        agents.AddCodex("codex", "gpt-5.4")
            .WithReasoningEffort(GridletCodexReasoningEffort.High);
        agents.AddGitHubCopilot("copilot", "gpt-5")
            .WithReasoningEffort(GridletCopilotReasoningEffort.Medium);
        agents.AddOllama("local", new Uri("http://127.0.0.1:11434"), "qwen3:4b");
    });

var app = builder.Build();
app.MapGridlet();
