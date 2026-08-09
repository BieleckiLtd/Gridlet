using System.Collections.Concurrent;
using System.Text.Json;

namespace Gridlet.AgentFramework;

/// <summary>
/// Reads the context window Ollama actually loaded a model with. Ollama reports token counts on
/// every response but never the window size, and the effective window is the runtime <c>num_ctx</c>
/// rather than the model's trained maximum, so only the running server can answer this.
/// </summary>
internal sealed class OllamaContextWindowProbe : IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly ConcurrentDictionary<string, CachedWindow> cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the loaded model's context window, or <see langword="null"/> when Ollama is
    /// unreachable or the model is not currently resident. Probing never fails a conversation: an
    /// absent window only means the gauge falls back to the host's declared value.
    /// </summary>
    public async Task<int?> TryGetContextWindowAsync(
        Uri endpoint,
        string model,
        CancellationToken cancellationToken)
    {
        var key = $"{endpoint}\n{model}";
        if (cache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Tokens;
        }

        var tokens = await ProbeAsync(endpoint, model, cancellationToken);
        cache[key] = new CachedWindow(tokens, DateTimeOffset.UtcNow.Add(CacheLifetime));
        return tokens;
    }

    private async Task<int?> ProbeAsync(
        Uri endpoint,
        string model,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                new Uri(endpoint, "api/ps"), cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, default, cancellationToken);
            if (!document.RootElement.TryGetProperty("models", out var models) ||
                models.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var loaded in models.EnumerateArray())
            {
                if (!Matches(loaded, model)) continue;
                if (loaded.TryGetProperty("context_length", out var window) &&
                    window.ValueKind == JsonValueKind.Number &&
                    window.TryGetInt32(out var tokens) &&
                    tokens > 0)
                {
                    return tokens;
                }
            }
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or
                                              TaskCanceledException &&
                                          !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // Ollama reports the resident model under both `name` and `model`, and a tagless request such
    // as "qwen3.5" is served by the ":latest" tag.
    internal static bool Matches(JsonElement loaded, string model)
    {
        foreach (var property in (ReadOnlySpan<string>)["name", "model"])
        {
            if (!loaded.TryGetProperty(property, out var value) ||
                value.GetString() is not { } name)
            {
                continue;
            }
            if (name.Equals(model, StringComparison.OrdinalIgnoreCase) ||
                (!model.Contains(':') &&
                 name.Equals($"{model}:latest", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }

    public void Dispose() => httpClient.Dispose();

    private readonly record struct CachedWindow(int? Tokens, DateTimeOffset ExpiresAt);
}
