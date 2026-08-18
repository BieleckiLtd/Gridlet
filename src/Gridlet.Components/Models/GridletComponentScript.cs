using System.Text.RegularExpressions;

namespace Gridlet.Components;

/// <summary>
/// A JavaScript module a component can attach for behaviour.
/// </summary>
/// <remarks>
/// The module is ordinary JavaScript, stored as a file and served to the browser as a real ES
/// module, so it is written and read with the conventions any JavaScript developer already has:
/// <c>import</c>, <c>export</c>, classes, private fields. Nothing about it is Gridlet-specific
/// except the object handed to it at runtime.
/// <para>
/// The component document stays declarative: it names the modules it uses and holds no code and no
/// event wiring of its own. What a module does when it runs is decided in the module.
/// </para>
/// <para>
/// A module runs in the browser of whoever previews or runs the component, with the same trust as the
/// rest of the workspace. It is code, not data: treat writing one as the privileged action it is,
/// and review modules the way the rest of an application's source is reviewed.
/// </para>
/// </remarks>
/// <param name="Name">File name of the module, including the <c>.js</c> extension.</param>
/// <param name="Source">The module's JavaScript source, stored verbatim.</param>
/// <param name="ReadOnly">
/// True for the modules Gridlet ships. They can be opened, read and imported like any other, and
/// cannot be edited or deleted.
/// </param>
public sealed partial record GridletComponentScript(
    string Name,
    string Source,
    DateTimeOffset UpdatedAtUtc,
    bool ReadOnly = false)
{
    /// <summary>
    /// The modules Gridlet ships, by the name a component imports them under. They live in the same flat
    /// namespace as everything else, so <c>import { json } from './gridlet.js'</c> resolves the way
    /// an import between two of your own modules does — and a module of yours cannot take one of
    /// these names, or an import would silently mean something else.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuiltIn { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["gridlet.js"] = "standard.js",
        };

    public static bool IsBuiltIn(string? name) => name is not null && BuiltIn.ContainsKey(name);

    /// <summary>
    /// Module names are a single file name with a <c>.js</c> extension, and nothing else: no
    /// directories, no traversal, no characters that mean something to a URL. Modules live in one
    /// flat folder so a relative <c>import './shared.js'</c> resolves the obvious way, in the
    /// editor and in the browser alike.
    /// </summary>
    public static bool IsValidName(string? name)
        => name is not null && ValidName().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\.js$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();
}

/// <summary>
/// Persistence for the JavaScript modules components attach. The default implementation keeps them as
/// files in a folder; replace the registration to store them elsewhere.
/// </summary>
public interface IComponentScriptStore
{
    Task<IReadOnlyList<GridletComponentScript>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<GridletComponentScript?> FindAsync(string name, CancellationToken cancellationToken = default);

    Task<GridletComponentScript> SaveAsync(string name, string source, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);
}
