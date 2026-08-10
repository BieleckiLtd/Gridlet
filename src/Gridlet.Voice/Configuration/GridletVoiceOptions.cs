using Gridlet.Models;

namespace Gridlet.Voice;

/// <summary>
/// Host-controlled configuration for Gridlet's read-aloud support.
/// </summary>
/// <remarks>
/// Every value here is a hint to the browser's own speech synthesizer. Which voices exist is a
/// property of the person's operating system, so Gridlet expresses preferences and lets the
/// browser fall back to its default when a preference cannot be honoured.
/// </remarks>
public sealed class GridletVoiceOptions
{
    /// <summary>
    /// Speaking rate, where <c>1</c> is the voice's normal speed. Defaults to <c>1</c> and must be
    /// between <c>0.1</c> and <c>10</c>, the range the Web Speech API accepts.
    /// </summary>
    public double Rate { get; set; } = 1.0;

    /// <summary>
    /// Speaking pitch, where <c>1</c> is the voice's normal pitch. Defaults to <c>1</c> and must be
    /// between <c>0</c> and <c>2</c>.
    /// </summary>
    public double Pitch { get; set; } = 1.0;

    /// <summary>
    /// Playback volume. Defaults to <c>1</c> and must be between <c>0</c> and <c>1</c>.
    /// </summary>
    public double Volume { get; set; } = 1.0;

    /// <summary>
    /// BCP-47 language tag used to choose a voice, such as <c>en-GB</c>. Defaults to
    /// <see langword="null"/>, which follows the browser's own default voice.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Name of a voice to prefer when the browser reports one that matches, such as
    /// <c>Microsoft Sonia Online (Natural) - English (United Kingdom)</c>. Installed voices differ
    /// by machine, so an unmatched name is ignored rather than treated as an error.
    /// </summary>
    public string? PreferredVoice { get; set; }

    /// <summary>
    /// Whether fenced code blocks are read aloud. Defaults to <see langword="false"/>: spoken SQL
    /// punctuation is long and hard to follow, and the code is already on screen.
    /// </summary>
    public bool SpeakCode { get; set; }

    /// <summary>
    /// Whether voices the browser synthesizes remotely may be used. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The natural-sounding voices most browsers offer are cloud services: choosing one sends the
    /// text of every spoken response to the browser vendor to be turned into audio. For an agent
    /// that discusses schema and query results, that is a disclosure decision, so it belongs to the
    /// host rather than to whichever voice happens to sound best. While this is off, Gridlet speaks
    /// only through voices the browser reports as running on the listener's own device, and
    /// <see cref="PreferredVoice"/> will not override that.
    /// </remarks>
    public bool AllowNetworkVoices { get; set; }

    internal GridletVoiceInfo Build()
    {
        ValidateRange(Rate is >= 0.1 and <= 10, nameof(Rate), "between 0.1 and 10");
        ValidateRange(Pitch is >= 0 and <= 2, nameof(Pitch), "between 0 and 2");
        ValidateRange(Volume is >= 0 and <= 1, nameof(Volume), "between 0 and 1");
        ValidateRange(Language is null || Language.Trim().Length is > 0 and <= 35,
            nameof(Language), "null or a language tag of 1-35 characters");
        ValidateRange(PreferredVoice is null || PreferredVoice.Trim().Length is > 0 and <= 200,
            nameof(PreferredVoice), "null or a voice name of 1-200 characters");

        return new GridletVoiceInfo(
            GridletVoiceEngines.Browser,
            Rate,
            Pitch,
            Volume,
            Language?.Trim(),
            PreferredVoice?.Trim(),
            SpeakCode,
            AllowNetworkVoices);
    }

    private static void ValidateRange(bool valid, string name, string expected)
    {
        if (!valid)
        {
            throw new GridletValidationException($"{name} must be {expected}.");
        }
    }
}
