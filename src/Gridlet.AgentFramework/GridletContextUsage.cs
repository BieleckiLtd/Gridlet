using Gridlet.Models;
using Microsoft.Extensions.AI;

namespace Gridlet.AgentFramework;

/// <summary>
/// Carries provider-reported context-window consumption through Agent Framework updates. Providers
/// that report token usage without a window size omit <see cref="ContextWindowTokens"/>; the host's
/// configured profile window is then used instead.
/// </summary>
internal static class GridletContextUsage
{
    /// <summary>
    /// Key used to smuggle a provider-reported context-window size through
    /// <see cref="UsageDetails.AdditionalCounts"/>, which is the only provider-neutral usage
    /// channel Agent Framework exposes.
    /// </summary>
    internal const string ContextWindowCountKey = "gridlet.context_window";

    /// <summary>Key used for the cached portion of the input tokens, when a provider reports it.</summary>
    internal const string CachedInputCountKey = "gridlet.cached_input_tokens";

    internal static UsageContent Create(
        long? inputTokens,
        long? outputTokens,
        long? cachedInputTokens = null,
        long? totalTokens = null,
        long? contextWindowTokens = null)
    {
        var details = new UsageDetails
        {
            InputTokenCount = inputTokens,
            OutputTokenCount = outputTokens,
            TotalTokenCount = totalTokens ?? (inputTokens + outputTokens),
        };
        if (cachedInputTokens is > 0)
        {
            details.AdditionalCounts ??= [];
            details.AdditionalCounts[CachedInputCountKey] = cachedInputTokens.Value;
        }
        if (contextWindowTokens is > 0)
        {
            details.AdditionalCounts ??= [];
            details.AdditionalCounts[ContextWindowCountKey] = contextWindowTokens.Value;
        }
        return new UsageContent(details);
    }

    /// <summary>
    /// Converts provider usage into the browser-visible shape, or returns <see langword="null"/>
    /// when the provider reported nothing usable.
    /// </summary>
    internal static GridletAgentContextUsage? TryCreateContextUsage(
        UsageDetails usage,
        int? configuredContextWindowTokens)
    {
        var cached = ReadAdditionalCount(usage, CachedInputCountKey);
        var used = usage.TotalTokenCount ??
                   (usage.InputTokenCount + usage.OutputTokenCount);
        if (used is not > 0) return null;

        var window = ReadAdditionalCount(usage, ContextWindowCountKey) ??
                     configuredContextWindowTokens;
        return new GridletAgentContextUsage(
            used.Value,
            window is > 0 ? window : null,
            usage.InputTokenCount,
            cached,
            usage.OutputTokenCount);
    }

    private static long? ReadAdditionalCount(UsageDetails usage, string key)
        => usage.AdditionalCounts is not null &&
           usage.AdditionalCounts.TryGetValue(key, out var value) &&
           value > 0
            ? value
            : null;
}
