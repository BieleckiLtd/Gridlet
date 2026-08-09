namespace Gridlet.Models;

/// <summary>A schema created or edited through the object explorer.</summary>
public sealed record SchemaDesign(string Name, string? Owner = null);

/// <summary>A column definition used by the table designer (create table, add/alter column).</summary>
// The compatibility constructor below leaves the serializer with two candidates, so the shape a
// request body binds to is named explicitly.
[method: System.Text.Json.Serialization.JsonConstructor]
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
    long IdentityIncrement = 1,
    string? Collation = null)
{
    /// <summary>Creates the legacy ten-field column shape without relying on optional-parameter ABI.</summary>
    public ColumnDesign(
        string name,
        string dataType,
        bool isNullable,
        bool isIdentity,
        bool isPrimaryKey,
        string? defaultExpression,
        string? computedExpression,
        bool isPersisted,
        long identitySeed,
        long identityIncrement)
        : this(name, dataType, isNullable, isIdentity, isPrimaryKey, defaultExpression,
            computedExpression, isPersisted, identitySeed, identityIncrement, null)
    {
    }
}

/// <summary>A primary-key constraint designed in the structure editor.</summary>
public sealed record PrimaryKeyDesign(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsClustered = true);

/// <summary>One ordered key in an index or unique constraint designed in the structure editor.</summary>
/// <param name="Column">The column name, or <see langword="null"/> for an expression key.</param>
/// <param name="Expression">The provider expression for an expression key.</param>
public sealed record IndexKeyDesign(
    string? Column,
    bool IsDescending = false,
    string? Expression = null,
    string? Collation = null);

/// <summary>A CHECK constraint designed in the structure editor.</summary>
public sealed record CheckConstraintDesign(
    string? Name,
    string Expression,
    bool CheckExistingData = true,
    bool IsDisabled = false,
    bool IsNotForReplication = false);

/// <summary>A UNIQUE table constraint designed in the structure editor.</summary>
public sealed record UniqueConstraintDesign(
    string? Name,
    IReadOnlyList<IndexKeyDesign> Columns,
    bool IsClustered = false,
    int FillFactor = 0,
    bool IsDisabled = false);

/// <summary>An ordinary index designed in the structure editor.</summary>
public sealed record IndexDesign(
    string Name,
    IReadOnlyList<IndexKeyDesign> KeyColumns,
    bool IsUnique = false,
    IReadOnlyList<string>? IncludedColumns = null,
    string? FilterExpression = null,
    bool IsClustered = false,
    bool IsColumnstore = false,
    int FillFactor = 0,
    bool IsDisabled = false);

/// <summary>
/// Identifies a constraint by provider name or by its ordinal when the provider supports unnamed
/// constraints (notably SQLite).
/// </summary>
public sealed record ConstraintReference(string? Name = null, int? Ordinal = null);

/// <summary>A foreign-key constraint designed in the structure editor.</summary>
public sealed record ForeignKeyDesign(
    string Name,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<ForeignKeyColumnPair> Columns,
    string OnDelete = "NO ACTION",
    string OnUpdate = "NO ACTION");

/// <summary>A new table as designed in the UI.</summary>
/// <param name="Options">
/// Table-level options the engine accepts after the column list, such as SQLite's
/// <c>WITHOUT ROWID</c> and <c>STRICT</c>. Providers reject anything they do not recognise.
/// </param>
// The compatibility constructor below leaves the serializer with two candidates, so the shape a
// request body binds to is named explicitly.
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record TableDesign(
    string Schema,
    string Name,
    IReadOnlyList<ColumnDesign> Columns,
    IReadOnlyList<string>? Options = null)
{

    /// <summary>Creates the legacy three-field design shape without relying on optional-parameter ABI.</summary>
    public TableDesign(string schema, string name, IReadOnlyList<ColumnDesign> columns)
        : this(schema, name, columns, null)
    {
    }
}
