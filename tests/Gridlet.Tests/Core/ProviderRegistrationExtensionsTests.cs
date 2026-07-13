using Gridlet.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gridlet.Tests.Core;

public sealed class ProviderRegistrationExtensionsTests
{
    [Fact]
    public void Provider_extensions_add_connections_with_derived_unique_labels()
    {
        var services = new ServiceCollection();

        services
            .AddGridletCore()
            .AddSqlServer("Server=sql01;Database=Sales;Integrated Security=True")
            .AddSqlServer("Server=sql01;Database=Sales;User ID=admin;Password=secret")
            .AddSqlite("Data Source=GridletSample.db");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GridletOptions>>().Value;

        Assert.Collection(options.Connections,
            connection =>
            {
                Assert.Equal("sql01", connection.Name);
                Assert.Equal("Sales", connection.DefaultDatabase);
                Assert.Equal(GridletProviderNames.SqlServer, connection.ProviderName);
            },
            connection =>
            {
                Assert.Equal("sql01 (2)", connection.Name);
                Assert.Equal("Sales", connection.DefaultDatabase);
                Assert.Equal(GridletProviderNames.SqlServer, connection.ProviderName);
            },
            connection =>
            {
                Assert.Equal("GridletSample.db", connection.Name);
                Assert.Equal("main", connection.DefaultDatabase);
                Assert.Equal(GridletProviderNames.Sqlite, connection.ProviderName);
            });

        var providers = provider.GetServices<IGridletProvider>().ToArray();
        Assert.Single(providers, item => item.ProviderName == GridletProviderNames.SqlServer);
        Assert.Single(providers, item => item.ProviderName == GridletProviderNames.Sqlite);
    }

    [Fact]
    public void Provider_extensions_apply_connection_configuration()
    {
        var services = new ServiceCollection();

        services
            .AddGridletCore()
            .AddSqlite("Data Source=local.db", connection => connection.AllowDdl = false);

        using var provider = services.BuildServiceProvider();
        var connection = Assert.Single(
            provider.GetRequiredService<IOptions<GridletOptions>>().Value.Connections);

        Assert.False(connection.AllowDdl);
    }

    [Fact]
    public void Configuration_overloads_validate_missing_connection_strings()
    {
        var configuration = new ConfigurationBuilder().Build();

        var sqlServer = Assert.Throws<GridletValidationException>(() =>
            new ServiceCollection().AddGridletCore().AddSqlServer(configuration, "Missing"));
        var sqlite = Assert.Throws<GridletValidationException>(() =>
            new ServiceCollection().AddGridletCore().AddSqlite(configuration, "Missing"));

        Assert.Equal("ConnectionStrings:Missing is not configured.", sqlServer.Message);
        Assert.Equal("ConnectionStrings:Missing is not configured.", sqlite.Message);
    }

    [Fact]
    public void Sqlite_registration_resolves_relative_data_source_from_supplied_base_path()
    {
        var services = new ServiceCollection();
        var basePath = Path.Combine(Path.GetTempPath(), "gridlet-app");

        services
            .AddGridletCore()
            .AddSqlite("Data Source=data/local.db", relativePathBase: basePath);

        using var provider = services.BuildServiceProvider();
        var connection = Assert.Single(
            provider.GetRequiredService<IOptions<GridletOptions>>().Value.Connections);
        var parsed = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection.ConnectionString);

        Assert.Equal(Path.Combine(basePath, "data/local.db"), parsed.DataSource);
        Assert.Equal("local.db", connection.Name);
    }
}
