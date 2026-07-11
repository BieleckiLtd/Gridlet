namespace Gridlet.Models;

/// <summary>A query saved by a user for reuse.</summary>
public sealed record SavedQuery(
    string Id,
    string Name,
    string ConnectionName,
    string? Database,
    string Sql,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// A value parameter of a published API endpoint, bound to a SQL parameter of the same name.
/// Type is one of auto, string, integer, number, or boolean. Auto preserves the value supplied
/// by a JSON client and treats query-string values as strings.
/// </summary>
public sealed record PublishedParameter(string Name, bool Required, string Type = "auto");

/// <summary>
/// A data operation published as an HTTP endpoint under the Gridlet mount path
/// (<c>{mount}/pub/{route}</c>). Endpoints inherit Gridlet's authorization and can
/// additionally require their own policy.
/// </summary>
public sealed record PublishedEndpoint(
    string Id,
    string Name,
    string Method,
    string Route,
    string ConnectionName,
    string? Database,
    string Sql,
    IReadOnlyList<PublishedParameter> Parameters,
    string? AuthorizationPolicy,
    bool Enabled,
    DateTimeOffset UpdatedAtUtc);
