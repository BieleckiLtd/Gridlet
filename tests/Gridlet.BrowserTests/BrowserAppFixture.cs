using Gridlet.Abstractions;
using Gridlet.Models;
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

    public BrowserGridletAgentService Agent { get; } = new();

    private IGridletProvider SqliteUiProvider => new BrowserSqliteProvider(Provider);

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
            options.AddConnection("Main", "Server=browser-test;", FakeGridletProvider.Name, connection =>
            {
                connection.AllowAgentDataAccess = true;
                connection.AllowAgentDataWithPrimaryConnection = true;
                connection.AllowAgentSchemaAccess = true;
            });
            options.AddConnection("SQLite", "Data Source=browser-test.db;", BrowserSqliteProvider.Name);
            options.Security.AllowAnonymous = true;
            options.Security.AllowAnonymousAgentCredentials = true;
            options.Storage.FilePath = storePath;
        });
        builder.Services.AddSingleton<IGridletProvider>(Provider);
        builder.Services.AddSingleton<IGridletAgentService>(Agent);
        builder.Services.AddSingleton(SqliteUiProvider);

        app = builder.Build();
        app.MapGridlet();
        await app.StartAsync();

        BaseAddress = new Uri(app.Urls.Single(url => url.StartsWith("http://", StringComparison.Ordinal)));
        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // Lets restricted development environments reuse an installed Chromium-based browser.
            ExecutablePath = Environment.GetEnvironmentVariable("GRIDLET_PLAYWRIGHT_EXECUTABLE_PATH"),
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

internal sealed class BrowserSqliteProvider(FakeGridletProvider inner) : IGridletProvider, IGridletProviderMetadata
{
    public const GridletProviderNames Name = GridletProviderNames.Sqlite;

    public GridletProviderNames ProviderName => Name;

    public GridletProviderCapabilities Capabilities { get; } = new(
        DefaultSchema: "main",
        SupportsSchemas: false,
        SupportsViews: true,
        SupportsStoredProcedures: false,
        SupportsFunctions: false,
        SupportsTriggers: true,
        SupportsClusteredPrimaryKeys: false,
        SuggestedDataTypes: ["INTEGER", "TEXT", "REAL", "BLOB", "NUMERIC"],
        SelectExample: "SELECT * FROM {object} LIMIT 100;",
        CreateTriggerExample:
            "CREATE TRIGGER [main].[NewTrigger]\nAFTER INSERT ON [Customers]\nBEGIN\n    SELECT 1;\nEND;",
        ObjectEditMode: "Recreate");

    public ISchemaReader Schema => inner.Schema;

    public ITableDataService Data => inner.Data;

    public IQueryRunner Query => inner.Query;

    public ITableWriteService Writes => inner.Writes;

    public ITableDdlService Ddl => inner.Ddl;
}

public sealed class BrowserGridletAgentService : IGridletAgentService
{
    private readonly FakeGridletAgentService inner = new();

    public GridletAgentInfo Info => inner.Info;

    public List<GridletAgentRequest> Requests => inner.Requests;

    public List<(string ProfileId, string ApiKey, GridletAgentUserContext User)> StoredCredentials =>
        inner.StoredCredentials;

    public List<(string ConversationId, GridletAgentUserContext User)> ClosedConversations =>
        inner.ClosedConversations;

    public Task<GridletAgentCredential> StoreCredentialAsync(
        string profileId,
        string apiKey,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default) =>
        inner.StoreCredentialAsync(profileId, apiKey, user, cancellationToken);

    public Task RemoveCredentialAsync(
        string credentialHandle,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default) =>
        inner.RemoveCredentialAsync(credentialHandle, user, cancellationToken);

    public async IAsyncEnumerable<GridletAgentStreamEvent> ChatAsync(
        GridletAgentRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request.Message.Contains("slow streaming scroll", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(request);
            yield return new GridletAgentStreamEvent("started");
            yield return new GridletAgentStreamEvent(
                "content",
                string.Join('\n', Enumerable.Range(1, 100).Select(index =>
                    $"Initial streamed line {index:000}: enough content to make the conversation scroll.")));
            await Task.Delay(300, cancellationToken);
            yield return new GridletAgentStreamEvent(
                "content",
                "\nA later streamed chunk must not move a reader who scrolled upward.");
            await Task.Delay(100, cancellationToken);
            yield return new GridletAgentStreamEvent("completed");
            yield break;
        }

        if (request.Message.Contains("slow cancellation", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(request);
            yield return new GridletAgentStreamEvent("started");
            yield return new GridletAgentStreamEvent("reasoning", "Waiting on a deliberately slow provider.");
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            yield return new GridletAgentStreamEvent("content", "This response should have been cancelled.");
            yield return new GridletAgentStreamEvent("completed");
            yield break;
        }

        if (request.Message.Contains("report unsized context usage", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(request);
            yield return new GridletAgentStreamEvent("started");
            yield return new GridletAgentStreamEvent(
                "usage",
                System.Text.Json.JsonSerializer.Serialize(
                    new GridletAgentContextUsage(12_500),
                    new System.Text.Json.JsonSerializerOptions(
                        System.Text.Json.JsonSerializerDefaults.Web)));
            yield return new GridletAgentStreamEvent("content", "Usage reported without a window.");
            yield return new GridletAgentStreamEvent("completed");
            yield break;
        }

        if (request.Message.Contains("report context usage", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(request);
            yield return new GridletAgentStreamEvent("started");
            yield return new GridletAgentStreamEvent(
                "usage",
                System.Text.Json.JsonSerializer.Serialize(
                    new GridletAgentContextUsage(48_000, 64_000, 44_000, 30_000, 4_000),
                    new System.Text.Json.JsonSerializerOptions(
                        System.Text.Json.JsonSerializerDefaults.Web)));
            yield return new GridletAgentStreamEvent("content", "Usage reported.");
            yield return new GridletAgentStreamEvent("completed");
            yield break;
        }

        if (request.Message.Contains("fail during reasoning", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(request);
            yield return new GridletAgentStreamEvent("started");
            yield return new GridletAgentStreamEvent("reasoning", "The provider is about to fail.");
            await Task.Delay(20, cancellationToken);
            yield return new GridletAgentStreamEvent("error", "Deliberate streamed failure.");
            yield break;
        }

        await foreach (var agentEvent in inner.ChatAsync(request, cancellationToken))
        {
            yield return agentEvent;
        }
    }

    public Task CloseConversationAsync(
        string conversationId,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default) =>
        inner.CloseConversationAsync(conversationId, user, cancellationToken);
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
