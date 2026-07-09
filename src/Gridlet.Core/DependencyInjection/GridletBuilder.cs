using Microsoft.Extensions.DependencyInjection;

namespace Gridlet;

/// <summary>
/// Fluent builder returned by <c>AddGridlet(...)</c>. Provider packages hang their
/// registration extensions off this type (e.g. <c>AddSqlServer()</c>).
/// </summary>
public sealed class GridletBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}
