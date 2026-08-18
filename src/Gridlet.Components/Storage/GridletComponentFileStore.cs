using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gridlet.Components.Storage;

/// <summary>
/// Default store for designed components: one JSON file under the host's content root. The file is the
/// artifact — it is meant to be readable and diffable, so it is written indented and the component
/// documents are stored as-is rather than re-encoded.
/// </summary>
internal sealed class GridletComponentFileStore : IComponentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    private List<GridletComponent>? _components;

    public GridletComponentFileStore(IOptions<GridletComponentsOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.FilePath;
        _path = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
    }

    public async Task<IReadOnlyList<GridletComponent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
            return _components!
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GridletComponent?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
            return _components!.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GridletComponent> SaveAsync(GridletComponent component, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
            _components!.RemoveAll(f => string.Equals(f.Id, component.Id, StringComparison.Ordinal));
            _components!.Add(component);
            await WriteAsync(cancellationToken);
            return component;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
            var removed = _components!.RemoveAll(f => string.Equals(f.Id, id, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                await WriteAsync(cancellationToken);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_components is not null)
        {
            return;
        }

        if (!File.Exists(_path))
        {
            _components = [];
            return;
        }

        await using var stream = File.OpenRead(_path);
        _components = await JsonSerializer.DeserializeAsync<List<GridletComponent>>(stream, JsonOptions, cancellationToken)
                 ?? [];
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write beside the target and move into place, so a crash mid-write cannot leave a
        // half-written file where every designed component used to be.
        var temporary = _path + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, _components, JsonOptions, cancellationToken);
        }

        File.Move(temporary, _path, overwrite: true);
    }
}
