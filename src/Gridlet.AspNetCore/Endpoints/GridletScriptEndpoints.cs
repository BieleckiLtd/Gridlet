using System.Text;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using static Gridlet.AspNetCore.GridletEndpointHelpers;

namespace Gridlet.AspNetCore;

/// <summary>
/// Scripting an object as SQL. This is the standard escape hatch when the designer will not do
/// something: a script can be read, edited, kept and run somewhere else, which is what people
/// otherwise open a second tool for.
/// </summary>
internal static partial class GridletApiEndpoints
{
    private static void MapScripts(RouteGroupBuilder api)
        => api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/script",
            ScriptObject);

    private static Task<IResult> ScriptObject(
        string connection,
        string database,
        string schema,
        string name,
        ObjectScriptRequest body,
        IGridletConnectionResolver resolver,
        IOptionsMonitor<GridletOptions> options,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            var parts = (body.Include ?? ["create"])
                .Select(part => (part ?? "").Trim().ToLowerInvariant())
                .Where(part => part.Length > 0)
                .Distinct()
                .ToArray();
            if (parts.Length == 0)
            {
                throw new GridletValidationException(
                    "Choose at least one of 'drop', 'create' or 'data' to script.");
            }

            foreach (var part in parts)
            {
                if (part is not ("drop" or "create" or "data"))
                {
                    throw new GridletValidationException($"'{part}' is not something Gridlet can script.");
                }
            }

            var limits = options.CurrentValue.Limits;
            var maxRows = Math.Clamp(body.MaxRows ?? limits.MaxQueryResultRows, 1, limits.MaxQueryResultRows);

            // Only the identity of the object is needed to drop or create it. A full table
            // definition is asked for only when rows are scripted, so scripting a procedure never
            // depends on a provider being willing to describe one as if it were a table.
            var @object = await FindObjectAsync(resolved, schema, name, cancellationToken);
            var script = new StringBuilder();

            // Ordered so the result runs top to bottom: drop what is there, create it, fill it.
            if (parts.Contains("drop"))
            {
                Append(script, resolved.Provider.Ddl.BuildDropScript(@object));
            }

            if (parts.Contains("create"))
            {
                var create = await resolved.Provider.Schema.GetObjectDefinitionAsync(
                    resolved.Context, schema, name, cancellationToken);
                Append(script, string.IsNullOrWhiteSpace(create)
                    ? $"-- No definition available for {schema}.{name}."
                    : create.TrimEnd());
            }

            if (parts.Contains("data"))
            {
                if (@object.Type is not (DbObjectType.Table or DbObjectType.View))
                {
                    throw new GridletValidationException($"{schema}.{name} has no rows to script.");
                }

                var definition = await resolved.Provider.Schema.GetTableDefinitionAsync(
                    resolved.Context, schema, name, cancellationToken);
                Append(script, await BuildDataScriptAsync(
                    resolved, schema, name, definition, maxRows, limits.MaxPageSize, cancellationToken));
            }

            return Results.Ok(new ObjectScriptResponse(script.ToString().TrimEnd()));
        });

    /// <summary>
    /// Finds the object by name in the provider's own list, so its kind comes from the database
    /// rather than from the request.
    /// </summary>
    private static async Task<DbObjectInfo> FindObjectAsync(
        ResolvedConnection resolved,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        var objects = await resolved.Provider.Schema.GetObjectsAsync(resolved.Context, cancellationToken);
        return objects.FirstOrDefault(candidate =>
            string.Equals(candidate.Schema, schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new GridletObjectNotFoundException($"{schema}.{name}");
    }

    private static async Task<string> BuildDataScriptAsync(
        ResolvedConnection resolved,
        string schema,
        string name,
        TableDefinition definition,
        int maxRows,
        int maxPageSize,
        CancellationToken cancellationToken)
    {
        // Rows are read a page at a time, so scripting a large table does not depend on one
        // enormous result, and stops at the same cap the grid uses.
        //
        // Paging only holds together while every page comes from the same order, which the providers
        // get from the row identity. A table that has none - a heap with no unique, non-nullable key
        // - has no order to page by, and its second page can repeat rows from the first and skip
        // others entirely. Those rows are read in one request instead, so the script is at least a
        // consistent snapshot of the rows it did read.
        var pagesAreOrdered = definition.RowIdentity is not null;
        var columns = Array.Empty<ResultColumn>();
        var rows = new List<object?[]>();
        var page = 1;
        while (rows.Count < maxRows)
        {
            var pageSize = pagesAreOrdered ? Math.Min(maxPageSize, maxRows - rows.Count) : maxRows;
            var data = await resolved.Provider.Data.GetPageAsync(
                resolved.Context, schema, name, new TableDataRequest(page, pageSize), cancellationToken);
            if (page == 1)
            {
                columns = [.. data.Columns];
            }

            if (data.Rows.Count == 0)
            {
                break;
            }

            rows.AddRange(data.Rows);
            if (!pagesAreOrdered || rows.Count >= data.TotalRows)
            {
                break;
            }

            page++;
        }

        var script = resolved.Provider.Ddl.BuildInsertScript(definition, columns, rows);
        return rows.Count >= maxRows
            ? script + $"\n-- Stopped at {maxRows} rows."
            : script;
    }

    private static void Append(StringBuilder script, string part)
    {
        if (script.Length > 0)
        {
            script.AppendLine().AppendLine();
        }

        script.Append(part.TrimEnd());
    }
}
