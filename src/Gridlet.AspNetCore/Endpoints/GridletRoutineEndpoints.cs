using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Gridlet.AspNetCore.GridletEndpointHelpers;

namespace Gridlet.AspNetCore;

/// <summary>
/// Calling a stored procedure or function. Gridlet describes the routine's parameters, then turns
/// the values somebody enters into a script for the query editor, so the call that runs is one they
/// can read, edit and keep rather than something the tool does invisibly.
/// </summary>
internal static partial class GridletApiEndpoints
{
    private static void MapRoutines(RouteGroupBuilder api)
    {
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/routine",
            GetRoutineDefinition);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/routine/script",
            BuildRoutineScript);
    }

    private static Task<IResult> GetRoutineDefinition(
        string connection,
        string database,
        string schema,
        string name,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            var routine = await resolved.Provider.Schema.GetRoutineDefinitionAsync(
                resolved.Context, schema, name, cancellationToken);
            return Results.Ok(new RoutineDefinitionResponse(ToDto(routine.Object), routine.Parameters));
        });

    private static Task<IResult> BuildRoutineScript(
        string connection,
        string database,
        string schema,
        string name,
        RoutineScriptRequest body,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            if (!resolved.Context.Connection.AllowSqlExecution)
            {
                return Forbidden(
                    $"SQL execution is disabled for connection '{resolved.Context.ConnectionName}'.");
            }

            // The routine is re-read rather than taken from the request, so the script is built from
            // the parameters the database actually has.
            var routine = await resolved.Provider.Schema.GetRoutineDefinitionAsync(
                resolved.Context, schema, name, cancellationToken);
            var arguments = (body.Arguments ?? [])
                .ToDictionary(
                    entry => entry.Key,
                    entry => new RoutineArgument(
                        entry.Value?.Value, entry.Value?.IsNull ?? false, entry.Value?.IsRawSql ?? false),
                    StringComparer.OrdinalIgnoreCase);
            foreach (var argument in arguments.Keys)
            {
                if (!routine.Parameters.Any(parameter =>
                    string.Equals(parameter.Name, argument, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parameter.Name.TrimStart('@'), argument.TrimStart('@'),
                        StringComparison.OrdinalIgnoreCase)))
                {
                    throw new GridletValidationException(
                        $"'{argument}' is not a parameter of {schema}.{name}.");
                }
            }

            return Results.Ok(new RoutineScriptResponse(
                resolved.Provider.Schema.BuildRoutineExecuteScript(routine, arguments)));
        });
}
