namespace Gridlet.Sqlite;

internal static class SqliteValues
{
    public static object? Materialize(object value)
        => value switch
        {
            null or DBNull => null,
            bool or byte or short or int or long or float or double or decimal
                or string or DateTime or DateTimeOffset or TimeSpan or Guid or byte[] => value,
            _ => value.ToString(),
        };
}
