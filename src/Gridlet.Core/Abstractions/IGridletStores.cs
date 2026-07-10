using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>
/// Persistence for saved queries. The default implementation stores a JSON file under the
/// host's content root; replace the registration to persist elsewhere (e.g. a database).
/// </summary>
public interface ISavedQueryStore
{
    Task<IReadOnlyList<SavedQuery>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SavedQuery> SaveAsync(SavedQuery query, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for published API endpoints. Same replacement story as <see cref="ISavedQueryStore"/>.</summary>
public interface IPublishedEndpointStore
{
    Task<IReadOnlyList<PublishedEndpoint>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds an endpoint by HTTP method and route (both case-insensitive).</summary>
    Task<PublishedEndpoint?> FindAsync(string method, string route, CancellationToken cancellationToken = default);

    Task<PublishedEndpoint> SaveAsync(PublishedEndpoint endpoint, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
