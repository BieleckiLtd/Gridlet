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

        // The prefix becomes a literal route segment, so anything that would turn it into a route
        // template, a traversal, or a collision with Gridlet's own API is rejected at startup
        // rather than producing endpoints nobody can reach.
        var publishedPrefix = options.PublishedApiRoutePrefix;
        if (string.IsNullOrWhiteSpace(publishedPrefix))
        {
            failures.Add("PublishedApiRoutePrefix must be a non-empty route segment, for example 'pub'.");
        }
        else
        {
            var prefix = publishedPrefix.Trim('/');
            if (prefix.Length is 0 or > 64)
            {
                failures.Add("PublishedApiRoutePrefix must contain 1-64 characters once surrounding slashes are removed.");
            }
            else if (prefix is "." or "..")
            {
                failures.Add("PublishedApiRoutePrefix cannot be '.' or '..' because route dot segments may be normalized as traversal.");
            }
            else if (!prefix.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            {
                failures.Add(
                    $"PublishedApiRoutePrefix '{publishedPrefix}' must be a single route segment of ASCII letters, digits, '.', '-', or '_'.");
            }
            else if (prefix.Equals("api", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("PublishedApiRoutePrefix cannot be 'api'; Gridlet's own management API is mounted there.");
            }
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

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
