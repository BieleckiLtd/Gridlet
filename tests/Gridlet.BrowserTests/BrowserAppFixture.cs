using Gridlet.Abstractions;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace Gridlet.BrowserTests;

public sealed class BrowserAppFixture : IAsyncLifetime
{
    private WebApplication? app;
    private IPlaywright? playwright;
    private string? storePath;

    public Uri BaseAddress { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public FakeGridletProvider Provider { get; } = new();

    public async Task InitializeAsync()
    {
        storePath = Path.Combine(Path.GetTempPath(), $"gridlet-browser-tests-{Guid.NewGuid():n}.json");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddGridlet(options =>
        {
            options.AddConnection("Main", "Server=browser-test;", FakeGridletProvider.Name);
            options.Security.AllowAnonymous = true;
            options.Storage.FilePath = storePath;
        });
        builder.Services.AddSingleton<IGridletProvider>(Provider);

        app = builder.Build();
        app.MapGridlet();
        await app.StartAsync();

        BaseAddress = new Uri(app.Urls.Single(url => url.StartsWith("http://", StringComparison.Ordinal)));
        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task<BrowserTestPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = true,
            BaseURL = BaseAddress.ToString(),
        });
        var page = await context.NewPageAsync();
        return new BrowserTestPage(context, page);
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        playwright?.Dispose();

        if (app is not null)
        {
            await app.DisposeAsync();
        }

        if (storePath is not null)
        {
            File.Delete(storePath);
        }
    }
}

public sealed class BrowserTestPage : IAsyncDisposable
{
    private readonly IBrowserContext context;
    private readonly List<string> errors = [];

    public BrowserTestPage(IBrowserContext context, IPage page)
    {
        this.context = context;
        Page = page;
        page.PageError += (_, error) => errors.Add($"Uncaught page error: {error}");
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                errors.Add($"Console error: {message.Text}");
            }
        };
    }

    public IPage Page { get; }

    public void AssertNoUnexpectedErrors(params string[] expectedErrorFragments)
    {
        Assert.Equal(expectedErrorFragments.Length, errors.Count);
        foreach (var expected in expectedErrorFragments)
        {
            Assert.Contains(errors, error => error.Contains(expected, StringComparison.OrdinalIgnoreCase));
        }
    }

    public ValueTask DisposeAsync() => context.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserAppFixture>
{
    public const string Name = "Gridlet browser tests";
}
