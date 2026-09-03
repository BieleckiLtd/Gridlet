using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gridlet.Components.Storage;

/// <summary>
/// Default store for component modules: one <c>.js</c> file per module in a folder. The files are the
/// artifact - they are meant to be opened in an editor, linted, diffed and reviewed like any other
/// source in the project, so nothing is wrapped, encoded or minified on the way in or out.
/// </summary>
internal sealed class GridletComponentScriptFileStore : IComponentScriptStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;

    public GridletComponentScriptFileStore(IOptions<GridletComponentsOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.Path;
        _directory = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
    }

    public async Task<IReadOnlyList<GridletComponentScript>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_directory))
            {
                return [];
            }

            var scripts = new List<GridletComponentScript>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.js"))
            {
                var name = Path.GetFileName(path);

                // A file someone dropped in with a name a module cannot have is left alone rather
                // than offered as something the designer can open and then fail to save.
                if (!GridletComponentScript.IsValidName(name))
                {
                    continue;
                }

                scripts.Add(new GridletComponentScript(
                    name,
                    await File.ReadAllTextAsync(path, cancellationToken),
                    File.GetLastWriteTimeUtc(path)));
            }

            return scripts.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GridletComponentScript?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!GridletComponentScript.IsValidName(name))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = PathFor(name);
            return File.Exists(path)
                ? new GridletComponentScript(
                    name,
                    await File.ReadAllTextAsync(path, cancellationToken),
                    File.GetLastWriteTimeUtc(path))
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GridletComponentScript> SaveAsync(
        string name, string source, CancellationToken cancellationToken = default)
    {
        if (!GridletComponentScript.IsValidName(name))
        {
            throw new ArgumentException($"'{name}' is not a valid module name.", nameof(name));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(name);

            // Written beside the target and moved into place, so a crash mid-write cannot leave a
            // half-written module where working behaviour used to be.
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, source, cancellationToken);
            File.Move(temporary, path, overwrite: true);
            return new GridletComponentScript(name, source, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!GridletComponentScript.IsValidName(name))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = PathFor(name);
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

    // Names are validated before they get here, so this only ever joins one plain file name onto
    // the configured folder. The check is what makes traversal impossible, not this method.
    private string PathFor(string name) => Path.Combine(_directory, name);
}
