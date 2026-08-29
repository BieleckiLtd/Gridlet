using Gridlet.Models;
using Xunit;

namespace Gridlet.Tests.Core;

public sealed class DataModelsTests
{
    [Fact]
    public void Result_column_retains_the_legacy_two_argument_constructor()
    {
        var constructor = typeof(ResultColumn).GetConstructor([typeof(string), typeof(string)]);

        Assert.NotNull(constructor);
        var column = Assert.IsType<ResultColumn>(constructor.Invoke(["Value", "int"]));
        Assert.False(column.IsBinary);
    }
}
