using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gridlet.Tests.Core;

public sealed class GridletConfigurationRegistrationTests
{
    [Fact]
    public void Configuration_section_binds_all_core_option_groups()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gridlet:Connections:0:Name"] = "Reporting",
                ["Gridlet:Connections:0:ConnectionString"] = "Data Source=reporting.db",
                ["Gridlet:Connections:0:ProviderName"] = "Sqlite",
                ["Gridlet:Connections:0:DefaultDatabase"] = "main",
                ["Gridlet:Connections:0:AllowSqlExecution"] = "false",
                ["Gridlet:Connections:0:AllowWrites"] = "false",
                ["Gridlet:Connections:0:AllowDdl"] = "false",
                ["Gridlet:Limits:DefaultPageSize"] = "25",
                ["Gridlet:Limits:MaxPageSize"] = "250",
                ["Gridlet:Limits:MaxQueryResultRows"] = "2500",
                ["Gridlet:Limits:CommandTimeoutSeconds"] = "15",
                ["Gridlet:Security:AllowAnonymous"] = "true",
                ["Gridlet:Security:AuthorizationPolicy"] = "GridletAccess",
                ["Gridlet:Storage:FilePath"] = "App_Data/gridlet.json",
                ["Gridlet:Audit:IncludeSqlText"] = "false",
                ["Gridlet:Audit:IncludeErrorDetails"] = "false",
            })
            .Build();
        var services = new ServiceCollection();

        services
            .AddGridletFromConfiguration(configuration.GetSection("Gridlet"))
            .AddSqlite();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GridletOptions>>().Value;
        var connection = Assert.Single(options.Connections);

        Assert.Equal("Reporting", connection.Name);
        Assert.Equal("Data Source=reporting.db", connection.ConnectionString);
        Assert.Equal(GridletProviderNames.Sqlite, connection.ProviderName);
        Assert.Equal("main", connection.DefaultDatabase);
        Assert.False(connection.AllowSqlExecution);
        Assert.False(connection.AllowWrites);
        Assert.False(connection.AllowDdl);
        Assert.Equal(25, options.Limits.DefaultPageSize);
        Assert.Equal(250, options.Limits.MaxPageSize);
        Assert.Equal(2500, options.Limits.MaxQueryResultRows);
        Assert.Equal(15, options.Limits.CommandTimeoutSeconds);
        Assert.True(options.Security.AllowAnonymous);
        Assert.Equal("GridletAccess", options.Security.AuthorizationPolicy);
        Assert.Equal("App_Data/gridlet.json", options.Storage.FilePath);
        Assert.False(options.Audit.IncludeSqlText);
        Assert.False(options.Audit.IncludeErrorDetails);
    }

    [Fact]
    public void Configuration_section_still_runs_normal_options_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gridlet:Limits:DefaultPageSize"] = "100",
                ["Gridlet:Limits:MaxPageSize"] = "50",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddGridletFromConfiguration(configuration.GetSection("Gridlet"));

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<GridletOptions>>().Value);

        Assert.Contains("MaxPageSize", exception.Message);
    }
}
