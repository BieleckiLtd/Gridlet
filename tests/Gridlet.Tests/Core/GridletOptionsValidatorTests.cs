using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gridlet.Tests.Core;

public class GridletOptionsValidatorTests
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(Action<GridletOptions> configure)
    {
        var options = new GridletOptions();
        configure(options);
        return new GridletOptionsValidator().Validate(null, options);
    }

    [Fact]
    public void Add_connection_requires_a_strongly_typed_provider()
    {
        var method = typeof(GridletOptions).GetMethods()
            .Single(candidate => candidate.Name == nameof(GridletOptions.AddConnection) &&
                candidate.GetParameters()[0].ParameterType == typeof(string));
        var provider = method.GetParameters().Single(parameter => parameter.Name == "providerName");

        Assert.Equal(typeof(GridletProviderNames), provider.ParameterType);
        Assert.False(provider.HasDefaultValue);
        Assert.Equal(typeof(GridletProviderNames), typeof(GridletConnectionOptions)
            .GetProperty(nameof(GridletConnectionOptions.ProviderName))!.PropertyType);
        Assert.Equal(GridletProviderNames.Unspecified, new GridletConnectionOptions().ProviderName);
    }

    [Fact]
    public void Configuration_key_is_used_as_the_default_connection_name()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Reporting"] = "Data Source=reporting.db",
            })
            .Build();
        var options = new GridletOptions();

        options.AddConnection(configuration, "Reporting", GridletProviderNames.Sqlite);

        var connection = Assert.Single(options.Connections);
        Assert.Equal("Reporting", connection.Name);
        Assert.Equal("Data Source=reporting.db", connection.ConnectionString);
        Assert.Equal(GridletProviderNames.Sqlite, connection.ProviderName);
    }

    [Fact]
    public void Missing_configuration_connection_key_fails_immediately()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<GridletValidationException>(() =>
            new GridletOptions().AddConnection(
                configuration, "Missing", GridletProviderNames.SqlServer));

        Assert.Contains("ConnectionStrings:Missing", exception.Message);
    }

    [Fact]
    public void Valid_configuration_passes()
    {
        var result = Validate(o => o.AddConnection(
            "Main", "Server=.;Integrated Security=True", GridletProviderNames.SqlServer));
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Duplicate_connection_names_fail_case_insensitively()
    {
        var result = Validate(o =>
        {
            o.AddConnection("Main", "Server=a;", GridletProviderNames.SqlServer);
            o.AddConnection("main", "Server=b;", GridletProviderNames.SqlServer);
        });

        Assert.True(result.Failed);
        Assert.Contains("Duplicate", result.FailureMessage);
    }

    [Fact]
    public void Empty_connection_string_fails()
    {
        var result = Validate(o => o.AddConnection("Main", "", GridletProviderNames.SqlServer));
        Assert.True(result.Failed);
    }

    [Fact]
    public void Empty_connection_name_fails()
    {
        var result = Validate(o => o.AddConnection("", "Server=.;", GridletProviderNames.SqlServer));
        Assert.True(result.Failed);
    }

    [Fact]
    public void Undefined_provider_enum_value_fails()
    {
        var result = Validate(o => o.Connections.Add(new GridletConnectionOptions
        {
            Name = "Main",
            ConnectionString = "Server=.;",
            ProviderName = (GridletProviderNames)999,
        }));

        Assert.True(result.Failed);
        Assert.Contains("unsupported ProviderName", result.FailureMessage);
    }

    [Fact]
    public void Unspecified_provider_fails()
    {
        var result = Validate(o => o.Connections.Add(new GridletConnectionOptions
        {
            Name = "Main",
            ConnectionString = "Server=.;",
        }));

        Assert.True(result.Failed);
        Assert.Contains("unsupported ProviderName", result.FailureMessage);
    }

    [Fact]
    public void Max_page_size_below_default_page_size_fails()
    {
        var result = Validate(o =>
        {
            o.Limits.DefaultPageSize = 100;
            o.Limits.MaxPageSize = 50;
        });

        Assert.True(result.Failed);
        Assert.Contains("MaxPageSize", result.FailureMessage);
    }
}
