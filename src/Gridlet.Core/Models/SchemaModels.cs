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
    Trigger,
}

/// <summary>A schema-qualified database object.</summary>
public sealed record DbObjectInfo(
    string Schema,
    string Name,
    DbObjectType Type,
    string? SubKind = null,
    bool IsInternal = false)
{
    /// <summary>Creates the legacy three-field object shape without relying on optional-parameter ABI.</summary>
    public DbObjectInfo(string schema, string name, DbObjectType type)
        : this(schema, name, type, null, false)
    {
    }

    /// <summary>Deconstructs the legacy three-field object shape.</summary>
    public void Deconstruct(out string schema, out string name, out DbObjectType type)
    {
        schema = Schema;
        name = Name;
        type = Type;
    }
}

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
    int Ordinal,
    string? ComputedDefinition = null,
    bool IsPersisted = false,
    long? IdentitySeed = null,
    long? IdentityIncrement = null,
    bool IsHidden = false)
{
    /// <summary>Creates the legacy twelve-field column shape without relying on optional-parameter ABI.</summary>
    public ColumnInfo(
        string name,
        string dataType,
        bool isNullable,
        bool isIdentity,
        bool isComputed,
        bool isPrimaryKey,
        string? defaultDefinition,
        int ordinal,
        string? computedDefinition,
        bool isPersisted,
        long? identitySeed,
        long? identityIncrement)
        : this(name, dataType, isNullable, isIdentity, isComputed, isPrimaryKey,
            defaultDefinition, ordinal, computedDefinition, isPersisted, identitySeed,
            identityIncrement, false)
    {
    }

    /// <summary>Deconstructs the legacy twelve-field column shape.</summary>
    public void Deconstruct(
        out string name,
        out string dataType,
        out bool isNullable,
        out bool isIdentity,
        out bool isComputed,
        out bool isPrimaryKey,
        out string? defaultDefinition,
        out int ordinal,
        out string? computedDefinition,
        out bool isPersisted,
        out long? identitySeed,
        out long? identityIncrement)
    {
        name = Name;
        dataType = DataType;
        isNullable = IsNullable;
        isIdentity = IsIdentity;
        isComputed = IsComputed;
        isPrimaryKey = IsPrimaryKey;
        defaultDefinition = DefaultDefinition;
        ordinal = Ordinal;
        computedDefinition = ComputedDefinition;
        isPersisted = IsPersisted;
        identitySeed = IdentitySeed;
        identityIncrement = IdentityIncrement;
    }
}

/// <summary>One ordered key in an index or unique constraint.</summary>
/// <param name="Column">The column name, or <see langword="null"/> for an expression key.</param>
/// <param name="Ordinal">The one-based key ordinal.</param>
/// <param name="Expression">The provider expression for an expression key.</param>
public sealed record IndexKeyInfo(
    string? Column,
    int Ordinal,
    bool IsDescending = false,
    string? Expression = null,
    string? Collation = null);

/// <summary>An index on a table, including the implicit primary-key index.</summary>
/// <remarks>
/// <paramref name="Columns"/> is retained as the provider-neutral, source-compatible list of
/// column names. <paramref name="KeyColumns"/> carries richer key metadata when the provider can
/// expose it (including expression keys, direction, and collation).
/// </remarks>
public sealed record IndexInfo(
    string Name,
    string Kind,
    bool IsUnique,
    bool IsPrimaryKey,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IndexKeyInfo>? KeyColumns = null,
    IReadOnlyList<string>? IncludedColumns = null,
    string? FilterDefinition = null,
    bool IsClustered = false,
    bool IsColumnstore = false,
    int FillFactor = 0,
    bool IsDisabled = false,
    bool IsOrderedColumnstore = false)
{
    /// <summary>Creates the legacy five-field index shape without relying on optional-parameter ABI.</summary>
    public IndexInfo(
        string name,
        string kind,
        bool isUnique,
        bool isPrimaryKey,
        IReadOnlyList<string> columns)
        : this(name, kind, isUnique, isPrimaryKey, columns, null, null, null, false, false, 0, false, false)
    {
    }

    /// <summary>Deconstructs the legacy five-field index shape.</summary>
    public void Deconstruct(
        out string name,
        out string kind,
        out bool isUnique,
        out bool isPrimaryKey,
        out IReadOnlyList<string> columns)
    {
        name = Name;
        kind = Kind;
        isUnique = IsUnique;
        isPrimaryKey = IsPrimaryKey;
        columns = Columns;
    }
}

/// <summary>A CHECK constraint declared on a table.</summary>
public sealed record CheckConstraintInfo(
    string? Name,
    string Definition,
    int Ordinal = 0,
    string? Column = null,
    bool IsDisabled = false,
    bool IsTrusted = true,
    bool IsNotForReplication = false);

/// <summary>
/// A UNIQUE table constraint. It is separate from <see cref="IndexInfo"/> even on providers that
/// implement the constraint with an index.
/// </summary>
public sealed record UniqueConstraintInfo(
    string? Name,
    IReadOnlyList<IndexKeyInfo> Columns,
    int Ordinal = 0,
    bool IsClustered = false,
    int FillFactor = 0,
    bool IsDisabled = false);

/// <summary>One column pairing within a foreign key.</summary>
public sealed record ForeignKeyColumnPair(string Column, string ReferencedColumn);

/// <summary>A foreign key from this table to a referenced table.</summary>
public sealed record ForeignKeyInfo(
    string Name,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<ForeignKeyColumnPair> Columns,
    string OnDelete = "NO_ACTION",
    string OnUpdate = "NO_ACTION");

/// <summary>Full structural description of a table or view.</summary>
public sealed record TableDefinition(
    DbObjectInfo Object,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<CheckConstraintInfo> CheckConstraints,
    IReadOnlyList<UniqueConstraintInfo> UniqueConstraints)
{
    /// <summary>Creates a definition for callers that do not yet supply CHECK or UNIQUE metadata.</summary>
    public TableDefinition(
        DbObjectInfo @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys)
        : this(@object, columns, indexes, foreignKeys, [], [])
    {
    }

    /// <summary>Deconstructs the legacy four-field table-definition shape.</summary>
    public void Deconstruct(
        out DbObjectInfo @object,
        out IReadOnlyList<ColumnInfo> columns,
        out IReadOnlyList<IndexInfo> indexes,
        out IReadOnlyList<ForeignKeyInfo> foreignKeys)
    {
        @object = Object;
        columns = Columns;
        indexes = Indexes;
        foreignKeys = ForeignKeys;
    }
}
