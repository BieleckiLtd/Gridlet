using Gridlet;
using Microsoft.Extensions.Options;

namespace Gridlet.Components;

/// <summary>Validates the route prefix used by consumer-facing components.</summary>
public sealed class GridletComponentsOptionsValidator : IValidateOptions<GridletComponentsOptions>
{
    public ValidateOptionsResult Validate(string? name, GridletComponentsOptions options)
    {
        var failures = new List<string>();
        var value = options.PublicRoutePrefix;

        if (value is null)
        {
            failures.Add("GridletComponents.PublicRoutePrefix cannot be null.");
        }
        else if (value.Length == 0)
        {
            // Empty is intentional: it puts component pages directly beneath the Gridlet mount.
        }
        else if (value.Trim().Length == 0)
        {
            failures.Add("GridletComponents.PublicRoutePrefix cannot be whitespace.");
        }
        else if (!GridletRoutePath.TryNormalize(value, out var normalized, allowEmpty: true))
        {
            failures.Add(
                "GridletComponents.PublicRoutePrefix must contain safe, non-empty route segments " +
                "using ASCII letters, digits, '.', '-', '_' and '/'.");
        }
        else if (GridletRoutePath.FirstSegment(normalized) is var firstSegment &&
                 (firstSegment.Equals("api", StringComparison.OrdinalIgnoreCase) ||
                  firstSegment.Equals("assets", StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(
                "GridletComponents.PublicRoutePrefix cannot be 'api' or 'assets' (or be beneath " +
                "them); those paths are reserved by Gridlet.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
