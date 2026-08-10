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
    bool IsHidden = false,
    string? Collation = null)
{
    /// <summary>Creates the thirteen-field column shape without relying on optional-parameter ABI.</summary>
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
        long? identityIncrement,
        bool isHidden)
        : this(name, dataType, isNullable, isIdentity, isComputed, isPrimaryKey,
            defaultDefinition, ordinal, computedDefinition, isPersisted, identitySeed,
            identityIncrement, isHidden, null)
    {
    }

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
            identityIncrement, false, null)
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

/// <summary>How a provider identifies one row of a table for editing.</summary>
/// <param name="Kind">
/// One of <see cref="RowIdentityKinds"/>: the declared primary key, a unique key over
/// non-nullable columns, or a provider-supplied row identifier such as SQLite's <c>rowid</c>.
/// </param>
/// <param name="Columns">
/// The identifying columns, in key order. For <see cref="RowIdentityKinds.RowId"/> the single
/// entry is a pseudo-column that does not appear in <see cref="TableDefinition.Columns"/>.
/// </param>
/// <param name="Source">The constraint or index the identity was taken from, when it has a name.</param>
public sealed record RowIdentityInfo(string Kind, IReadOnlyList<string> Columns, string? Source = null);

/// <summary>The <see cref="RowIdentityInfo.Kind"/> values Gridlet understands.</summary>
public static class RowIdentityKinds
{
    /// <summary>The table's declared primary key.</summary>
    public const string PrimaryKey = "primaryKey";

    /// <summary>A unique constraint or unique index whose key columns are all non-nullable.</summary>
    public const string UniqueKey = "uniqueKey";

    /// <summary>A provider row identifier that is not one of the table's columns.</summary>
    public const string RowId = "rowId";
}

/// <summary>One parameter of a stored procedure or function.</summary>
/// <param name="Name">The parameter name as the engine declares it, including any leading marker.</param>
/// <param name="DataType">The declared type, formatted for display and for a DECLARE statement.</param>
/// <param name="Ordinal">The one-based position, or 0 for a return value.</param>
/// <param name="IsOutput">Whether the caller gets a value back through this parameter.</param>
/// <param name="IsReturnValue">Whether this is the routine's return value rather than a parameter.</param>
/// <param name="HasDefault">Whether the routine supplies a default when the argument is omitted.</param>
/// <param name="DefaultDefinition">The default value, where the engine exposes it.</param>
/// <param name="IsReadOnly">Whether the parameter is READONLY, as table-valued parameters must be.</param>
/// <param name="IsTableType">
/// Whether the parameter takes a table type. Such a parameter cannot be filled in from a simple
/// value, so callers offer it as something to script rather than something to type.
/// </param>
public sealed record RoutineParameterInfo(
    string Name,
    string DataType,
    int Ordinal,
    bool IsOutput = false,
    bool IsReturnValue = false,
    bool HasDefault = false,
    string? DefaultDefinition = null,
    bool IsReadOnly = false,
    bool IsTableType = false);

/// <summary>A stored procedure or function and the parameters it is called with.</summary>
public sealed record RoutineDefinition(
    DbObjectInfo Object,
    IReadOnlyList<RoutineParameterInfo> Parameters);

/// <summary>One argument supplied for a call to a routine.</summary>
/// <param name="Value">
/// The value as typed by the person, to be quoted for the parameter's declared type. Ignored when
/// <paramref name="IsNull"/> or <paramref name="IsRawSql"/> apply.
/// </param>
/// <param name="IsNull">Whether the argument is explicitly NULL.</param>
/// <param name="IsRawSql">
/// Whether <paramref name="Value"/> is already a SQL expression and should be placed in the script
/// as written. This is the escape hatch for types a text box cannot express, such as a table-valued
/// parameter variable.
/// </param>
public sealed record RoutineArgument(string? Value, bool IsNull = false, bool IsRawSql = false);

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
/// <param name="RowIdentity">
/// How a single row of this object can be addressed for editing, or <see langword="null"/> when the
/// provider cannot identify one row reliably.
/// </param>
public sealed record TableDefinition(
    DbObjectInfo Object,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<CheckConstraintInfo> CheckConstraints,
    IReadOnlyList<UniqueConstraintInfo> UniqueConstraints,
    RowIdentityInfo? RowIdentity = null,
    IReadOnlyList<string>? TableOptions = null)
{
    /// <summary>Creates the seven-field table-definition shape without relying on optional-parameter ABI.</summary>
    public TableDefinition(
        DbObjectInfo @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys,
        IReadOnlyList<CheckConstraintInfo> checkConstraints,
        IReadOnlyList<UniqueConstraintInfo> uniqueConstraints,
        RowIdentityInfo? rowIdentity)
        : this(@object, columns, indexes, foreignKeys, checkConstraints, uniqueConstraints, rowIdentity, null)
    {
    }

    /// <summary>Creates the six-field table-definition shape without relying on optional-parameter ABI.</summary>
    public TableDefinition(
        DbObjectInfo @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys,
        IReadOnlyList<CheckConstraintInfo> checkConstraints,
        IReadOnlyList<UniqueConstraintInfo> uniqueConstraints)
        : this(@object, columns, indexes, foreignKeys, checkConstraints, uniqueConstraints, null, null)
    {
    }

    /// <summary>Creates a definition for callers that do not yet supply CHECK or UNIQUE metadata.</summary>
    public TableDefinition(
        DbObjectInfo @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys)
        : this(@object, columns, indexes, foreignKeys, [], [], null, null)
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
