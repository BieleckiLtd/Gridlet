using Gridlet.Abstractions;
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
                options.AddOllama(
                    "local", new Uri("http://127.0.0.1:11434"), "qwen3:4b");
            });

        using var provider = services.BuildServiceProvider();
        var agent = provider.GetRequiredService<IGridletAgentService>();
        var serialized = System.Text.Json.JsonSerializer.Serialize(agent.Info);

        Assert.Equal(2, agent.Info.Profiles.Count);
        Assert.False(agent.Info.Profiles[0].RequiresUserApiKey);
        Assert.True(agent.Info.Profiles[0].AllowsUserApiKey);
        Assert.True(agent.Info.Profiles[1].IsLocal);
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
}
