using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Gridlet.AgentFramework;

/// <summary>
/// Every instruction Gridlet gives a model, loaded from the Markdown files under <c>Prompts/</c>.
/// </summary>
/// <remarks>
/// The wording an agent is given is the part of this package most likely to need changing, and the
/// part least likely to be changed by someone who wants to edit C#. Keeping it in files means a
/// reviewer sees a prose diff rather than a diff of string literals, and it removes the temptation
/// to word instructions around what fits comfortably in a source file. The files are embedded
/// resources, so a deployed application still carries exactly the text it was built with.
/// <para>
/// A missing file or section is a programming error rather than a runtime condition: it means code
/// asked for text that nobody wrote. Every lookup therefore throws instead of substituting a
/// default, and <c>GridletPromptTests</c> asks for all of them so the failure lands at build time.
/// </para>
/// </remarks>
internal static class GridletPrompts
{
    private const string ResourcePrefix = "Gridlet.AgentFramework.Prompts.";
    private const string GuideFolder = "Guide";

    private static readonly Assembly Assembly = typeof(GridletPrompts).Assembly;

    private static readonly ConcurrentDictionary<string, GridletPromptDocument> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every prompt file, keyed by its path below <c>Prompts/</c> without the extension.</summary>
    private static readonly IReadOnlyDictionary<string, string> ResourceNames = MapResourceNames();

    /// <summary>
    /// The guide topics, in file order. The numeric prefix on each file name orders this list and
    /// is not part of the topic name, so topics can be reordered without renaming the topic the
    /// agent asks for.
    /// </summary>
    public static IReadOnlyList<string> GuideTopics { get; } =
    [
        .. ResourceNames.Keys
            .Where(key => key.StartsWith(GuideFolder + "/", StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => TopicName(key[(GuideFolder.Length + 1)..])),
    ];

    /// <summary>Loads one prompt file.</summary>
    public static GridletPromptDocument Document(string path) =>
        Cache.GetOrAdd(path, static key => GridletPromptDocument.Parse(key, Read(key)));

    /// <summary>The main text of one prompt file, with <c>{token}</c> placeholders replaced.</summary>
    public static string Text(string path, params ReadOnlySpan<(string Token, string Value)> values) =>
        Substitute(Document(path).Text, values);

    /// <summary>One <c>## name</c> section of a prompt file, with placeholders replaced.</summary>
    public static string Section(
        string path,
        string section,
        params ReadOnlySpan<(string Token, string Value)> values) =>
        Substitute(Document(path).Section(section), values);

    /// <summary>The guide text for one topic, or <see langword="null"/> when no file defines it.</summary>
    public static string? Guide(string topic)
    {
        var normalized = topic.Trim();
        var match = ResourceNames.Keys.FirstOrDefault(key =>
            key.StartsWith(GuideFolder + "/", StringComparison.Ordinal) &&
            string.Equals(
                TopicName(key[(GuideFolder.Length + 1)..]),
                normalized,
                StringComparison.OrdinalIgnoreCase));
        return match is null ? null : Document(match).Text;
    }

    private static string TopicName(string fileName)
    {
        var separator = fileName.IndexOf('-');
        return separator > 0 && fileName[..separator].All(char.IsAsciiDigit)
            ? fileName[(separator + 1)..]
            : fileName;
    }

    private static string Substitute(
        string text,
        ReadOnlySpan<(string Token, string Value)> values)
    {
        if (values.Length == 0) return text;

        // Only the tokens a caller supplied are replaced. Prompt text legitimately contains other
        // braces — JSON examples, and the `{route}` placeholder people are meant to read as one —
        // and those have to survive untouched.
        var builder = new StringBuilder(text);
        foreach (var (token, value) in values)
        {
            builder.Replace("{" + token + "}", value);
        }

        return builder.ToString();
    }

    private static string Read(string path)
    {
        if (!ResourceNames.TryGetValue(path, out var resourceName))
        {
            throw new InvalidOperationException(
                $"There is no agent prompt file at 'Prompts/{path}.md'.");
        }

        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The agent prompt resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, string> MapResourceNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !name.EndsWith(".md", StringComparison.Ordinal))
            {
                continue;
            }

            // The build turns each directory separator into a '.', so the folder a file sits in is
            // recovered by putting them back. File names themselves never contain a '.'.
            var relative = name[ResourcePrefix.Length..^".md".Length].Replace('.', '/');
            map[relative] = name;
        }

        return map;
    }
}

/// <summary>One prompt file: its main text plus any <c>## name</c> sections.</summary>
internal sealed partial class GridletPromptDocument
{
    private readonly string path;
    private readonly IReadOnlyDictionary<string, string> sections;

    private GridletPromptDocument(
        string path,
        string text,
        IReadOnlyDictionary<string, string> sections)
    {
        this.path = path;
        this.sections = sections;
        Text = text;
    }

    /// <summary>The text before the first section heading.</summary>
    public string Text { get; }

    /// <summary>The section names, in file order.</summary>
    public IReadOnlyList<string> SectionNames => [.. sections.Keys];

    public string Section(string name) =>
        sections.TryGetValue(name, out var text)
            ? text
            : throw new InvalidOperationException(
                $"The agent prompt file 'Prompts/{path}.md' has no '## {name}' section.");

    internal static GridletPromptDocument Parse(string path, string content)
    {
        // Comments let a prompt file explain itself to whoever edits it next without spending the
        // model's context on notes addressed to a maintainer.
        var text = CommentPattern().Replace(content, string.Empty).Replace("\r\n", "\n");
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lead = new StringBuilder();
        var current = (Name: (string?)null, Body: new StringBuilder());

        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Close();
                current = (line[3..].Trim(), new StringBuilder());
                continue;
            }

            (current.Name is null ? lead : current.Body).Append(line).Append('\n');
        }

        Close();
        var leadText = lead.ToString().Trim();
        if (leadText.Length == 0 && sections.Count == 0)
        {
            throw new InvalidOperationException(
                $"The agent prompt file 'Prompts/{path}.md' is empty.");
        }

        return new GridletPromptDocument(path, leadText, sections);

        void Close()
        {
            if (current.Name is null) return;

            var body = current.Body.ToString().Trim();
            if (body.Length == 0)
            {
                throw new InvalidOperationException(
                    $"The '## {current.Name}' section of 'Prompts/{path}.md' is empty.");
            }
            if (!sections.TryAdd(current.Name, body))
            {
                throw new InvalidOperationException(
                    $"'Prompts/{path}.md' declares '## {current.Name}' more than once.");
            }
        }
    }

    [GeneratedRegex(@"<!--.*?-->\s*", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();
}
