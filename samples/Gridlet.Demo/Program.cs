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
    })
    .AddSqlite(
        builder.Configuration,
        "SQLite",
        relativePathBase: builder.Environment.ContentRootPath);

var app = builder.Build();

// Demo only.
var connectionString = app.Services.GetRequiredService<IOptions<GridletOptions>>()
    .Value.Connections.Single(connection => connection.ProviderName == GridletProviderNames.Sqlite)
    .ConnectionString;
await SampleDatabase.EnsureAsync(connectionString, app.Logger, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Redirect("/gridlet"));
app.MapGridlet();

app.Run();
