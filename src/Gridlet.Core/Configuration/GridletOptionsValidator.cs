using Microsoft.Extensions.Options;

namespace Gridlet;

/// <summary>Validates <see cref="GridletOptions"/> on first resolution.</summary>
public sealed class GridletOptionsValidator : IValidateOptions<GridletOptions>
{
    public ValidateOptionsResult Validate(string? name, GridletOptions options)
    {
        var failures = new List<string>();

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in options.Connections)
        {
            if (string.IsNullOrWhiteSpace(connection.Name))
            {
                failures.Add("Every Gridlet connection must have a non-empty Name.");
                continue;
            }

            if (!seenNames.Add(connection.Name))
            {
                failures.Add($"Duplicate Gridlet connection name '{connection.Name}'. Connection names must be unique (case-insensitive).");
            }

            if (string.IsNullOrWhiteSpace(connection.ConnectionString))
            {
                failures.Add($"Gridlet connection '{connection.Name}' has an empty ConnectionString.");
            }

            if (connection.ProviderName == GridletProviderNames.Unspecified ||
                !Enum.IsDefined(connection.ProviderName))
            {
                failures.Add(
                    $"Gridlet connection '{connection.Name}' has an unsupported ProviderName '{connection.ProviderName}'.");
            }

            if (connection.ProviderName == GridletProviderNames.Sqlite)
            {
                foreach (var attachment in connection.SqliteAttachments)
                {
                    if (string.IsNullOrWhiteSpace(attachment.Key) ||
                        attachment.Key.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                        attachment.Key.Equals("temp", StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add(
                            $"Gridlet connection '{connection.Name}' has invalid SQLite attachment name '{attachment.Key}'. Names must be non-empty and cannot be 'main' or 'temp'.");
                    }
                    if (string.IsNullOrWhiteSpace(attachment.Value))
                    {
                        failures.Add(
                            $"SQLite attachment '{attachment.Key}' on connection '{connection.Name}' has an empty filename.");
                    }
                }
            }

            if (connection.AllowAgentDataAccess &&
                string.IsNullOrWhiteSpace(connection.AgentDataConnectionString) &&
                !connection.AllowAgentDataWithPrimaryConnection)
            {
                failures.Add(
                    $"Gridlet connection '{connection.Name}' enables agent data access but has no AgentDataConnectionString. Configure a read-only identity or explicitly set AllowAgentDataWithPrimaryConnection.");
            }
        }

        // The prefix becomes a literal route path, so anything that would turn it into a route
        // template, a traversal, or a collision with Gridlet's own API is rejected at startup
        // rather than producing endpoints nobody can reach.
        var publishedPrefix = options.PublishedApiRoutePrefix;
        if (string.IsNullOrWhiteSpace(publishedPrefix))
        {
            failures.Add("PublishedApiRoutePrefix must be a non-empty route path, for example 'pub'.");
        }
        else if (!GridletRoutePath.TryNormalize(publishedPrefix, out var normalizedPublishedPrefix))
        {
            failures.Add(
                $"PublishedApiRoutePrefix '{publishedPrefix}' must contain safe, non-empty route segments " +
                "using ASCII letters, digits, '.', '-', '_' and '/'.");
        }
        else if (GridletRoutePath.FirstSegment(normalizedPublishedPrefix)
                     .Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                "PublishedApiRoutePrefix cannot be 'api' or be beneath it; Gridlet's own management API is mounted there.");
        }
        else if (GridletRoutePath.FirstSegment(normalizedPublishedPrefix)
                     .Equals("assets", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                "PublishedApiRoutePrefix cannot be 'assets' or be beneath it; Gridlet's runtime assets are mounted there.");
        }

        if (options.PublishedApiPath is not null)
        {
            if (!GridletRoutePath.TryNormalize(options.PublishedApiPath, out _))
            {
                failures.Add(
                    $"PublishedApiPath '{options.PublishedApiPath}' must contain safe, non-empty route segments " +
                    "using ASCII letters, digits, '.', '-', '_' and '/'.");
            }
            // The absolute path may be under /gridlet when a host intentionally maps the whole
            // application there; the mapping-time check has the real mount and resolves that case.
        }

        var limits = options.Limits;
        if (limits.DefaultPageSize < 1)
        {
            failures.Add("Limits.DefaultPageSize must be at least 1.");
        }

        if (limits.MaxPageSize < limits.DefaultPageSize)
        {
            failures.Add("Limits.MaxPageSize must be greater than or equal to Limits.DefaultPageSize.");
        }

        if (limits.MaxQueryResultRows < 1)
        {
            failures.Add("Limits.MaxQueryResultRows must be at least 1.");
        }

        if (limits.CommandTimeoutSeconds < 1)
        {
            failures.Add("Limits.CommandTimeoutSeconds must be at least 1.");
        }

        if (limits.MaxQuerySessions < 1)
        {
            failures.Add("Limits.MaxQuerySessions must be at least 1.");
        }

        if (limits.QuerySessionIdleTimeoutMinutes < 1)
        {
            failures.Add("Limits.QuerySessionIdleTimeoutMinutes must be at least 1.");
        }

        if (limits.MaxQueryJobs < 1)
        {
            failures.Add("Limits.MaxQueryJobs must be at least 1.");
        }

        if (limits.MaxQueryJobsPerOwner < 1)
        {
            failures.Add("Limits.MaxQueryJobsPerOwner must be at least 1.");
        }

        if (limits.MaxQueryJobEvents < 16)
        {
            failures.Add("Limits.MaxQueryJobEvents must be at least 16.");
        }

        if (limits.MaxQueryJobRetainedBytes < 64 * 1024)
        {
            failures.Add("Limits.MaxQueryJobRetainedBytes must be at least 65536.");
        }

        if (limits.QueryJobRetentionMinutes < 1)
        {
            failures.Add("Limits.QueryJobRetentionMinutes must be at least 1.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
