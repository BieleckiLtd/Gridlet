using System.Text.RegularExpressions;

namespace Gridlet.Components;

/// <summary>
/// A component designed in the Gridlet components designer.
/// </summary>
/// <remarks>
/// The document is HTML, and it is the artifact: readable, diffable in review, and portable between
/// environments by copying the file. A label is a <c>&lt;span&gt;</c>, a text box is an
/// <c>&lt;input&gt;</c>, a drop-down is a <c>&lt;select&gt;</c> holding <c>&lt;option&gt;</c>s, and
/// everything HTML already has a word for is spelled that way. Only what HTML has no word for -
/// bindings, handlers, per-theme colours - is a <c>data-</c> attribute.
/// <para>
/// The server stores the document verbatim and never executes it. It reads exactly two things out
/// of it: the version it was written to, so a document from a newer build is refused rather than
/// silently downgraded, and the name it goes by. Everything else is the browser's to interpret,
/// because the browser is the only thing that renders a component.
/// </para>
/// <para>
/// A document is a description, never code. Handlers are <c>data-on-click</c> rather than
/// <c>onclick</c>, and nothing writes or reads a <c>&lt;script&gt;</c>: the JavaScript a component
/// runs lives in the modules it names, so saving a document and saving a module stay two different
/// privileges. See <see cref="GridletComponentScript"/>.
/// </para>
/// </remarks>
/// <param name="Html">The document, stored exactly as the designer wrote it.</param>
public sealed partial record GridletComponent(
    string Id,
    string Name,
    string Html,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>The document version this build writes and understands.</summary>
    public const int CurrentDocumentVersion = 2;

    /// <summary>
    /// Whether this component has a consumer-facing page. New components created through the
    /// management API are embedded-only unless the caller opts in; the <c>true</c> initializer is
    /// retained for components constructed by older custom stores that predate publication metadata.
    /// </summary>
    public bool Routable { get; init; } = true;

    /// <summary>
    /// Optional page route relative to the configured component-public path. When omitted, the
    /// component id is used, preserving the legacy URL.
    /// </summary>
    public string? Route { get; init; }

    /// <summary>Optional browser title for the consumer-facing page.</summary>
    public string? Title { get; init; }

    /// <summary>The route and title exposed to a consumer page after compatibility defaults.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveRoute => string.IsNullOrWhiteSpace(Route) ? Id : Route;

    /// <summary>The title exposed to a consumer page after compatibility defaults.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveTitle => string.IsNullOrWhiteSpace(Title) ? Name : Title;

    /// <summary>
    /// The version a document claims, or <c>null</c> when it does not claim one and so is not a
    /// component document at all.
    /// </summary>
    public static int? VersionOf(string? html)
    {
        var tag = DocumentTag().Match(html ?? string.Empty);
        return tag.Success && int.TryParse(tag.Groups["version"].Value, out var version)
            ? version
            : null;
    }

    /// <summary>
    /// The name a document goes by, read from the element that carries the version so a control's
    /// own <c>data-name</c> can never be mistaken for the component's.
    /// </summary>
    public static string? NameOf(string? html)
    {
        var tag = DocumentTag().Match(html ?? string.Empty);
        if (!tag.Success)
        {
            return null;
        }

        var name = DocumentName().Match(tag.Value);
        return name.Success ? System.Net.WebUtility.HtmlDecode(name.Groups["name"].Value) : null;
    }

    // The opening tag of the document element, and nothing inside it. Reading the version and the
    // name off the same match is what keeps a control's `data-name` from answering for the
    // component's: a control's attributes are never part of this tag.
    //
    // Any tag will do. What makes an element the document is the attribute it carries, not what it
    // is written as, so nothing here depends on a tag whose HTML meaning a component does not want.
    [GeneratedRegex(
        """<[A-Za-z][\w-]*\b[^>]*\bdata-gridlet\s*=\s*["']?(?<version>\d+)[^>]*>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentTag();

    [GeneratedRegex(
        @"\bdata-name\s*=\s*""(?<name>[^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentName();
}

/// <summary>Portable sidecar representation of component publication settings.</summary>
public sealed record GridletComponentPublication(
    bool Routable,
    string? Route,
    string? Title);

/// <summary>
/// Persistence for designed components. The default implementation stores one HTML file per
/// component in a folder; replace the registration to persist elsewhere.
/// </summary>
public interface IComponentStore
{
    Task<IReadOnlyList<GridletComponent>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<GridletComponent?> FindAsync(string id, CancellationToken cancellationToken = default);

    Task<GridletComponent> SaveAsync(GridletComponent component, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional publication metadata persistence for custom component stores. Keeping this separate
/// from <see cref="IComponentStore"/> means stores written against the original HTML-only contract
/// continue to load and save documents; the default file store implements this through its adjacent
/// sidecar file.
/// </summary>
public interface IComponentPublicationStore
{
    /// <summary>Saves metadata for an existing component.</summary>
    Task SavePublicationAsync(
        string componentId,
        bool routable,
        string? route,
        string? title,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes metadata for a component.</summary>
    Task<bool> DeletePublicationAsync(string componentId, CancellationToken cancellationToken = default);
}

/// <summary>Host configuration for the components designer.</summary>
public sealed class GridletComponentsOptions
{
    /// <summary>
    /// The folder holding a workspace's components: one <c>.html</c> file per component, and beside
    /// them the <c>.js</c> modules components attach, one file per module. Relative paths resolve
    /// against the host's content root.
    /// <para>
    /// Both are ordinary files rather than records inside a container, so an editor, a linter, a
    /// diff and a code review all see them as the source they are. A component and the modules it
    /// runs live together because they are one thing to copy, promote or review.
    /// </para>
    /// </summary>
    public string Path { get; set; } = "gridlet-components";

    /// <summary>
    /// Route prefix for consumer-facing component pages, relative to the Gridlet mount. The
    /// default keeps the legacy <c>{mount}/components/{id}</c> URL. Multiple safe segments are
    /// supported; an empty value maps pages directly beneath the mount.
    /// </summary>
    public string PublicRoutePrefix { get; set; } = "components";

    /// <summary>Alias for <see cref="PublicRoutePrefix"/> for hosts that configure paths by name.</summary>
    public string PublicPath
    {
        get => PublicRoutePrefix;
        set => PublicRoutePrefix = value;
    }

    /// <summary>Alias for <see cref="PublicRoutePrefix"/>.</summary>
    public string ComponentPublicPath
    {
        get => PublicRoutePrefix;
        set => PublicRoutePrefix = value;
    }
}
