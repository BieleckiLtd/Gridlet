namespace Gridlet.Models;

/// <summary>A schema created or edited through the object explorer.</summary>
public sealed record SchemaDesign(string Name, string? Owner = null);

/// <summary>A column definition used by the table designer (create table, add/alter column).</summary>
public sealed record ColumnDesign(
    string Name,
    string DataType,
    bool IsNullable = true,
    bool IsIdentity = false,
    bool IsPrimaryKey = false,
    string? DefaultExpression = null);

/// <summary>A new table as designed in the UI.</summary>
public sealed record TableDesign(
    string Schema,
    string Name,
    IReadOnlyList<ColumnDesign> Columns);
