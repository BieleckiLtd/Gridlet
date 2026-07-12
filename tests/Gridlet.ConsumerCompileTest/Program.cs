using Gridlet;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration["ConnectionStrings:Default"] =
    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True";

builder.Services
    .AddGridlet(options => options.AddConnection(
        builder.Configuration,
        "Default",
        GridletProviderNames.SqlServer))
    .AddSqlServer();

var app = builder.Build();
app.MapGridlet();
