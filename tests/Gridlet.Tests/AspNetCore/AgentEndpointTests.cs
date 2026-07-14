using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Gridlet.Abstractions;
using Gridlet.Models;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class AgentEndpointTests
{
    private const string TestSchemeName = "AgentEndpointTests";
    private const string DataChat = "/gridlet/api/connections/Main/databases/Reporting/agents/data/chat";
    private const string SchemaChat = "/gridlet/api/connections/Main/databases/Reporting/agents/schema/chat";

    [Fact]
    public async Task Unknown_connection_and_null_history_are_rejected_without_invoking_the_agent()
    {
        var agent = new FakeGridletAgentService();
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name, connection =>
                    connection.AllowAgentSchemaAccess = true);
                options.Security.AllowAnonymous = true;
            },
            services => services.AddSingleton<IGridletAgentService>(agent));
        await using var _ = app;

        var unknown = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Unknown/databases/Reporting/agents/schema/chat",
            ValidChatBody());
        var malformed = await client.PostAsJsonAsync(SchemaChat, new
        {
            profileId = FakeGridletAgentService.ProfileId,
            message = "hello",
            history = new object?[] { null },
        });

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Empty(agent.Requests);
    }

    [Fact]
    public async Task Credential_provider_errors_are_sanitized_and_anonymous_byok_is_off_by_default()
    {
        const string secret = "sk-must-not-leak";
        var agent = new FakeGridletAgentService
        {
            CredentialException = new InvalidOperationException(secret),
        };
        var (anonymousApp, anonymousClient) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                options.Security.AllowAnonymous = true;
            },
            services => services.AddSingleton<IGridletAgentService>(agent));
        await using var _ = anonymousApp;

        var anonymous = await anonymousClient.PostAsJsonAsync(
            $"/gridlet/api/agents/{FakeGridletAgentService.ProfileId}/credentials",
            new { apiKey = secret });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                options.Security.AllowAnonymous = true;
                options.Security.AllowAnonymousAgentCredentials = true;
            },
            services => services.AddSingleton<IGridletAgentService>(agent));
        await using var __ = app;

        var failed = await client.PostAsJsonAsync(
            $"/gridlet/api/agents/{FakeGridletAgentService.ProfileId}/credentials",
            new { apiKey = secret });
        var body = await failed.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.Contains("could not be completed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Meta_exposes_safe_profiles_and_connection_gates_without_secrets()
    {
        var agent = new FakeGridletAgentService();
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection(
                    "DefaultOff",
                    "Server=default-off-secret;Database=Reporting;",
                    FakeGridletProvider.Name);
                options.AddConnection(
                    "AgentEnabled",
                    "Server=agent-enabled-secret;Database=Reporting;",
                    FakeGridletProvider.Name,
                    connection =>
                    {
                        connection.AllowAgentDataAccess = true;
                        connection.AgentDataConnectionString =
                            "Server=readonly-agent-secret;Database=Reporting;";
                        connection.AllowAgentSchemaAccess = true;
                    });
                options.Security.AllowAnonymous = true;
            },
            services => services.AddSingleton<IGridletAgentService>(agent));
        await using var _ = app;

        var json = await client.GetStringAsync("/gridlet/api/meta");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var profiles = root.GetProperty("agent").GetProperty("profiles").EnumerateArray().ToArray();
        Assert.Equal(2, profiles.Length);
        Assert.Equal(FakeGridletAgentService.ProfileId, profiles[0].GetProperty("id").GetString());
        Assert.Equal("fake-model-v1", profiles[0].GetProperty("model").GetString());
        Assert.True(profiles[0].GetProperty("allowsUserApiKey").GetBoolean());
        Assert.True(profiles[0].GetProperty("requiresUserApiKey").GetBoolean());
        Assert.True(profiles[1].GetProperty("isLocal").GetBoolean());

        var connections = root.GetProperty("connections").EnumerateArray()
            .ToDictionary(connection => connection.GetProperty("name").GetString()!);
        Assert.False(connections["DefaultOff"].GetProperty("allowAgentDataAccess").GetBoolean());
        Assert.False(connections["DefaultOff"].GetProperty("allowAgentSchemaAccess").GetBoolean());
        Assert.True(connections["AgentEnabled"].GetProperty("allowAgentDataAccess").GetBoolean());
        Assert.True(connections["AgentEnabled"].GetProperty("allowAgentSchemaAccess").GetBoolean());

        Assert.DoesNotContain(FakeGridletAgentService.ConfiguredApiKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeGridletAgentService.ProviderEndpoint, json, StringComparison.Ordinal);
        Assert.DoesNotContain("default-off-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("agent-enabled-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("readonly-agent-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"apiKey\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"endpoint\":", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Absent_agent_service_is_reported_safely_by_meta_and_routes()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name, connection =>
            {
                connection.AllowAgentDataAccess = true;
                connection.AllowAgentDataWithPrimaryConnection = true;
                connection.AllowAgentSchemaAccess = true;
            });
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        using var meta = JsonDocument.Parse(await client.GetStringAsync("/gridlet/api/meta"));
        Assert.Equal(JsonValueKind.Null, meta.RootElement.GetProperty("agent").ValueKind);

        var credential = await client.PostAsJsonAsync(
            $"/gridlet/api/agents/{FakeGridletAgentService.ProfileId}/credentials",
            new { apiKey = "sk-user-secret" });
        var chat = await client.PostAsJsonAsync(DataChat, ValidChatBody());

        Assert.Equal(HttpStatusCode.NotFound, credential.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, chat.StatusCode);
        Assert.Contains("not configured", await credential.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not configured", await chat.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("schema")]
    public async Task Agent_access_is_off_by_default(string mode)
    {
        var agent = new FakeGridletAgentService();
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                options.Security.AllowAnonymous = true;
            },
            services => services.AddSingleton<IGridletAgentService>(agent));
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            $"/gridlet/api/connections/Main/databases/Reporting/agents/{mode}/chat",
            ValidChatBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(agent.Requests);
    }

    [Theory]
    [InlineData("data", false, true)]
    [InlineData("schema", true, false)]
    public async Task Per_mode_connection_gates_are_enforced_independently(
        string requestedMode,
        bool allowData,
        bool allowSchema)
    {
        var agent = new FakeGridletAgentService();
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name, connection =>
                {
                    connection.AllowAgentDataAccess = allowData;
                    connection.AllowAgentDataWithPrimaryConnection = allowData;
                    connection.AllowAgentSchemaAccess = allowSchema;
                });
                options.Security.AllowAnonymous = true;
            },
            services => services.AddSingleton<IGridletAgentService>(agent));
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            $"/gridlet/api/connections/Main/databases/Reporting/agents/{requestedMode}/chat",
            ValidChatBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(agent.Requests);
    }

    [Theory]
    [InlineData("data", GridletAgentMode.Data)]
    [InlineData("schema", GridletAgentMode.Schema)]
    public async Task Chat_streams_ndjson_and_forwards_the_complete_provider_neutral_request(
        string routeMode,
        GridletAgentMode expectedMode)
    {
        var agent = new FakeGridletAgentService();
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name, connection =>
                {
                    connection.AllowAgentDataAccess = true;
                    connection.AllowAgentDataWithPrimaryConnection = true;
                    connection.AllowAgentSchemaAccess = true;
                });
                options.Security.AllowAnonymous = true;
            },
            services => services.AddSingleton<IGridletAgentService>(agent));
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            $"/gridlet/api/connections/Main/databases/Reporting/agents/{routeMode}/chat",
            new
            {
                profileId = FakeGridletAgentService.ProfileId,
                message = "What should I know?",
                history = new[]
                {
                    new { role = "user", content = "Earlier question" },
                    new { role = "assistant", content = "Earlier answer" },
                },
                credentialHandle = FakeGridletAgentService.CredentialHandle,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType!.MediaType);
        var events = (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<GridletAgentStreamEvent>(line, JsonSerializerOptions.Web)!)
            .ToArray();
        Assert.Collection(
            events,
            started => Assert.Equal("started", started.Type),
            content =>
            {
                Assert.Equal("content", content.Type);
                Assert.Contains(routeMode, content.Content, StringComparison.Ordinal);
                Assert.Equal("assistant", content.Name);
            },
            completed => Assert.Equal("completed", completed.Type));

        var request = Assert.Single(agent.Requests);
        Assert.Equal("Main", request.ConnectionName);
        Assert.Equal("Reporting", request.Database);
        Assert.Equal(expectedMode, request.Mode);
        Assert.Equal(FakeGridletAgentService.ProfileId, request.ProfileId);
        Assert.Equal("What should I know?", request.Message);
        Assert.Equal(FakeGridletAgentService.CredentialHandle, request.CredentialHandle);
        Assert.False(request.User.IsAuthenticated);
        Assert.Null(request.User.DisplayName);
        Assert.Collection(
            request.History,
            message =>
            {
                Assert.Equal("user", message.Role);
                Assert.Equal("Earlier question", message.Content);
            },
            message =>
            {
                Assert.Equal("assistant", message.Role);
                Assert.Equal("Earlier answer", message.Content);
            });
    }

    [Fact]
    public async Task User_credential_can_be_stored_by_profile_and_deleted_by_opaque_handle()
    {
        const string userApiKey = "sk-user-supplied-secret";
        var agent = new FakeGridletAgentService();
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                options.Security.AllowAnonymous = true;
                options.Security.AllowAnonymousAgentCredentials = true;
            },
            services => services.AddSingleton<IGridletAgentService>(agent));
        await using var _ = app;

        var store = await client.PostAsJsonAsync(
            $"/gridlet/api/agents/{FakeGridletAgentService.ProfileId}/credentials",
            new { apiKey = userApiKey });
        var storeBody = await store.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, store.StatusCode);
        Assert.DoesNotContain(userApiKey, storeBody, StringComparison.Ordinal);
        using var storedDocument = JsonDocument.Parse(storeBody);
        var handle = storedDocument.RootElement.GetProperty("handle").GetString();
        Assert.Equal(FakeGridletAgentService.CredentialHandle, handle);
        var stored = Assert.Single(agent.StoredCredentials);
        Assert.Equal(FakeGridletAgentService.ProfileId, stored.ProfileId);
        Assert.Equal(userApiKey, stored.ApiKey);
        Assert.False(stored.User.IsAuthenticated);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/gridlet/api/agents/credentials")
        {
            Content = JsonContent.Create(new { handle }),
        };
        var delete = await client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var removed = Assert.Single(agent.RemovedCredentials);
        Assert.Equal(FakeGridletAgentService.CredentialHandle, removed.Handle);
        Assert.False(removed.User.IsAuthenticated);
    }

    [Fact]
    public async Task Data_and_schema_chat_can_require_distinct_authorization_policies()
    {
        var agent = new FakeGridletAgentService();
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name, connection =>
                {
                    connection.AllowAgentDataAccess = true;
                    connection.AllowAgentDataWithPrimaryConnection = true;
                    connection.AllowAgentSchemaAccess = true;
                });
                options.Security.AllowAnonymous = true;
                options.Security.AgentDataAuthorizationPolicy = "AgentData";
                options.Security.AgentSchemaAuthorizationPolicy = "AgentSchema";
                options.Security.AgentCredentialAuthorizationPolicy = "AgentCredentials";
            },
            services =>
            {
                services.AddSingleton<IGridletAgentService>(agent);
                services.AddAuthentication(TestSchemeName)
                    .AddScheme<AuthenticationSchemeOptions, AgentModeAuthHandler>(TestSchemeName, null);
                services.AddAuthorizationBuilder()
                    .AddPolicy("AgentData", policy => policy.RequireClaim("agent_mode", "data"))
                    .AddPolicy("AgentSchema", policy => policy.RequireClaim("agent_mode", "schema"))
                    .AddPolicy("AgentCredentials", policy => policy.RequireClaim("agent_mode", "credentials"));
            });
        await using var _ = app;

        var anonymous = await client.PostAsJsonAsync(DataChat, ValidChatBody());
        var dataAllowed = await SendAuthorizedChatAsync(client, DataChat, "data", "data@example.com");
        var dataDeniedSchema = await SendAuthorizedChatAsync(client, SchemaChat, "data", "data@example.com");
        var schemaAllowed = await SendAuthorizedChatAsync(client, SchemaChat, "schema", "schema@example.com");
        var schemaDeniedData = await SendAuthorizedChatAsync(client, DataChat, "schema", "schema@example.com");
        var credentialDenied = await SendAuthorizedCredentialAsync(client, "data", "data@example.com");
        var credentialAllowed = await SendAuthorizedCredentialAsync(
            client, "credentials", "credentials@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dataAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, dataDeniedSchema.StatusCode);
        Assert.Equal(HttpStatusCode.OK, schemaAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, schemaDeniedData.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, credentialDenied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, credentialAllowed.StatusCode);
        Assert.Equal("credentials@example.com", Assert.Single(agent.StoredCredentials).User.DisplayName);
        Assert.Collection(
            agent.Requests,
            request =>
            {
                Assert.Equal(GridletAgentMode.Data, request.Mode);
                Assert.Equal("data@example.com", request.User.DisplayName);
            },
            request =>
            {
                Assert.Equal(GridletAgentMode.Schema, request.Mode);
                Assert.Equal("schema@example.com", request.User.DisplayName);
            });
    }

    private static object ValidChatBody()
        => new
        {
            profileId = FakeGridletAgentService.ProfileId,
            message = "Hello database",
        };

    private static async Task<HttpResponseMessage> SendAuthorizedChatAsync(
        HttpClient client,
        string path,
        string mode,
        string userName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(ValidChatBody()),
        };
        request.Headers.Add("X-Test-Agent-Mode", mode);
        request.Headers.Add("X-Test-User", userName);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAuthorizedCredentialAsync(
        HttpClient client,
        string mode,
        string userName)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/gridlet/api/agents/{FakeGridletAgentService.ProfileId}/credentials")
        {
            Content = JsonContent.Create(new { apiKey = "sk-policy-test" }),
        };
        request.Headers.Add("X-Test-Agent-Mode", mode);
        request.Headers.Add("X-Test-User", userName);
        return await client.SendAsync(request);
    }

    private sealed class AgentModeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Agent-Mode", out var mode) ||
                !Request.Headers.TryGetValue("X-Test-User", out var user) ||
                string.IsNullOrWhiteSpace(mode) ||
                string.IsNullOrWhiteSpace(user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, user.ToString()),
                new Claim("agent_mode", mode.ToString()),
            ], TestSchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestSchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
