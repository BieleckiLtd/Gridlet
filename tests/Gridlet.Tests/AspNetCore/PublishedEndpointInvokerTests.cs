using System.Net;
using System.Net.Http.Json;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

/// <summary>
/// The invoker lets a language model make a real HTTP call, so these tests are about what it
/// refuses as much as what it returns.
/// </summary>
public sealed class PublishedEndpointInvokerTests
{
    [Fact]
    public async Task A_published_get_endpoint_is_called_for_real_and_its_response_returned()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        await PublishAsync(client, "Top customers", "GET", "sales/top-customers");

        var result = await InvokeAsync(app.Services, "Top customers", new() { ["country"] = "Poland" });

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(200, result.StatusCode);
        Assert.Contains("/gridlet/pub/sales/top-customers", result.Url, StringComparison.Ordinal);
        Assert.Contains("country=Poland", result.Url, StringComparison.Ordinal);
        Assert.Contains("\"rows\"", result.Body!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A published endpoint runs whatever SQL was published. The write verbs are the ones somebody
    /// would use for something that changes data, so the agent may not send them at all.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Endpoints_that_are_not_get_are_refused(string method)
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        await PublishAsync(client, $"{method} customer", method, $"customers/{method.ToLowerInvariant()}");

        var result = await InvokeAsync(app.Services, $"{method} customer", []);

        Assert.False(result.Succeeded);
        Assert.Equal("method_not_invocable", result.ErrorCode);
    }

    /// <summary>
    /// The model names an endpoint, never an address, so there is no argument through which it can
    /// aim this at another host.
    /// </summary>
    [Fact]
    public async Task An_endpoint_that_is_not_published_here_cannot_be_reached()
    {
        var (app, _) = await StartAsync();
        await using var __ = app;

        var result = await InvokeAsync(app.Services, "https://example.com/steal", []);

        Assert.False(result.Succeeded);
        Assert.Equal("endpoint_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task Parameters_the_endpoint_does_not_declare_are_refused()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        await PublishAsync(client, "Top customers", "GET", "sales/top-customers");

        var result = await InvokeAsync(app.Services, "Top customers", new()
        {
            ["country"] = "Poland",
            ["limit"] = "1000",
        });

        Assert.False(result.Succeeded);
        Assert.Equal("unknown_parameter", result.ErrorCode);
    }

    [Fact]
    public async Task A_required_parameter_must_be_supplied_rather_than_guessed_at()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        await PublishAsync(client, "Top customers", "GET", "sales/top-customers");

        var result = await InvokeAsync(app.Services, "Top customers", []);

        Assert.False(result.Succeeded);
        Assert.Equal("missing_parameter", result.ErrorCode);
    }

    private static Task PublishAsync(HttpClient client, string name, string method, string route)
        => client.PostAsJsonAsync("/gridlet/api/published", new
        {
            name,
            method,
            route,
            connectionName = "Main",
            database = "FakeDb",
            sql = "SELECT * FROM dbo.Customers WHERE Country = @country",
            parameters = new[] { new { name = "country", required = true } },
        }).ContinueWith(task =>
            Assert.Equal(HttpStatusCode.OK, task.Result.StatusCode), TaskScheduler.Default);

    /// <summary>
    /// Runs the invoker the way a turn does: inside a live request, so it sees the address the
    /// browser used and can forward that caller's credentials.
    /// </summary>
    private static async Task<GridletPublishedEndpointInvocation> InvokeAsync(
        IServiceProvider services,
        string name,
        Dictionary<string, string?> query)
    {
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = "/gridlet/api/agents/chat";
        accessor.HttpContext = context;
        try
        {
            return await services.GetRequiredService<IGridletPublishedEndpointInvoker>()
                .InvokeAsync(name, query, new GridletAgentUserContext(null, null, false));
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    /// <summary>
    /// The invoker calls over real HTTP, which an in-memory test server has no socket for. Pointing
    /// its named client at the test server's handler exercises the true request path regardless.
    /// </summary>
    /// <summary>
    /// The invoker builds its own URL, so it has to read the configured segment rather than the
    /// default. If it did not, a host that moved the prefix would break the agent's ability to call
    /// its own endpoints while the browser carried on working.
    /// </summary>
    [Fact]
    public async Task The_configured_route_prefix_is_used_when_calling_an_endpoint()
    {
        var (app, client) = await StartAsync(options => options.PublishedApiRoutePrefix = "endpoints");
        await using var _ = app;
        await PublishAsync(client, "Top customers", "GET", "sales/top-customers");

        var result = await InvokeAsync(
            app.Services, "Top customers", new() { ["country"] = "Poland" });

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(200, result.StatusCode);
        Assert.Contains(
            "/gridlet/endpoints/sales/top-customers", result.Url, StringComparison.Ordinal);
    }

    private static Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client)> StartAsync(
        Action<GridletOptions>? configure = null)
        => GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=fake;", Fakes.FakeGridletProvider.Name);
                options.Security.AllowAnonymous = true;
                configure?.Invoke(options);
            },
            services => services
                .AddHttpClient("Gridlet.PublishedEndpointInvoker")
                .ConfigurePrimaryHttpMessageHandler(provider =>
                    ((TestServer)provider.GetRequiredService<IServer>()).CreateHandler()));
}
