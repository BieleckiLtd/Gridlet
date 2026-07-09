namespace Gridlet.SqlServer;

internal static class SqlServerValues
{
    /// <summary>
    /// Converts an ADO.NET cell value into something safely JSON-serializable:
    /// <see cref="DBNull"/> becomes <c>null</c>, well-known scalars pass through,
    /// anything exotic (hierarchyid, geography, ...) becomes its string form.
    /// </summary>
    public static object? Materialize(object value)
        => value switch
        {
            null or DBNull => null,
            bool or byte or short or int or long or float or double or decimal
                or string or DateTime or DateTimeOffset or TimeSpan or Guid or byte[] => value,
            _ => value.ToString(),
        };
}
