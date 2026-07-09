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
}
