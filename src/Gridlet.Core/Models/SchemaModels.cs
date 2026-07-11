namespace Gridlet.Models;

/// <summary>A database visible on a connection.</summary>
public sealed record DatabaseInfo(string Name, bool IsSystem);

/// <summary>Kinds of database objects Gridlet understands.</summary>
public enum DbObjectType
{
    Table,
    View,
    StoredProcedure,
    ScalarFunction,
    TableValuedFunction,
}

/// <summary>A schema-qualified database object.</summary>
public sealed record DbObjectInfo(string Schema, string Name, DbObjectType Type);

/// <summary>A database schema and its owning principal.</summary>
public sealed record SchemaInfo(string Name, string Owner);

/// <summary>A column of a table or view.</summary>
public sealed record ColumnInfo(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool IsPrimaryKey,
    string? DefaultDefinition,
    int Ordinal);

/// <summary>An index on a table, including the implicit primary-key index.</summary>
public sealed record IndexInfo(
    string Name,
    string Kind,
    bool IsUnique,
    bool IsPrimaryKey,
    IReadOnlyList<string> Columns);

/// <summary>One column pairing within a foreign key.</summary>
public sealed record ForeignKeyColumnPair(string Column, string ReferencedColumn);

/// <summary>A foreign key from this table to a referenced table.</summary>
public sealed record ForeignKeyInfo(
    string Name,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<ForeignKeyColumnPair> Columns);

/// <summary>Full structural description of a table or view.</summary>
public sealed record TableDefinition(
    DbObjectInfo Object,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys);
