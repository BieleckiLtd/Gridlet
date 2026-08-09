using Gridlet.AgentFramework;
using Microsoft.Extensions.AI;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

/// <summary>
/// Every instruction Gridlet gives a model is a file rather than a string literal, so nothing the
/// compiler used to catch about it is caught any more: a renamed file, a mistyped section name, or
/// a parameter description that no longer matches its method all become runtime failures inside a
/// conversation. These tests ask for each of them once so the failure happens here instead.
/// </summary>
public sealed class GridletPromptTests
{
    [Theory]
    [InlineData("Instructions/base")]
    [InlineData("Instructions/product-briefing")]
    [InlineData("Instructions/access")]
    [InlineData("Instructions/access-state")]
    [InlineData("Instructions/database-environment")]
    [InlineData("Instructions/installation")]
    [InlineData("Instructions/cli-claude-code")]
    [InlineData("Instructions/cli-codex")]
    [InlineData("Notes/shared-access")]
    public void Loads_every_prompt_file_the_agent_needs(string path)
        => Assert.NotEmpty(GridletPrompts.Text(path));

    [Theory]
    [InlineData("Instructions/access-state", "shared")]
    [InlineData("Instructions/access-state", "not-shared")]
    [InlineData("Instructions/access-state", "host-disabled")]
    [InlineData("Notes/access-denied", "not-shared-message")]
    [InlineData("Notes/access-denied", "not-shared-next-step")]
    [InlineData("Notes/access-denied", "not-configured-message")]
    [InlineData("Notes/access-denied", "not-configured-next-step")]
    [InlineData("Notes/deployment", "with-installation")]
    [InlineData("Notes/deployment", "without-installation")]
    [InlineData("Notes/tool-call-limit", "claude-code")]
    [InlineData("Notes/tool-call-limit", "codex")]
    [InlineData("Notes/tool-call-limit", "copilot")]
    public void Loads_every_prompt_section_the_agent_needs(string path, string section)
        => Assert.NotEmpty(GridletPrompts.Section(path, section));

