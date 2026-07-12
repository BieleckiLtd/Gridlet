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

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
