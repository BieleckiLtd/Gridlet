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
    string? DefaultExpression = null,
    string? ComputedExpression = null,
    bool IsPersisted = false,
    long IdentitySeed = 1,
    long IdentityIncrement = 1);

/// <summary>A primary-key constraint designed in the structure editor.</summary>
public sealed record PrimaryKeyDesign(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsClustered = true);

/// <summary>A foreign-key constraint designed in the structure editor.</summary>
public sealed record ForeignKeyDesign(
    string Name,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<ForeignKeyColumnPair> Columns,
    string OnDelete = "NO ACTION",
    string OnUpdate = "NO ACTION");

/// <summary>A new table as designed in the UI.</summary>
public sealed record TableDesign(
    string Schema,
    string Name,
    IReadOnlyList<ColumnDesign> Columns);
