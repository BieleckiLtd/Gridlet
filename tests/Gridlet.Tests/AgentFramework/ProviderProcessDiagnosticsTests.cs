using Gridlet.AgentFramework;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class ProviderProcessDiagnosticsTests
{
    [Fact]
    public async Task Stderr_capture_retains_only_the_configured_tail()
    {
        var source = string.Concat(new string('x', 100_000), "diagnostic-tail");
        var capture = BoundedTextTail.Capture(new StringReader(source), maximumCharacters: 64);

        await capture.Completion;
        var tail = capture.GetTail();

        Assert.Equal(64, tail.Length);
        Assert.EndsWith("diagnostic-tail", tail, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_runtime_exception_keeps_sensitive_diagnostics_out_of_safe_message()
    {
        var exception = new AgentProviderRuntimeException(
            "Provider runtime failed.",
            "C:\\sensitive\\codex.exe TOKEN=secret");

        Assert.Equal("Provider runtime failed.", exception.Message);
        Assert.DoesNotContain("sensitive", exception.Message, StringComparison.Ordinal);
        Assert.Contains("TOKEN=secret", exception.InnerException!.Message, StringComparison.Ordinal);
    }
}
