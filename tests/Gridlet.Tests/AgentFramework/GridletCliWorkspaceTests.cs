using Gridlet.AgentFramework;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class GridletCliWorkspaceTests
{
    [Fact]
    public void The_cli_workspace_exists_and_carries_no_instruction_files()
    {
        var path = GridletCliWorkspace.Path;

        Assert.True(Directory.Exists(path));
        // The location is stable across restarts, so "nobody put an AGENTS.md here" has to be
        // enforced rather than assumed: a CLI launched here would obey one without any tool call.
        foreach (var name in new[]
                 {
                     "AGENTS.md", "CLAUDE.md", "GEMINI.md", "copilot-instructions.md", ".cursorrules",
                 })
        {
            Assert.False(
                File.Exists(Path.Combine(path, name)),
                $"{name} must not survive in the agent CLI workspace.");
        }

        Assert.False(
            Directory.Exists(Path.Combine(path, ".git")),
            "A repository in the workspace would give the CLI a git root to report.");
    }

    [Fact]
    public void The_cli_workspace_is_stable_across_calls()
    {
        // A path that changed per process would litter a new directory on every restart.
        Assert.Equal(GridletCliWorkspace.Path, GridletCliWorkspace.Path);
        Assert.DoesNotContain(
            Path.GetFileName(GridletCliWorkspace.Path),
            Guid.NewGuid().ToString("N"),
            StringComparison.Ordinal);
    }
}
