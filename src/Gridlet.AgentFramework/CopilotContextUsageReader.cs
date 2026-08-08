using System.Collections.Concurrent;
using GitHub.Copilot;
using Gridlet.Models;

namespace Gridlet.AgentFramework;

/// <summary>
/// Reads context-window consumption from the GitHub Copilot CLI after a turn.
/// <para>
/// Copilot pushes <c>session.usage_info</c> only to the owner of the live <c>CopilotSession</c>,
/// which the Agent Framework adapter keeps to itself. Once a turn ends the adapter releases the
/// session, so Gridlet resumes it and asks for the same numbers over the metadata API instead.
/// </para>
/// </summary>
internal sealed class CopilotContextUsageReader
{
    private readonly ConcurrentDictionary<string, ModelLimits> modelLimits =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the conversation's context usage, or <see langword="null"/> when Copilot cannot
    /// supply it. Reading usage is diagnostic only and must never fail a completed turn.
    /// </summary>
    public async Task<GridletAgentContextUsage?> TryReadAsync(
        CopilotClient client,
        string sessionId,
        string model,
        CancellationToken cancellationToken)
    {
        try
        {
            var limits = await GetModelLimitsAsync(client, model, cancellationToken);
            if (limits is null) return null;

            var resumed = await client.ResumeSessionAsync(
                sessionId,
                new ResumeSessionConfig { SuppressResumeEvent = true },
                cancellationToken);
            try
            {
                var info = (await resumed.Rpc.Metadata.ContextInfoAsync(
                    limits.Value.PromptTokens,
                    limits.Value.OutputTokens,
                    model,
                    cancellationToken)).ContextInfo;
                if (info is null || info.TotalTokens <= 0) return null;

                // The prompt budget, not the full window, is what the conversation may occupy;
                // the remainder is reserved for the model's own output.
                return new GridletAgentContextUsage(
                    info.TotalTokens,
                    limits.Value.PromptTokens,
                    info.ConversationTokens + info.SystemTokens,
                    null,
                    null);
            }
            finally
            {
                await resumed.DisposeAsync();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<ModelLimits?> GetModelLimitsAsync(
        CopilotClient client,
        string model,
        CancellationToken cancellationToken)
    {
        if (modelLimits.TryGetValue(model, out var cached)) return cached;

        var models = await client.ListModelsAsync(cancellationToken);
        foreach (var candidate in models)
        {
            if (candidate.Id is not { } id ||
                candidate.Capabilities?.Limits is not { } limits ||
                limits.MaxContextWindowTokens <= 0)
            {
                continue;
            }

            // Copilot reserves the difference between the window and the prompt budget for output.
            var promptTokens = limits.MaxPromptTokens is > 0
                ? limits.MaxPromptTokens.Value
                : limits.MaxContextWindowTokens;
            modelLimits[id] = new ModelLimits(
                promptTokens,
                Math.Max(limits.MaxContextWindowTokens - promptTokens, 0));
        }

        return modelLimits.TryGetValue(model, out var resolved) ? resolved : null;
    }

    private readonly record struct ModelLimits(int PromptTokens, int OutputTokens);
}
