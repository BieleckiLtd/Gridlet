using System.Text.Json;
using Gridlet.AgentFramework;
using Microsoft.Extensions.AI;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class GridletContextUsageTests
{
    [Fact]
    public void Codex_token_usage_notifications_report_the_last_request_and_its_window()
    {
        using var notification = JsonDocument.Parse(
            """
            {"method":"thread/tokenUsage/updated","params":{"tokenUsage":{
              "total":{"totalTokens":40000,"inputTokens":30000,"cachedInputTokens":10000,
                       "cacheWriteInputTokens":0,"outputTokens":10000,"reasoningOutputTokens":4000},
              "last":{"totalTokens":18000,"inputTokens":16000,"cachedInputTokens":12000,
                      "cacheWriteInputTokens":0,"outputTokens":2000,"reasoningOutputTokens":800},
              "modelContextWindow":272000}}}
            """);

        Assert.True(CodexAppServerAgent.TryCreateUsageUpdate(
            notification.RootElement, out var update));
        var usage = Assert.Single(update.Contents.OfType<UsageContent>());
        var context = GridletContextUsage.TryCreateContextUsage(usage.Details, null);

        Assert.NotNull(context);
        // The accumulated turn total must not be mistaken for context occupancy.
        Assert.Equal(18_000, context.UsedTokens);
        Assert.Equal(272_000, context.ContextWindowTokens);
        Assert.Equal(16_000, context.InputTokens);
        Assert.Equal(12_000, context.CachedInputTokens);
        Assert.Equal(2_000, context.OutputTokens);
    }

    [Fact]
    public void Codex_notifications_without_usable_counts_are_ignored()
    {
        using var notification = JsonDocument.Parse(
            """{"method":"thread/tokenUsage/updated","params":{"tokenUsage":{"total":{}}}}""");

        Assert.False(CodexAppServerAgent.TryCreateUsageUpdate(
            notification.RootElement, out _));
    }

    [Fact]
    public void Claude_stream_usage_counts_cached_and_written_input_as_occupied_context()
    {
        var usage = new ClaudeCodeRuntime.ClaudeRequestUsage();
        using var start = JsonDocument.Parse(
            """
            {"type":"message_start","message":{"usage":{"input_tokens":1200,
              "cache_read_input_tokens":8000,"cache_creation_input_tokens":800,"output_tokens":1}}}
            """);
        using var delta = JsonDocument.Parse(
            """{"type":"message_delta","usage":{"output_tokens":640}}""");

        Assert.True(ClaudeCodeRuntime.TryReadStreamUsage(start.RootElement, usage));
        Assert.True(ClaudeCodeRuntime.TryReadStreamUsage(delta.RootElement, usage));
        Assert.True(usage.TryCreateUpdate(200_000, out var update));
        var content = Assert.Single(update.Contents.OfType<UsageContent>());
        var context = GridletContextUsage.TryCreateContextUsage(content.Details, null);

        Assert.NotNull(context);
        Assert.Equal(10_640, context.UsedTokens);
        Assert.Equal(200_000, context.ContextWindowTokens);
        Assert.Equal(8_800, context.CachedInputTokens);
        Assert.Equal(640, context.OutputTokens);
    }

    [Fact]
    public void Claude_usage_is_reported_once_per_change()
    {
        var usage = new ClaudeCodeRuntime.ClaudeRequestUsage();
        using var start = JsonDocument.Parse(
            """{"type":"message_start","message":{"usage":{"input_tokens":500,"output_tokens":0}}}""");

        Assert.True(ClaudeCodeRuntime.TryReadStreamUsage(start.RootElement, usage));
        Assert.True(usage.TryCreateUpdate(null, out _));
        Assert.False(usage.TryCreateUpdate(null, out _));
    }

    [Fact]
    public void Claude_publishes_the_window_learned_after_the_counts_stopped_changing()
    {
        // Claude Code reports the context window only in the terminating `result` message, by which
        // point the token counts are final. That update must not be dropped as a duplicate.
        var usage = new ClaudeCodeRuntime.ClaudeRequestUsage();
        using var start = JsonDocument.Parse(
            """{"type":"message_start","message":{"usage":{"input_tokens":3200,"output_tokens":446}}}""");

        Assert.True(ClaudeCodeRuntime.TryReadStreamUsage(start.RootElement, usage));
        Assert.True(usage.TryCreateUpdate(null, out var streamed));
        Assert.Null(GridletContextUsage.TryCreateContextUsage(
            Assert.Single(streamed.Contents.OfType<UsageContent>()).Details, null)!.ContextWindowTokens);

        Assert.True(usage.TryCreateUpdate(200_000, out var final));
        var context = GridletContextUsage.TryCreateContextUsage(
            Assert.Single(final.Contents.OfType<UsageContent>()).Details, null);

        Assert.NotNull(context);
        Assert.Equal(200_000, context.ContextWindowTokens);
        Assert.Equal(3_646, context.UsedTokens);
        Assert.False(usage.TryCreateUpdate(200_000, out _));
    }

    [Fact]
    public void Claude_result_messages_report_the_largest_model_context_window()
    {
        using var result = JsonDocument.Parse(
            """
            {"type":"result","modelUsage":{
              "claude-haiku-4-5":{"inputTokens":10,"contextWindow":200000},
              "claude-opus-5":{"inputTokens":900,"contextWindow":500000}}}
            """);

        Assert.Equal(500_000, ClaudeCodeRuntime.ReadContextWindow(result.RootElement));
    }

    [Fact]
    public void A_provider_reported_window_wins_over_the_host_declaration()
    {
        var usage = GridletContextUsage.Create(
            inputTokens: 900, outputTokens: 100, contextWindowTokens: 128_000);

        var context = GridletContextUsage.TryCreateContextUsage(usage.Details, 8_000);

        Assert.NotNull(context);
        Assert.Equal(128_000, context.ContextWindowTokens);
    }

    [Fact]
    public void The_host_declaration_sizes_providers_that_only_report_token_counts()
    {
        var usage = GridletContextUsage.Create(inputTokens: 900, outputTokens: 100);

        var context = GridletContextUsage.TryCreateContextUsage(usage.Details, 8_000);

        Assert.NotNull(context);
        Assert.Equal(1_000, context.UsedTokens);
        Assert.Equal(8_000, context.ContextWindowTokens);
    }

    [Theory]
    [InlineData("qwen3.5:0.8b", true)]
    [InlineData("QWEN3.5:0.8B", true)]
    [InlineData("qwen3.5", false)]
    [InlineData("qwen3.5:4b", false)]
    public void Ollama_matches_a_loaded_model_by_its_exact_tag(string requested, bool expected)
    {
        using var loaded = JsonDocument.Parse(
            """{"name":"qwen3.5:0.8b","model":"qwen3.5:0.8b","context_length":262144}""");

        Assert.Equal(expected, OllamaContextWindowProbe.Matches(loaded.RootElement, requested));
    }

    [Fact]
    public void Ollama_resolves_a_tagless_model_name_to_the_latest_tag()
    {
        using var loaded = JsonDocument.Parse(
            """{"name":"gemma4:latest","model":"gemma4:latest","context_length":262144}""");

        Assert.True(OllamaContextWindowProbe.Matches(loaded.RootElement, "gemma4"));
        Assert.False(OllamaContextWindowProbe.Matches(loaded.RootElement, "gemma4:31b"));
    }

    [Fact]
    public void Usage_without_token_counts_produces_no_gauge()
    {
        var context = GridletContextUsage.TryCreateContextUsage(new UsageDetails(), 8_000);

        Assert.Null(context);
    }
}
