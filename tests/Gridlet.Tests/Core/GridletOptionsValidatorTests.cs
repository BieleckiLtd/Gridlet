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
    public void Valid_configuration_passes()
    {
        var result = Validate(o => o.AddConnection("Main", "Server=.;Integrated Security=True"));
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Duplicate_connection_names_fail_case_insensitively()
    {
        var result = Validate(o =>
        {
            o.AddConnection("Main", "Server=a;");
            o.AddConnection("main", "Server=b;");
        });

        Assert.True(result.Failed);
        Assert.Contains("Duplicate", result.FailureMessage);
    }

    [Fact]
    public void Empty_connection_string_fails()
    {
        var result = Validate(o => o.AddConnection("Main", ""));
        Assert.True(result.Failed);
    }

    [Fact]
    public void Empty_connection_name_fails()
    {
        var result = Validate(o => o.AddConnection("", "Server=.;"));
        Assert.True(result.Failed);
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
