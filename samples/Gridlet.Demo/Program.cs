using Gridlet;
using Gridlet.Demo;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthorizationPolicies();

builder.Services
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
        })
    .AddAgentFramework(agents =>
    {
        agents.AddOllama("local-qwen3.5-4b", new Uri("http://127.0.0.1:11434"), "qwen3.5:4b");
        agents.AddOllama("local-qwen3.5-2b", new Uri("http://127.0.0.1:11434"), "qwen3.5:2b");
        agents.AddOllama("local-qwen3.5-0.8b", new Uri("http://127.0.0.1:11434"), "qwen3.5:0.8b");
        agents.AddOllama("local-gemma4-12b", new Uri("http://127.0.0.1:11434"), "gemma4:12b");
        agents.AddOllama("local-qwen3.6-35b-a3b", new Uri("http://127.0.0.1:11434"), "qwen3.6:35b-a3b");
    });

var app = builder.Build();

// Demo only.
var connectionString = app.Services.GetRequiredService<IOptions<GridletOptions>>()
    .Value.Connections.Single(connection => connection.ProviderName == GridletProviderNames.Sqlite)
    .ConnectionString;
await SampleDatabase.EnsureAsync(connectionString, app.Logger, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Redirect("/gridlet"));
app.MapGridlet();

app.Run();
