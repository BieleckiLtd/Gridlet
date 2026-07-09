using Gridlet.Models;

namespace Gridlet.AspNetCore.Contracts;

/// <summary>Wire-level DTOs. Kept separate from the core domain model so the HTTP contract can evolve independently.</summary>
public sealed record GridletMetaResponse(string Version, IReadOnlyList<GridletConnectionSummary> Connections);

public sealed record GridletConnectionSummary(string Name, string ProviderName, bool AllowSqlExecution);

public sealed record DbObjectDto(string Schema, string Name, string Type);

public sealed record TableStructureResponse(
    DbObjectDto Object,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys);

public sealed record ObjectDefinitionResponse(string? Definition);

public sealed record QueryRequestBody(string? Sql);

public sealed record GridletErrorResponse(string Error);
