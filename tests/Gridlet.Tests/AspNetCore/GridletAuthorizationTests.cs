using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class GridletAuthorizationTests
{
    private const string Scheme = "Test";

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user) || string.IsNullOrEmpty(user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.ToString())], Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private static Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client)> StartSecuredAsync()
        => GridletTestHost.StartAsync(
            o => o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name),
            services =>
            {
                services.AddAuthentication(Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(Scheme, null);
                services.AddAuthorization();
            });

    [Fact]
    public async Task Endpoints_require_authentication_by_default()
    {
        var (app, client) = await StartSecuredAsync();
        await using var _ = app;

        var ui = await client.GetAsync("/gridlet");
        var api = await client.GetAsync("/gridlet/api/meta");

        Assert.Equal(HttpStatusCode.Unauthorized, ui.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task Authenticated_users_get_access()
    {
        var (app, client) = await StartSecuredAsync();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("X-Test-User", "admin@example.com");
        var ui = await client.GetAsync("/gridlet");
        var api = await client.GetAsync("/gridlet/api/meta");

        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }

    [Fact]
    public async Task Configured_authorization_policy_overrides_AllowAnonymous()
    {
        var (app, client) = await GridletTestHost.StartAsync(
            o =>
            {
                o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                o.Security.AllowAnonymous = true;
                o.Security.AuthorizationPolicy = "Admins";
            },
            services =>
            {
                services.AddAuthentication(Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(Scheme, null);
                services.AddAuthorizationBuilder()
                    .AddPolicy("Admins", policy => policy.RequireAuthenticatedUser());
            });
        await using var _ = app;

        var anonymous = await client.GetAsync("/gridlet/api/meta");

        client.DefaultRequestHeaders.Add("X-Test-User", "admin@example.com");
        var authenticated = await client.GetAsync("/gridlet/api/meta");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    /// <summary>
    /// A session holds an open connection and possibly an uncommitted transaction, so it must not be
    /// reachable by anybody except the person who opened it.
    /// </summary>
    [Fact]
    public async Task A_query_session_is_reachable_only_by_the_user_who_opened_it()
    {
        var (app, client) = await StartSecuredAsync();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("X-Test-User", "ada@example.com");
        var opened = await client.PostAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/sessions", null);
        opened.EnsureSuccessStatusCode();
        var id = System.Text.Json.JsonDocument.Parse(await opened.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var owner = await client.GetAsync($"/gridlet/api/sessions/{id}");
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", "grace@example.com");
        var stranger = await client.GetAsync($"/gridlet/api/sessions/{id}");
        var strangerList = await client.GetStringAsync("/gridlet/api/sessions");
        var strangerClose = await client.DeleteAsync($"/gridlet/api/sessions/{id}");

        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerClose.StatusCode);
        Assert.Equal("[]", strangerList);
    }
}
