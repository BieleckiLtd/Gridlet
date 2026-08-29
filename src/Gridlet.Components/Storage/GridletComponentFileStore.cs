using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gridlet.Components.Storage;

/// <summary>
/// Default store for designed components: one <c>.html</c> file per component in a folder, beside
/// the modules those components run.
/// </summary>
/// <remarks>
/// The file is the artifact. It is meant to be opened in an editor, diffed and reviewed like any
/// other source in the project, so the document is written exactly as the designer produced it —
/// nothing is wrapped, escaped or re-encoded on the way in or out. That is only true because the
/// document is HTML: held inside a container file it would be an escaped string, and a diff would
/// show one very long line changing.
/// <para>
/// A component's name and the version it was written to live in the document, so the file needs no
/// envelope to describe it and cannot disagree with itself. The id is the file name and the
/// modified time is the file's own.
/// </para>
/// </remarks>
internal sealed partial class GridletComponentFileStore : IComponentStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;

    public GridletComponentFileStore(IOptions<GridletComponentsOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.Path;
        _directory = System.IO.Path.IsPathRooted(configured)
            ? configured
            : System.IO.Path.Combine(environment.ContentRootPath, configured);
    }

    public async Task<IReadOnlyList<GridletComponent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_directory))
            {
                return [];
            }

            var components = new List<GridletComponent>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.html"))
            {
                var id = System.IO.Path.GetFileNameWithoutExtension(path);

                // A file someone dropped in with a name an id cannot have is left alone rather than
                // offered as something the designer can open and then fail to save back.
                if (!IsValidId(id))
                {
                    continue;
                }

                var component = await ReadAsync(path, id, cancellationToken);
                if (component is not null)
                {
                    components.Add(component);
                }
            }

            return components
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GridletComponent?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(id))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = PathFor(id);
            return File.Exists(path) ? await ReadAsync(path, id, cancellationToken) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GridletComponent> SaveAsync(
        GridletComponent component, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(component.Id))
        {
            throw new ArgumentException($"'{component.Id}' is not a usable component id.", nameof(component));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(component.Id);

            // Written beside the target and moved into place, so a crash mid-write cannot leave a
            // half-written document where a working component used to be.
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, component.Html, cancellationToken);
            File.Move(temporary, path, overwrite: true);

            return component with { UpdatedAtUtc = File.GetLastWriteTimeUtc(path) };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(id))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = PathFor(id);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GridletComponent?> ReadAsync(string path, string id, CancellationToken cancellationToken)
    {
        var html = await File.ReadAllTextAsync(path, cancellationToken);

        // A file that does not say it is a component document is not one. Listing it would offer
        // the designer something it cannot open, and saving over it would destroy whatever it is.
        if (GridletComponent.VersionOf(html) is null)
        {
            return null;
        }

        return new GridletComponent(
            id,
            GridletComponent.NameOf(html) is { Length: > 0 } name ? name : id,
            html,
            File.GetLastWriteTimeUtc(path));
    }

    private string PathFor(string id) => System.IO.Path.Combine(_directory, id + ".html");

    // The id is a file name, so it is held to what a file name may safely be: no directories, no
    // traversal, nothing that means something to a path or a URL.
    private static bool IsValidId(string? id) => id is not null && ValidId().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidId();
}
