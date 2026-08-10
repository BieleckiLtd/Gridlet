using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Voice;

/// <summary>
/// Speaks agent responses with the browser's own Web Speech API. The server produces no audio and
/// receives no request when a response is spoken: it only publishes the settings the browser reads
/// once, alongside the rest of the UI metadata.
/// </summary>
internal sealed class GridletBrowserVoiceService(GridletVoiceInfo info) : IGridletVoiceService
{
    public GridletVoiceInfo Info { get; } = info;
}
