using Gridlet.Abstractions;
using Gridlet.AgentFramework;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class GridletAgentRegistrationTests
{
    [Fact]
    public void Registers_safe_profile_metadata_without_exposing_provider_secrets_or_endpoints()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGridlet()
            .AddAgentFramework(options =>
            {
                options.AddOpenAI("openai", "gpt-5-mini")
                    .WithServerApiKey("sk-server-secret")
                    .AllowUserApiKeys();
                options.AddCodex("codex", "gpt-5.4")
                    .WithReasoningEffort(GridletCodexReasoningEffort.High);
                options.AddGitHubCopilot("copilot", "gpt-5")
                    .WithReasoningEffort(GridletCopilotReasoningEffort.Medium);
                options.AddOllama(
                    "local", new Uri("http://127.0.0.1:11434"), "qwen3:4b");
            });

        using var provider = services.BuildServiceProvider();
        var agent = provider.GetRequiredService<IGridletAgentService>();
        var serialized = System.Text.Json.JsonSerializer.Serialize(agent.Info);

        Assert.Equal(4, agent.Info.Profiles.Count);
        Assert.False(agent.Info.Profiles[0].RequiresUserApiKey);
        Assert.True(agent.Info.Profiles[0].AllowsUserApiKey);
        Assert.False(agent.Info.Profiles[1].AllowsUserApiKey);
        Assert.False(agent.Info.Profiles[1].RequiresUserApiKey);
        Assert.False(agent.Info.Profiles[2].AllowsUserApiKey);
        Assert.False(agent.Info.Profiles[2].RequiresUserApiKey);
        Assert.True(agent.Info.Profiles[3].IsLocal);
        Assert.DoesNotContain("sk-server-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_duplicate_profile_ids_during_registration()
    {
        var services = new ServiceCollection();
        var builder = services.AddGridlet();

        var exception = Assert.Throws<GridletValidationException>(() =>
            builder.AddAgentFramework(options =>
            {
                options.AddOllama("local", new Uri("http://localhost:11434"), "qwen3:4b");
                options.AddOllama("LOCAL", new Uri("http://localhost:11434"), "llama3.2");
            }));

        Assert.Contains("more than once", exception.Message);
    }

    [Fact]
    public void Rejects_api_keys_for_subscription_backed_codex_profiles()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<GridletValidationException>(() =>
            services.AddGridlet().AddAgentFramework(options =>
                options.AddCodex("codex", "gpt-5.4").AllowUserApiKeys()));

        Assert.Contains("cannot accept API keys", exception.Message);
    }

    [Fact]
    public void Rejects_api_keys_for_subscription_backed_copilot_profiles()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<GridletValidationException>(() =>
            services.AddGridlet().AddAgentFramework(options =>
                options.AddGitHubCopilot("copilot", "gpt-5").WithServerApiKey("github-token")));

        Assert.Contains("cannot accept API keys", exception.Message);
    }

    [Fact]
    public void Allows_disabling_the_gridlet_tool_iteration_limit()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddGridlet().AddAgentFramework(options =>
        {
            options.MaxToolIterations = null;
            options.AddCodex("codex", "gpt-5.4");
        });

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IGridletAgentService>());
    }
}
