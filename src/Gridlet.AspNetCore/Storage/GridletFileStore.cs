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
    : ISavedQueryStore, IPublishedEndpointStore, IForeignKeyDisplayStore
{
    private const int SwapAttempts = 6;
    private const int SwapRetryDelayMs = 15;

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

        public List<ForeignKeyDisplaySetting> ForeignKeyDisplays { get; set; } = [];
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

    // ---- foreign-key displays ----

    async Task<IReadOnlyList<ForeignKeyDisplaySetting>> IForeignKeyDisplayStore.GetForObjectAsync(
        string connectionName, string? database, string sourceSchema, string sourceTable,
        CancellationToken cancellationToken)
        => await ReadAsync(d => d.ForeignKeyDisplays.Where(setting =>
                Same(setting.ConnectionName, connectionName) &&
                Same(setting.Database, database) &&
                Same(setting.SourceSchema, sourceSchema) &&
                Same(setting.SourceTable, sourceTable))
            .OrderBy(setting => setting.ForeignKeyName, StringComparer.OrdinalIgnoreCase)
            .ToArray(), cancellationToken);

    async Task<ForeignKeyDisplaySetting> IForeignKeyDisplayStore.SaveAsync(
        ForeignKeyDisplaySetting setting, CancellationToken cancellationToken)
    {
        await MutateAsync(d =>
        {
            d.ForeignKeyDisplays.RemoveAll(candidate => SameDisplay(candidate, setting));
            d.ForeignKeyDisplays.Add(setting);
        }, cancellationToken);
        return setting;
    }

    async Task<bool> IForeignKeyDisplayStore.DeleteAsync(
        string connectionName, string? database, string sourceSchema, string sourceTable,
        string foreignKeyName, CancellationToken cancellationToken)
    {
        var removed = false;
        await MutateAsync(d => removed = d.ForeignKeyDisplays.RemoveAll(setting =>
            Same(setting.ConnectionName, connectionName) &&
            Same(setting.Database, database) &&
            Same(setting.SourceSchema, sourceSchema) &&
            Same(setting.SourceTable, sourceTable) &&
            Same(setting.ForeignKeyName, foreignKeyName)) > 0, cancellationToken);
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
            var current = await LoadAsync(cancellationToken);
            var candidate = new StoreData
            {
                SavedQueries = [.. current.SavedQueries],
                PublishedEndpoints = [.. current.PublishedEndpoints],
                ForeignKeyDisplays = [.. current.ForeignKeyDisplays],
            };
            mutate(candidate);
            await PersistAsync(candidate, cancellationToken);
            _data = candidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistAsync(StoreData data, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(_path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new GridletValidationException($"Storage path '{_path}' has no parent directory.");
        Directory.CreateDirectory(directory);

        // Write beside the destination so the final rename stays on one volume. The cached state
        // is replaced only after this succeeds, so failed/cancelled writes remain invisible.
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var unixMode = !OperatingSystem.IsWindows() && File.Exists(fullPath)
            ? File.GetUnixFileMode(fullPath)
            : (UnixFileMode?)null;
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (!OperatingSystem.IsWindows() && unixMode is { } preservedUnixMode)
            {
                // rename/replace keeps the temporary inode on Unix, so copy the destination's
                // permission bits before replacing it rather than falling back to the process umask.
                File.SetUnixFileMode(temporaryPath, preservedUnixMode);
            }

            SwapIntoPlace(temporaryPath, fullPath);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup after a failed write/rename.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup after a failed write/rename.
            }
        }
    }

    /// <summary>
    /// Moves <paramref name="temporaryPath"/> onto <paramref name="fullPath"/>, retrying briefly.
    /// On Windows another process (virus scanner, search indexer) routinely holds a freshly created
    /// file open for a fraction of a second, which makes the swap fail; the destination can also
    /// appear between attempts, so its existence is re-checked every time rather than assumed.
    /// </summary>
    private static void SwapIntoPlace(string temporaryPath, string fullPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    // On Windows File.Replace uses the platform replacement API, which retains the
                    // destination file's ACL and metadata. On Unix the mode was copied by the caller.
                    File.Replace(temporaryPath, fullPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }

                return;
            }
            catch (Exception ex) when (attempt < SwapAttempts && ex is IOException or UnauthorizedAccessException)
            {
                // Transient sharing violation, or the destination appeared after the check above.
                Thread.Sleep(SwapRetryDelayMs * attempt);
            }
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

    private static bool Same(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SameDisplay(ForeignKeyDisplaySetting left, ForeignKeyDisplaySetting right)
        => Same(left.ConnectionName, right.ConnectionName) &&
           Same(left.Database, right.Database) &&
           Same(left.SourceSchema, right.SourceSchema) &&
           Same(left.SourceTable, right.SourceTable) &&
           Same(left.ForeignKeyName, right.ForeignKeyName);
}
