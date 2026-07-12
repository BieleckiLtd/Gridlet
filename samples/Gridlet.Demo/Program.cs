using Gridlet;
using Gridlet.Demo;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var configuredConnectionString = builder.Configuration.GetConnectionString("SQLite")
    ?? throw new InvalidOperationException("ConnectionStrings:SQLite is not configured.");
var connectionBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
if (!string.Equals(connectionBuilder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) &&
    !Path.IsPathRooted(connectionBuilder.DataSource))
{
    connectionBuilder.DataSource = Path.Combine(builder.Environment.ContentRootPath, connectionBuilder.DataSource);
}
var connectionString = connectionBuilder.ConnectionString;
builder.Configuration["ConnectionStrings:SQLite"] = connectionString;

builder.Services.AddAuthorizationPolicies();

builder.Services
    .AddGridlet(options =>
    {
        options.AddConnection(builder.Configuration, "SQLite", GridletProviderNames.Sqlite);

        // Demo only.
        options.Security.AllowAnonymous = true;
        // A configured policy takes precedence over anonymous access.
        // options.Security.AuthorizationPolicy = AuthorizationExtensions.GridletAccessPolicy;

        options.Limits.MaxQueryResultRows = 999_999;
    })
    .AddSqlite();

var app = builder.Build();

// Demo only.
await SampleDatabase.EnsureAsync(connectionString, app.Logger, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Redirect("/gridlet"));
app.MapGridlet();

app.Run();
