using System.Text.Json;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gridlet.AspNetCore.Storage;

/// <summary>
/// Default store for saved queries and published endpoints: one JSON file under the host's
/// content root. Fine for the small volumes involved; replace the interface registrations
/// to persist elsewhere.
/// </summary>
internal sealed class GridletFileStore(IOptions<GridletOptions> options, IHostEnvironment environment)
    : ISavedQueryStore, IPublishedEndpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.IsPathRooted(options.Value.Storage.FilePath)
        ? options.Value.Storage.FilePath
        : Path.Combine(environment.ContentRootPath, options.Value.Storage.FilePath);

    private StoreData? _data;

    private sealed class StoreData
    {
        public List<SavedQuery> SavedQueries { get; set; } = [];

        public List<PublishedEndpoint> PublishedEndpoints { get; set; } = [];
    }

    // ---- saved queries ----

    async Task<IReadOnlyList<SavedQuery>> ISavedQueryStore.GetAllAsync(CancellationToken cancellationToken)
        => await ReadAsync(d => d.SavedQueries
            .OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray(), cancellationToken);

    public async Task<SavedQuery> SaveAsync(SavedQuery query, CancellationToken cancellationToken = default)
    {
        await MutateAsync(d =>
        {
            d.SavedQueries.RemoveAll(q => q.Id == query.Id);
            d.SavedQueries.Add(query);
        }, cancellationToken);
        return query;
    }

    async Task<bool> ISavedQueryStore.DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var removed = false;
        await MutateAsync(d => removed = d.SavedQueries.RemoveAll(q => q.Id == id) > 0, cancellationToken);
        return removed;
    }

    // ---- published endpoints ----

    async Task<IReadOnlyList<PublishedEndpoint>> IPublishedEndpointStore.GetAllAsync(CancellationToken cancellationToken)
        => await ReadAsync(d => d.PublishedEndpoints
            .OrderBy(e => e.Route, StringComparer.OrdinalIgnoreCase)
            .ToArray(), cancellationToken);

    public async Task<PublishedEndpoint?> FindAsync(string method, string route, CancellationToken cancellationToken = default)
        => await ReadAsync(d => d.PublishedEndpoints.FirstOrDefault(e =>
            string.Equals(e.Method, method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Route, route, StringComparison.OrdinalIgnoreCase)), cancellationToken);

    public async Task<PublishedEndpoint> SaveAsync(PublishedEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        await MutateAsync(d =>
        {
            var clash = d.PublishedEndpoints.FirstOrDefault(e =>
                e.Id != endpoint.Id &&
                string.Equals(e.Method, endpoint.Method, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Route, endpoint.Route, StringComparison.OrdinalIgnoreCase));
            if (clash is not null)
            {
                throw new GridletValidationException(
                    $"A published endpoint already uses {endpoint.Method} {endpoint.Route} ('{clash.Name}').");
            }

            d.PublishedEndpoints.RemoveAll(e => e.Id == endpoint.Id);
            d.PublishedEndpoints.Add(endpoint);
        }, cancellationToken);
        return endpoint;
    }

    async Task<bool> IPublishedEndpointStore.DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var removed = false;
        await MutateAsync(d => removed = d.PublishedEndpoints.RemoveAll(e => e.Id == id) > 0, cancellationToken);
        return removed;
    }

    // ---- plumbing ----

    private async Task<T> ReadAsync<T>(Func<StoreData, T> selector, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return selector(await LoadAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MutateAsync(Action<StoreData> mutate, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadAsync(cancellationToken);
            mutate(data);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(data, JsonOptions), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StoreData> LoadAsync(CancellationToken cancellationToken)
    {
        if (_data is not null)
        {
            return _data;
        }

        if (File.Exists(_path))
        {
            await using var stream = File.OpenRead(_path);
            _data = await JsonSerializer.DeserializeAsync<StoreData>(stream, cancellationToken: cancellationToken);
        }

        return _data ??= new StoreData();
    }
}
