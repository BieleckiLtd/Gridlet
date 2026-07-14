using Gridlet;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration["ConnectionStrings:Default"] =
    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True";

builder.Services
    .AddGridlet()
    .AddSqlServer(builder.Configuration.GetConnectionString("Default"))
    .AddSqlite("Data Source=GridletSample.db")
    .AddAgentFramework(agents =>
        agents.AddOllama("local", new Uri("http://127.0.0.1:11434"), "qwen3:4b"));

var app = builder.Build();
app.MapGridlet();
