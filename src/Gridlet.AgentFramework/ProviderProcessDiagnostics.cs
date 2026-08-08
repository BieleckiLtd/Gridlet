using System.Text;

namespace Gridlet.AgentFramework;

/// <summary>
/// Continuously drains a provider process stream while retaining only a bounded diagnostic tail.
/// </summary>
internal sealed class BoundedTextTail
{
    internal const int DefaultMaximumCharacters = 32_768;

    private readonly object sync = new();
    private readonly StringBuilder tail = new();
    private readonly int maximumCharacters;

    private BoundedTextTail(TextReader reader, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        this.maximumCharacters = maximumCharacters;
        Completion = DrainAsync(reader);
    }

    public Task Completion { get; }

    public static BoundedTextTail Capture(
        TextReader reader,
        int maximumCharacters = DefaultMaximumCharacters) =>
        new(reader, maximumCharacters);

    public string GetTail()
    {
        lock (sync)
        {
            return tail.ToString();
        }
    }

    private async Task DrainAsync(TextReader reader)
    {
        var buffer = new char[4_096];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory());
                if (read == 0) break;
                lock (sync)
                {
                    tail.Append(buffer, 0, read);
                    var excess = tail.Length - maximumCharacters;
                    if (excess > 0)
                    {
                        tail.Remove(0, excess);
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Process shutdown can close a redirected stream while its drain is still completing.
        }
    }
}

/// <summary>
/// Separates a safe provider failure from bounded host-only diagnostics. ASP.NET Core returns its
/// generic provider error for this non-Gridlet exception, while normal exception logging retains
/// the inner diagnostic detail.
/// </summary>
internal sealed class AgentProviderRuntimeException : Exception
{
    public AgentProviderRuntimeException(
        string safeMessage,
        string diagnosticDetail,
        Exception? cause = null)
        : base(safeMessage, new InvalidOperationException(diagnosticDetail, cause))
    {
    }
}
