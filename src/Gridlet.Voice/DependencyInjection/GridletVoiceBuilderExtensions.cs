using Gridlet;
using Gridlet.Abstractions;
using Gridlet.Voice;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace; conventional namespace for DI extensions.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration extensions for Gridlet's read-aloud support.</summary>
public static class GridletVoiceBuilderExtensions
{
    /// <summary>
    /// Adds a speaker button to agent responses, spoken by the browser's own speech synthesizer.
    /// </summary>
    public static GridletBuilder AddVoice(this GridletBuilder builder) =>
        builder.AddVoice(_ => { });

    /// <summary>
    /// Adds a speaker button to agent responses, spoken by the browser's own speech synthesizer
    /// with the supplied voice preferences.
    /// </summary>
    public static GridletBuilder AddVoice(
        this GridletBuilder builder,
        Action<GridletVoiceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GridletVoiceOptions();
        configure(options);
        // Settings are validated at startup rather than on the first spoken response, so a bad
        // rate or pitch fails the build instead of silently reaching a browser.
        var info = options.Build();

        builder.Services.TryAddSingleton<IGridletVoiceService>(new GridletBrowserVoiceService(info));
        return builder;
    }
}
