using Gridlet.Abstractions;
using Gridlet.Models;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gridlet.Tests.Core;

public sealed class GridletProviderRegistrationValidationTests
{
    [Fact]
    public void Missing_provider_fails_validation_with_connection_context()
    {
        var options = new GridletOptions();
        options.AddConnection(
            "Reporting",
            "Data Source=reporting.db",
            GridletProviderNames.Sqlite);
        var validator = new GridletProviderRegistrationValidator(
            new GridletProviderRegistry([]));

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains("Reporting", result.FailureMessage);
        Assert.Contains("Sqlite", result.FailureMessage);
        Assert.Contains("not registered", result.FailureMessage);
    }

    [Fact]
    public void Registered_provider_passes_validation()
    {
        var options = new GridletOptions();
        options.AddConnection(
            "Main",
            "Server=fake;",
            FakeGridletProvider.Name);
        var validator = new GridletProviderRegistrationValidator(
            new GridletProviderRegistry([new FakeGridletProvider()]));

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Mapping_fails_fast_when_a_configured_provider_is_missing()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddGridlet(options => options.AddConnection(
            "Reporting",
            "Data Source=reporting.db",
            GridletProviderNames.Sqlite));
        using var app = builder.Build();

        var exception = Assert.Throws<OptionsValidationException>(() => app.MapGridlet());

        Assert.Contains("Reporting", exception.Message);
        Assert.Contains("Sqlite", exception.Message);
        Assert.Contains("not registered", exception.Message);
    }

    [Fact]
    public void Provider_can_depend_on_options_without_a_mapping_cycle()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddGridlet(options => options.AddConnection(
            "Main",
            "Server=fake;",
            FakeGridletProvider.Name));
        builder.Services.AddSingleton<IGridletProvider, OptionsDependentProvider>();
        using var app = builder.Build();

        app.MapGridlet();
    }

    public sealed class OptionsDependentProvider : IGridletProvider
    {
        private readonly FakeGridletProvider inner = new();

        public OptionsDependentProvider(IOptions<GridletOptions> options)
        {
            _ = options.Value;
        }

        public GridletProviderNames ProviderName => inner.ProviderName;
        public ISchemaReader Schema => inner.Schema;
        public ITableDataService Data => inner.Data;
        public IQueryRunner Query => inner.Query;
        public ITableWriteService Writes => inner.Writes;
        public ITableDdlService Ddl => inner.Ddl;
    }
}
