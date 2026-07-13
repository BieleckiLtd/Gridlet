using Gridlet;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration["ConnectionStrings:Default"] =
    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True";

builder.Services
    .AddGridlet()
    .AddSqlServer(builder.Configuration.GetConnectionString("Default"))
    .AddSqlite("Data Source=GridletSample.db");

var app = builder.Build();
app.MapGridlet();
