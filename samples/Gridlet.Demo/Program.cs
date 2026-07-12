using Gridlet.Demo;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("MySqlServer")
    ?? throw new InvalidOperationException("ConnectionStrings:MySqlServer is not configured.");

builder.Services.AddAuthorizationPolicies();

builder.Services
    .AddGridlet(options =>
    {
        options.AddConnection("LocalDB", connectionString);

        // Demo only.
        options.Security.AllowAnonymous = true;
        // A configured policy takes precedence over anonymous access.
        // options.Security.AuthorizationPolicy = AuthorizationExtensions.GridletAccessPolicy;

        options.Limits.MaxQueryResultRows = 999_999;
    })
    .AddSqlServer();

var app = builder.Build();

// Demo only.
await SampleDatabase.EnsureAsync(connectionString, app.Logger, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Redirect("/gridlet"));
app.MapGridlet();

app.Run();
