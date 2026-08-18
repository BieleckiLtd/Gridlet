using System.Reflection;

namespace Gridlet.Components.Storage;

/// <summary>
/// The modules Gridlet ships with, read from this assembly's embedded resources.
/// </summary>
/// <remarks>
/// They are served through the same route as a component's own modules, so a module can import one by
/// name without knowing which of the two it is. They are read-only because they are part of the
/// build: an edit would be lost on the next upgrade, and a component that had come to depend on the edit
/// would break at exactly that moment.
/// </remarks>
internal static class GridletBuiltInScripts
{
    private const string ResourcePrefix = "Gridlet.Components.UI.wwwroot.";

    // Read once. They cannot change while the process is running: they are compiled into it.
    private static readonly Dictionary<string, GridletComponentScript> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly DateTimeOffset BuiltAt =
        File.GetLastWriteTimeUtc(typeof(GridletBuiltInScripts).Assembly.Location);

    public static GridletComponentScript? Find(string? name)
    {
        if (name is null || !GridletComponentScript.BuiltIn.TryGetValue(name, out var resource))
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            using var stream = typeof(GridletBuiltInScripts).Assembly
                .GetManifestResourceStream(ResourcePrefix + resource);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            var script = new GridletComponentScript(name, reader.ReadToEnd(), BuiltAt, ReadOnly: true);
            Cache[name] = script;
            return script;
        }
    }

    public static IEnumerable<GridletComponentScript> All()
        => GridletComponentScript.BuiltIn.Keys.Select(Find).OfType<GridletComponentScript>();
}
