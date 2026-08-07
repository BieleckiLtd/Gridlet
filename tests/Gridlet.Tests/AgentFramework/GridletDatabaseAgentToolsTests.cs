using Gridlet.Abstractions;
using Gridlet.AgentFramework;
using Gridlet.Auditing;
using Gridlet.Models;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.AI;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class GridletDatabaseAgentToolsTests
{
    [Fact]
    public async Task List_database_objects_accepts_optional_schema_argument()
    {
        var function = CreateTools()
            .Create(GridletAgentMode.Data)
            .OfType<AIFunction>()
            .Single(tool => tool.Name == "list_database_objects");

        var allResult = await function.InvokeAsync(new AIFunctionArguments());
        var schemaResult = await function.InvokeAsync(new AIFunctionArguments
        {
            ["schema"] = "dbo",
        });
        var missingSchemaResult = await function.InvokeAsync(new AIFunctionArguments
        {
            ["schema"] = "missing",
        });

        Assert.Contains("Customers", allResult?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Customers", schemaResult?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Customers", missingSchemaResult?.ToString(), StringComparison.Ordinal);
    }

    private static GridletDatabaseAgentTools CreateTools()
    {
        var connection = new GridletConnectionOptions
        {
            Name = "Fake",
            ConnectionString = "fake",
            ProviderName = FakeGridletProvider.Name,
        };
        var resolved = new ResolvedConnection(
            new FakeGridletProvider(),
            new GridletConnectionContext(connection, "FakeDb"));
        var settings = new GridletAgentFrameworkSettings(
            TimeSpan.FromMinutes(15),
            ConversationIdleTimeout: TimeSpan.FromMinutes(30),
            MaxActiveConversations: 32,
            MaxHistoryMessages: 20,
            MaxHistoryCharacters: 100_000,
            MaxMessageCharacters: 20_000,
            MaxToolResultCharacters: 32_000,
            MaxQueryCharacters: 20_000,
            MaxQueryRows: 1_000,
            QueryTimeoutSeconds: 30,
            MaxToolIterations: 10,
            MaxOutputTokens: 2_000,
            CodexExecutablePath: "codex",
            ClaudeExecutablePath: "claude",
            CopilotExecutablePath: "copilot",
            Profiles: []);

        return new GridletDatabaseAgentTools(
            resolved,
            userName: null,
            settings,
            new NullAuditSink());
    }

    private sealed class NullAuditSink : IGridletAuditSink
    {
        public ValueTask WriteAsync(
            GridletAuditEvent auditEvent,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
