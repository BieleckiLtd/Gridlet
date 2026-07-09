namespace Gridlet.SqlServer;

/// <summary>Formats SQL Server type metadata into display names like <c>nvarchar(50)</c> or <c>decimal(10,2)</c>.</summary>
public static class SqlServerDataTypeFormatter
{
    public static string Format(string typeName, int maxLength, int precision, int scale)
        => typeName.ToLowerInvariant() switch
        {
            "nvarchar" or "nchar" => maxLength == -1 ? $"{typeName}(max)" : $"{typeName}({maxLength / 2})",
            "varchar" or "char" or "varbinary" or "binary" => maxLength == -1 ? $"{typeName}(max)" : $"{typeName}({maxLength})",
            "decimal" or "numeric" => $"{typeName}({precision},{scale})",
            "datetime2" or "datetimeoffset" or "time" => $"{typeName}({scale})",
            _ => typeName,
        };
}
