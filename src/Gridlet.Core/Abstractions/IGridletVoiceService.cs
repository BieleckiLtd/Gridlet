using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>
/// Optional read-aloud service. Gridlet's Voice package supplies the default implementation, which
/// speaks in the browser; hosts may replace it without taking a speech dependency in
/// <c>Gridlet.Core</c> or <c>Gridlet.AspNetCore</c>. When no implementation is registered the UI
/// shows no speaker button.
/// </summary>
public interface IGridletVoiceService
{
    /// <summary>Safe speech settings for the UI. Secrets and host addresses must never be included.</summary>
    GridletVoiceInfo Info { get; }
}
