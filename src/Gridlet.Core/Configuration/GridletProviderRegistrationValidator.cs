using Gridlet.Abstractions;
using Microsoft.Extensions.Options;

namespace Gridlet;

/// <summary>Validates that every configured connection has an available provider implementation.</summary>
public sealed class GridletProviderRegistrationValidator(IGridletProviderRegistry providers)
    : IValidateOptions<GridletOptions>
{
    public ValidateOptionsResult Validate(string? name, GridletOptions options)
    {
        var failures = options.Connections
            .Where(connection => !providers.TryGet(connection.ProviderName, out _))
            .Select(connection =>
                $"Gridlet connection '{connection.Name}' uses provider '{connection.ProviderName}', " +
                "but that provider is not registered.")
            .ToArray();

        return failures.Length > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