    [Fact]
    public void Reports_a_missing_prompt_file_by_the_path_an_editor_would_look_for()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => GridletPrompts.Text("Instructions/not-written-yet"));

        Assert.Contains(
            "Prompts/Instructions/not-written-yet.md", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_a_missing_section_by_the_heading_an_editor_would_look_for()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => GridletPrompts.Section("Notes/deployment", "no-such-section"));

        Assert.Contains("## no-such-section", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_maintainer_comments_out_of_the_text_the_model_sees()
    {
        // Every file that carries a comment carries it at the top, so a comment that survived
        // would be the first thing in the prompt.
        Assert.DoesNotContain("<!--", GridletPrompts.Text("Instructions/installation"), StringComparison.Ordinal);
        Assert.StartsWith("This Gridlet installation", GridletPrompts.Text("Instructions/installation"), StringComparison.Ordinal);
    }

    [Fact]
    public void Substitutes_only_the_tokens_it_was_given()
    {
        var text = GridletPrompts.Text(
            "Instructions/installation",
            ("base_address", "https://example.test"),
            ("mount", "https://example.test/gridlet"),
            ("published_pattern", "https://example.test/gridlet/endpoints/{route}"));

        // {route} is a placeholder the person is meant to read, so it survives substitution.
        Assert.Contains(
            "https://example.test/gridlet/endpoints/{route}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{mount}", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Adding a guide topic is meant to be dropping a file into <c>Guide/</c>, so this checks the
    /// rules that make that work rather than pinning the exact list — which would turn every new
    /// topic into a failing test for no reason.
    /// </summary>
    [Fact]
    public void Names_guide_topics_from_their_files_without_exposing_the_order_prefix()
    {
        var topics = GridletPrompts.GuideTopics;

        Assert.NotEmpty(topics);
        Assert.Equal(topics.Distinct(StringComparer.OrdinalIgnoreCase).Count(), topics.Count);
        foreach (var topic in topics)
        {
            Assert.False(
                char.IsAsciiDigit(topic[0]),
                $"Guide topic '{topic}' still carries its ordering prefix.");
            Assert.DoesNotContain('.', topic);
            Assert.NotNull(GridletPrompts.Guide(topic));
        }

        // The number prefix orders the list, so the overview leads it however many topics exist.
        Assert.Equal("overview", topics[0]);
    }

    /// <summary>
    /// The agent has no way to discover the interface, so anything it cannot read here it will
    /// answer with SQL instead — which is how "how do I delete a customer?" once turned into a
    /// lecture about `DELETE` rather than "select the row and press Delete".
    /// </summary>
    [Fact]
    public void Documents_the_grid_row_editing_a_person_would_reach_for_first()
    {
        var guide = GridletPrompts.Guide("editing-data");

        Assert.NotNull(guide);
        Assert.Contains("press `Delete`", guide, StringComparison.Ordinal);
        Assert.Contains("＋ Row", guide, StringComparison.Ordinal);
        // The three conditions that make the grid read-only, which is what somebody asks about
        // when the feature is missing for them.
        Assert.Contains("AllowWrites", guide, StringComparison.Ordinal);
        Assert.Contains("primary key", guide, StringComparison.Ordinal);
    }

    /// <summary>
    /// A published endpoint is edited in place from the Published APIs tab. Without this documented
    /// the agent reasons out a plausible-sounding workflow instead — "edit the original query and
    /// publish it again" — which does not work, because publishing copies the SQL rather than
    /// linking to it, and following it would leave somebody with two endpoints.
    /// </summary>
    [Fact]
    public void Documents_editing_a_published_endpoint_in_place()
    {
        var guide = GridletPrompts.Guide("published-api");

        Assert.NotNull(guide);
        Assert.Contains("Published APIs", guide, StringComparison.Ordinal);
        Assert.Contains("Save endpoint", guide, StringComparison.Ordinal);
        Assert.Contains("copies", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinguishes_read_only_agent_tools_from_Gridlets_destructive_workflows()
    {
        var instructions = GridletPrompts.Text("Instructions/base");
        var guide = GridletPrompts.Guide("object-management");

        Assert.Contains("Gridlet itself is not", instructions, StringComparison.Ordinal);
        Assert.Contains("object-management", instructions, StringComparison.Ordinal);
        Assert.NotNull(guide);
        Assert.Contains("right-click", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Delete object…", guide, StringComparison.Ordinal);
        Assert.Contains("`DROP TABLE`", guide, StringComparison.Ordinal);
        Assert.Contains("Open in Query", guide, StringComparison.Ordinal);
        Assert.Contains("external database client", guide, StringComparison.Ordinal);
        Assert.Contains("not on Gridlet's interactive features",
            GridletPrompts.Guide("security"), StringComparison.Ordinal);
    }

    [Fact]
    public void Distinguishes_published_api_access_from_direct_database_data_access()
    {
        var access = GridletPrompts.Text("Instructions/access");
        var guide = GridletPrompts.Guide("ask");

        Assert.Contains("does not grant access to the read-only query tool", access,
            StringComparison.Ordinal);
        Assert.Contains("Only when you invoke an endpoint is its response shared", access,
            StringComparison.Ordinal);
        Assert.Contains("separate from Data", guide, StringComparison.Ordinal);
        Assert.Contains("grants no direct database-query access", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void Serves_a_guide_topic_and_rejects_one_that_has_no_file()
    {
        Assert.Contains("table designer", GridletPrompts.Guide("designer"), StringComparison.Ordinal);
        Assert.Null(GridletPrompts.Guide("no-such-topic"));
    }

    [Fact]
    public void Describes_every_tool_and_every_parameter_from_its_file()
    {
        var tools = GridletDatabaseAgentToolNames
            .Select(name => GridletDatabaseAgentTools.Describe(
                GridletDatabaseAgentToolStubs.For(name), name, ("topics", "overview")))
            .ToArray();

        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name);
            if (!tool.JsonSchema.TryGetProperty("properties", out var properties)) continue;

            foreach (var parameter in properties.EnumerateObject())
            {
                Assert.True(
                    parameter.Value.TryGetProperty("description", out var description) &&
                    !string.IsNullOrWhiteSpace(description.GetString()),
                    $"Tool '{tool.Name}' has no description for parameter '{parameter.Name}'.");
            }
        }
    }

    [Fact]
    public void Rejects_a_parameter_section_that_names_nothing_on_the_method()
    {
        // list_schemas takes no parameters, so its file must not describe any. Pointing the loader
        // at a file that does proves the mismatch is caught rather than quietly dropped.
        var exception = Assert.Throws<InvalidOperationException>(
            () => GridletDatabaseAgentTools.Describe(
                () => Task.FromResult(string.Empty), "describe_table"));

        Assert.Contains("does not take", exception.Message, StringComparison.Ordinal);
    }

    private static readonly string[] GridletDatabaseAgentToolNames =
    [
        "list_schemas", "list_database_objects", "describe_table", "get_object_definition",
        "execute_read_only_query", "get_shared_database_access", "request_database_access",
        "get_gridlet_guide", "describe_gridlet_deployment", "list_published_api_endpoints",
        "invoke_published_api_endpoint", "list_saved_queries",
    ];

    /// <summary>
    /// Stand-ins with the same parameters as the real tool methods, which need a live database
    /// connection to construct. Only the shape of each signature matters here.
    /// </summary>
    private static class GridletDatabaseAgentToolStubs
    {
        public static Delegate For(string name) => name switch
        {
            "list_database_objects" => (string? schema, string? nameContains, string? objectType)
                => Task.FromResult(string.Empty),
            "describe_table" or "get_object_definition" => (string schema, string name)
                => Task.FromResult(string.Empty),
            "execute_read_only_query" => (string sql) => Task.FromResult(string.Empty),
            "request_database_access" => (string scope, string reason) => Task.FromResult(string.Empty),
            "get_gridlet_guide" => (string? topic) => Task.FromResult(string.Empty),
            "invoke_published_api_endpoint" => (string name, string? parameters)
                => Task.FromResult(string.Empty),
            _ => () => Task.FromResult(string.Empty),
        };
    }
}
