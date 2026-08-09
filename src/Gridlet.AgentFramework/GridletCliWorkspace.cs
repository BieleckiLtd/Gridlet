namespace Gridlet.AgentFramework;

/// <summary>
/// The empty directory every subscription-backed CLI is launched in.
/// </summary>
/// <remarks>
/// These CLIs treat their working directory as a project to be worked on. Left in the host
/// application's directory they will, with no tool call and regardless of which tools Gridlet
/// grants them, read <c>AGENTS.md</c> and <c>CLAUDE.md</c> up the directory tree and obey them as
/// instructions, report the absolute path, git root, and a directory listing to the model, and load
/// the operating-system user's own agent memory for that project. None of that is database context
/// anybody shared in the Ask workspace, and repository instruction files are attacker-controlled
/// text in any repository that accepts contributions, so a private directory replaces it.
/// <para>
/// The location is stable rather than per-process. It sits under the current user's local
/// application data, which another local account cannot write to, so there is no need to randomize
/// the name to stop an <c>AGENTS.md</c> being planted there — and a stable path leaves nothing to
/// accumulate across restarts. It is swept on first use anyway, because a directory this code
/// requires to be empty should be checked rather than assumed.
/// </para>
/// </remarks>
internal static class GridletCliWorkspace
{
    /// <summary>
    /// Files the supported CLIs read as instructions purely because of where they sit. Anything
    /// found here was not put there by Gridlet, which never writes to this directory.
    /// </summary>
    private static readonly string[] InstructionFiles =
    [
        "AGENTS.md", "CLAUDE.md", "GEMINI.md", "copilot-instructions.md", ".cursorrules",
    ];

    private static readonly Lazy<string> Directory =
        new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Path => Directory.Value;

    private static string Create()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        // A service account can have no profile directory. Falling back to the temporary directory
        // keeps the agent working; the sweep below is what makes either location safe.
        if (string.IsNullOrWhiteSpace(root))
        {
            root = System.IO.Path.GetTempPath();
        }

        var path = System.IO.Path.Combine(root, "Gridlet", "agent-workspace");
        System.IO.Directory.CreateDirectory(path);
        Sweep(path);
        return path;
    }

    private static void Sweep(string path)
    {
        foreach (var name in InstructionFiles)
        {
            TryDelete(() => System.IO.File.Delete(System.IO.Path.Combine(path, name)));
        }

        // A planted repository would hand the CLI a git root and project name to report.
        TryDelete(() => System.IO.Directory.Delete(System.IO.Path.Combine(path, ".git"), true));
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be removed must not stop the agent from starting. The CLI flags
            // that disable project-document loading remain in force either way.
        }
    }
}
