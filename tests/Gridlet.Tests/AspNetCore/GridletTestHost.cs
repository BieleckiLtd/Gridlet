using Gridlet.Abstractions;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gridlet.Tests.AspNetCore;

/// <summary>Boots a minimal host with Gridlet mapped over the fake provider, on an in-memory server.</summary>
internal static class GridletTestHost
{
    public static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        Action<GridletOptions> configure,
        Action<IServiceCollection>? configureServices = null,
        string pattern = "/gridlet")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddGridlet(options =>
        {
            // Isolate each test host's state file so parallel tests never share or clobber state.
            options.Storage.FilePath = Path.Combine(
                Path.GetTempPath(), $"gridlet-tests-{Guid.NewGuid():n}.json");
            configure(options);
        });
        builder.Services.AddSingleton<IGridletProvider, FakeGridletProvider>();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapGridlet(pattern);

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    /// <summary>Standard single fake connection, open access.</summary>
    public static Task<(WebApplication App, HttpClient Client)> StartDefaultAsync()
        => StartAsync(o =>
        {
            o.AddConnection("Main", "Server=secret-host;Database=hidden;", FakeGridletProvider.Name);
            o.Security.AllowAnonymous = true;
        });

    /// <summary>Boots the standard host on a real ephemeral Kestrel socket.</summary>
    public static async Task<(WebApplication App, HttpClient Client)> StartKestrelDefaultAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddGridlet(options =>
        {
            options.Storage.FilePath = Path.Combine(
                Path.GetTempPath(), $"gridlet-tests-{Guid.NewGuid():n}.json");
            options.AddConnection("Main", "Server=secret-host;Database=hidden;", FakeGridletProvider.Name);
            options.Security.AllowAnonymous = true;
        });
        builder.Services.AddSingleton<IGridletProvider, FakeGridletProvider>();

        var app = builder.Build();
        app.MapGridlet("/gridlet");
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) });
    }
}
