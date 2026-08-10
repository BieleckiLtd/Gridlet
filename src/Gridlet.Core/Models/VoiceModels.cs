namespace Gridlet.Models;

/// <summary>
/// Non-secret speech settings published to the browser. Nothing here identifies a person or a
/// host, so it travels with the rest of the UI metadata.
/// </summary>
/// <param name="Engine">
/// Which synthesizer produces the audio. <c>browser</c> means the person's own operating system
/// speaks the text through the Web Speech API and no audio is generated on the server.
/// </param>
/// <param name="Rate">Speaking rate, where <c>1</c> is the voice's normal speed.</param>
/// <param name="Pitch">Speaking pitch, where <c>1</c> is the voice's normal pitch.</param>
/// <param name="Volume">Playback volume between <c>0</c> and <c>1</c>.</param>
/// <param name="Language">
/// BCP-47 tag used to pick a voice, or <see langword="null"/> to follow the browser's own default.
/// </param>
/// <param name="PreferredVoice">
/// Name of a voice to prefer when the browser offers it, matched case-insensitively. Installed
/// voices differ by machine, so this is a preference rather than a guarantee.
/// </param>
/// <param name="SpeakCode">
/// Whether fenced code blocks are read aloud. Off by default: spoken SQL punctuation is long and
/// hard to follow, and the code is already on screen.
/// </param>
/// <param name="AllowNetworkVoices">
/// Whether voices the browser synthesizes on a remote service may be used. Off by default, which
/// keeps every spoken response on the listener's device.
/// </param>
public sealed record GridletVoiceInfo(
    string Engine,
    double Rate = 1.0,
    double Pitch = 1.0,
    double Volume = 1.0,
    string? Language = null,
    string? PreferredVoice = null,
    bool SpeakCode = false,
    bool AllowNetworkVoices = false);

/// <summary>Well-known <see cref="GridletVoiceInfo.Engine"/> values.</summary>
public static class GridletVoiceEngines
{
    /// <summary>The browser's own Web Speech API, synthesized on the person's device.</summary>
    public const string Browser = "browser";
}
