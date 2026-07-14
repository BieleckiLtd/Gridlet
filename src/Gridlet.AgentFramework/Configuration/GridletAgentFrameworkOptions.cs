using System.Collections.ObjectModel;
using Gridlet.Models;

namespace Gridlet.AgentFramework;

/// <summary>Host-controlled configuration for Gridlet's Microsoft Agent Framework integration.</summary>
public sealed class GridletAgentFrameworkOptions
{
    private readonly List<GridletAgentProfileBuilder> _profiles = [];

    /// <summary>How long a browser-supplied API key remains available in process memory.</summary>
    public TimeSpan CredentialLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Maximum number of client-supplied history messages accepted for one turn.</summary>
    public int MaxHistoryMessages { get; set; } = 50;

    /// <summary>Maximum combined character count of client-supplied history for one turn.</summary>
    public int MaxHistoryCharacters { get; set; } = 200_000;

    /// <summary>Maximum character count of the current user message.</summary>
    public int MaxMessageCharacters { get; set; } = 20_000;

    /// <summary>Maximum serialized characters returned by any database tool to a model.</summary>
    public int MaxToolResultCharacters { get; set; } = 32_000;

    /// <summary>Maximum SQL characters accepted by the data-mode query tool.</summary>
    public int MaxQueryCharacters { get; set; } = 20_000;

    /// <summary>Maximum rows retained per result set by the data-mode query tool.</summary>
    public int MaxQueryRows { get; set; } = 100;

    /// <summary>Database command timeout used by the data-mode query tool.</summary>
    public int QueryTimeoutSeconds { get; set; } = 120;

    /// <summary>Maximum model/tool round trips permitted during one chat turn.</summary>
    public int MaxToolIterations { get; set; } = 8;

    /// <summary>Maximum output tokens requested from the configured model.</summary>
    public int MaxOutputTokens { get; set; } = 4_096;

    /// <summary>Adds a profile using OpenAI's hosted API.</summary>
    public GridletAgentProfileBuilder AddOpenAI(
        string id,
        string model,
        string? displayName = null)
        => Add(id, displayName ?? "OpenAI", model, GridletAgentProvider.OpenAI, endpoint: null);

    /// <summary>Adds a profile using Anthropic's hosted Claude API.</summary>
    public GridletAgentProfileBuilder AddAnthropic(
        string id,
        string model,
        string? displayName = null)
        => Add(id, displayName ?? "Anthropic", model, GridletAgentProvider.Anthropic, endpoint: null);

    /// <summary>
    /// Adds an OpenAI Chat Completions-compatible profile. The endpoint is supplied only by the
    /// host and can never be overridden by an agent request.
    /// </summary>
    public GridletAgentProfileBuilder AddOpenAICompatible(
        string id,
        Uri endpoint,
        string model,
        string? displayName = null)
        => Add(id, displayName ?? "OpenAI compatible", model,
            GridletAgentProvider.OpenAICompatible, endpoint);

    /// <summary>
    /// Adds a local Ollama profile. The endpoint and model are supplied only by the host.
    /// </summary>
    public GridletAgentProfileBuilder AddOllama(
        string id,
        Uri endpoint,
        string model,
        string? displayName = null)
        => Add(id, displayName ?? "Ollama", model, GridletAgentProvider.Ollama, endpoint);

    private GridletAgentProfileBuilder Add(
        string id,
        string displayName,
        string model,
        GridletAgentProvider provider,
        Uri? endpoint)
    {
        var profile = new GridletAgentProfileBuilder(id, displayName, model, provider, endpoint);
        _profiles.Add(profile);
        return profile;
    }

