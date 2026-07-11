using Microsoft.Extensions.DependencyInjection;

namespace Gridlet;

/// <summary>
/// Fluent builder returned by <c>AddGridlet(...)</c>. Provider packages hang their
/// registration extensions off this type (e.g. <c>AddSqlServer()</c>).
/// </summary>
public sealed class GridletBuilder(IServiceCollection services)
{
    /// <summary>
    /// Service collection being configured. Provider extension methods use this to register their
    /// <see cref="Abstractions.IGridletProvider"/> implementations.
    /// </summary>
    public IServiceCollection Services { get; } = services;
}
