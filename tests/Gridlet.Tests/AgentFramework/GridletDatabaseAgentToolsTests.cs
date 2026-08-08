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

    [Fact]
    public async Task List_database_objects_filters_by_name_and_type_case_insensitively()
    {
        var function = CreateTools()
            .Create(GridletAgentMode.Data)
            .OfType<AIFunction>()
            .Single(tool => tool.Name == "list_database_objects");

        var nameResult = await function.InvokeAsync(new AIFunctionArguments
        {
            ["nameContains"] = "order",
        });
        var typeResult = await function.InvokeAsync(new AIFunctionArguments
        {
            ["objectType"] = "vIeW",
        });

        Assert.Contains("vw_Orders", nameResult?.ToString(), StringComparison.Ordinal);
        Assert.Contains("RefreshOrders", nameResult?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Customers", nameResult?.ToString(), StringComparison.Ordinal);
        Assert.Contains("vw_Orders", typeResult?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshOrders", typeResult?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_database_object_filters_compose_with_schema()
    {
        var function = CreateTools()
            .Create(GridletAgentMode.Data)
            .OfType<AIFunction>()
            .Single(tool => tool.Name == "list_database_objects");

        var result = await function.InvokeAsync(new AIFunctionArguments
        {
            ["schema"] = "missing",
            ["nameContains"] = "order",
            ["objectType"] = "View",
        });

        Assert.DoesNotContain("vw_Orders", result?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_database_object_type_is_a_recoverable_tool_error()
    {
        var function = CreateTools()
            .Create(GridletAgentMode.Data)
            .OfType<AIFunction>()
            .Single(tool => tool.Name == "list_database_objects");

        var result = await function.InvokeAsync(new AIFunctionArguments
        {
            ["objectType"] = "0",
        });

        Assert.Contains("GridletValidationException", result?.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"recoverable\":true", result?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_database_object_is_returned_as_a_recoverable_tool_error()
    {
        var function = CreateTools()
            .Create(GridletAgentMode.Data)
            .OfType<AIFunction>()
            .Single(tool => tool.Name == "describe_table");

        var result = await function.InvokeAsync(new AIFunctionArguments
        {
            ["schema"] = "dbo",
            ["name"] = "Missing",
        });

        Assert.Contains("GridletObjectNotFoundException", result?.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"recoverable\":true", result?.ToString(), StringComparison.Ordinal);
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