    internal GridletAgentFrameworkSettings Build()
    {
        ValidateRange(CredentialLifetime > TimeSpan.Zero && CredentialLifetime <= TimeSpan.FromDays(1),
            nameof(CredentialLifetime), "greater than zero and no more than one day");
        ValidateRange(MaxHistoryMessages is >= 0 and <= 50,
            nameof(MaxHistoryMessages), "between 0 and 50");
        ValidateRange(MaxHistoryCharacters is >= 1 and <= 1_000_000,
            nameof(MaxHistoryCharacters), "between 1 and 1,000,000");
        ValidateRange(MaxMessageCharacters is >= 1 and <= 100_000,
            nameof(MaxMessageCharacters), "between 1 and 100,000");
        ValidateRange(MaxToolResultCharacters is >= 256 and <= 1_000_000,
            nameof(MaxToolResultCharacters), "between 256 and 1,000,000");
        ValidateRange(MaxQueryCharacters is >= 1 and <= 100_000,
            nameof(MaxQueryCharacters), "between 1 and 100,000");
        ValidateRange(MaxQueryRows is >= 1 and <= 10_000,
            nameof(MaxQueryRows), "between 1 and 10,000");
        ValidateRange(QueryTimeoutSeconds is >= 1 and <= 300,
            nameof(QueryTimeoutSeconds), "between 1 and 300");
        ValidateRange(MaxToolIterations is >= 1 and <= 20,
            nameof(MaxToolIterations), "between 1 and 20");
        ValidateRange(MaxOutputTokens is >= 1 and <= 100_000,
            nameof(MaxOutputTokens), "between 1 and 100,000");

        if (_profiles.Count == 0)
        {
            throw new GridletValidationException(
                "At least one agent profile must be configured before AddAgentFramework can be used.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profiles = new List<GridletAgentProfileSettings>(_profiles.Count);
        foreach (var builder in _profiles)
        {
            var profile = builder.Build();
            if (!ids.Add(profile.Id))
            {
                throw new GridletValidationException(
                    $"Agent profile id '{profile.Id}' is configured more than once.");
            }
            profiles.Add(profile);
        }

        return new GridletAgentFrameworkSettings(
            CredentialLifetime,
            MaxHistoryMessages,
            MaxHistoryCharacters,
            MaxMessageCharacters,
            MaxToolResultCharacters,
            MaxQueryCharacters,
            MaxQueryRows,
            QueryTimeoutSeconds,
            MaxToolIterations,
            MaxOutputTokens,
            new ReadOnlyCollection<GridletAgentProfileSettings>(profiles));
    }

    private static void ValidateRange(bool valid, string name, string expected)
    {
        if (!valid)
        {
            throw new GridletValidationException($"{name} must be {expected}.");
        }
    }
}

/// <summary>Fluent, host-only configuration for one model-provider profile.</summary>
public sealed class GridletAgentProfileBuilder
{
    internal GridletAgentProfileBuilder(
        string id,
        string displayName,
        string model,
        GridletAgentProvider provider,
        Uri? endpoint)
    {
        Id = id;
        DisplayName = displayName;
        Model = model;
        Provider = provider;
        Endpoint = endpoint;
        IsLocal = provider == GridletAgentProvider.Ollama;
    }

    internal string Id { get; }
    internal string DisplayName { get; private set; }
    internal string Model { get; }
    internal GridletAgentProvider Provider { get; }
    internal Uri? Endpoint { get; }
    internal string? ServerApiKey { get; private set; }
    internal bool AllowsUserApiKey { get; private set; }
    internal bool IsLocal { get; private set; }

    /// <summary>Changes the safe display label exposed to Gridlet clients.</summary>
    public GridletAgentProfileBuilder WithDisplayName(string displayName)
    {
        DisplayName = displayName;
        return this;
    }

    /// <summary>
    /// Configures a server-owned API key. It remains server-side and is never included in profile
    /// metadata, audit entries, prompts, tool results, or response events.
    /// </summary>
    public GridletAgentProfileBuilder WithServerApiKey(string? apiKey)
    {
        ServerApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        return this;
    }

    /// <summary>Allows a user to supply an ephemeral API key for this profile.</summary>
    public GridletAgentProfileBuilder AllowUserApiKeys(bool allow = true)
    {
        AllowsUserApiKey = allow;
        return this;
    }

    /// <summary>
    /// Marks an OpenAI-compatible endpoint as local for safe profile metadata. Ollama profiles are
    /// local automatically.
    /// </summary>
    public GridletAgentProfileBuilder AsLocal(bool isLocal = true)
    {
        IsLocal = isLocal;
        return this;
    }

    internal GridletAgentProfileSettings Build()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 ||
            Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new GridletValidationException(
                "Agent profile ids must contain 1-100 ASCII letters, digits, '.', '-', or '_'.");
        }
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 200)
        {
            throw new GridletValidationException(
                $"Agent profile '{Id}' must have a display name of 1-200 characters.");
        }
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 300)
        {
            throw new GridletValidationException(
                $"Agent profile '{Id}' must have a model id of 1-300 characters.");
        }
        if (ServerApiKey?.Length > 8_192)
        {
            throw new GridletValidationException(
                $"The server API key configured for agent profile '{Id}' is too long.");
        }

        if (Provider is GridletAgentProvider.OpenAICompatible or GridletAgentProvider.Ollama)
        {
            if (Endpoint is null || !Endpoint.IsAbsoluteUri ||
                Endpoint.Scheme is not ("http" or "https") ||
                !string.IsNullOrEmpty(Endpoint.UserInfo))
            {
                throw new GridletValidationException(
                    $"Agent profile '{Id}' requires an absolute HTTP(S) endpoint without embedded credentials.");
            }
        }

        if (Provider is GridletAgentProvider.OpenAI or GridletAgentProvider.Anthropic &&
            ServerApiKey is null && !AllowsUserApiKey)
        {
            throw new GridletValidationException(
                $"Agent profile '{Id}' requires a server API key or AllowUserApiKeys().");
        }

        return new GridletAgentProfileSettings(
            Id,
            DisplayName,
            Model,
            Provider,
            Endpoint,
            ServerApiKey,
            AllowsUserApiKey,
            IsLocal);
    }
}

internal enum GridletAgentProvider
{
    OpenAI,
    Anthropic,
    OpenAICompatible,
    Ollama,
}

internal sealed record GridletAgentProfileSettings(
    string Id,
    string DisplayName,
    string Model,
    GridletAgentProvider Provider,
    Uri? Endpoint,
    string? ServerApiKey,
    bool AllowsUserApiKey,
    bool IsLocal)
{
    public bool RequiresUserApiKey =>
        ServerApiKey is null &&
        AllowsUserApiKey &&
        Provider is not GridletAgentProvider.Ollama;
}

internal sealed record GridletAgentFrameworkSettings(
    TimeSpan CredentialLifetime,
    int MaxHistoryMessages,
    int MaxHistoryCharacters,
    int MaxMessageCharacters,
    int MaxToolResultCharacters,
    int MaxQueryCharacters,
    int MaxQueryRows,
    int QueryTimeoutSeconds,
    int MaxToolIterations,
    int MaxOutputTokens,
    IReadOnlyList<GridletAgentProfileSettings> Profiles)
{
    private readonly IReadOnlyDictionary<string, GridletAgentProfileSettings> _profilesById =
        Profiles.ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

    public GridletAgentInfo Info { get; } = new(
        Profiles.Select(profile => new GridletAgentProfileInfo(
            profile.Id,
            profile.DisplayName,
            profile.Model,
            profile.IsLocal,
            profile.AllowsUserApiKey,
            profile.RequiresUserApiKey)).ToArray());

    public bool TryGetProfile(string id, out GridletAgentProfileSettings profile)
        => _profilesById.TryGetValue(id, out profile!);
}
