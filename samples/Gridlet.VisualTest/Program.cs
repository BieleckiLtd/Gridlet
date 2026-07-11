using Gridlet.VisualTest;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("SqlServer")
    ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is not configured.");

const string oddSecondPolicy = "OddSecond";

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(oddSecondPolicy, policy =>
    {
        // Deliberately time-dependent for the demo: allow requests during odd UTC seconds.
        policy.RequireAssertion(_ => DateTimeOffset.UtcNow.Second % 2 == 1);
    });

builder.Services
    .AddGridlet(options =>
    {
        options.AddConnection("LocalDB", connectionString);

        // Visual test host only: the Gridlet endpoints are wide open. In a real host,
        // leave AllowAnonymous = false and configure authentication plus a policy, e.g.
        // options.Security.AuthorizationPolicy = "DbAdmins";
        options.Security.AllowAnonymous = true;
    })
    .AddSqlServer();

var app = builder.Build();

// Create and seed the GridletSample database on first run so there is something to look at.
await SampleDatabase.EnsureAsync(connectionString, app.Logger, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Redirect("/gridlet"));
app.MapGridlet();

app.Run();
