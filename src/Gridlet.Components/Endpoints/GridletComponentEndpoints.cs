using System.Text.Json;
using Gridlet.AspNetCore.Contracts;
using Gridlet.AspNetCore.Extensibility;
using Gridlet.Components.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gridlet.Components.Endpoints;

/// <summary>Body for creating or replacing a component document.</summary>
/// <param name="Html">
/// The document itself. It is stored exactly as it arrives: the server reads the version and the
/// name out of it and interprets nothing else, because the browser is the only thing that renders a
/// component.
/// </param>
public sealed record ComponentSaveRequest(
    string? Id,
    string Name,
    string Html);

/// <summary>Body for creating or replacing a component's JavaScript module.</summary>
public sealed record ComponentScriptSaveRequest(string? Source);

/// <summary>
/// Component administration endpoints, contributed into Gridlet's authorized API group.
/// </summary>
/// <remarks>
/// These are designer-side endpoints: they read and write component documents. Serving a component to end
/// users is a separate surface and is not part of this package yet.
/// </remarks>
internal sealed class GridletComponentEndpoints : IGridletEndpointContributor
{
    public void Map(IEndpointRouteBuilder api)
    {
        // Scripts are routed before the {id} component route so a module name can never be read as a
        // component id.
        api.MapGet("/components/scripts", GetScripts);
        api.MapGet("/components/scripts/{name}", GetScript);
        api.MapPut("/components/scripts/{name}", SaveScript);
        api.MapDelete("/components/scripts/{name}", DeleteScript);

        // The module itself, served as JavaScript for the browser to import. The version segment
        // is opaque and the server ignores it: it exists so a caller can ask for a module graph
        // the browser has not cached, which is what makes an edit take effect on the next run.
        // Relative imports inside a module resolve alongside it, so they carry the same version.
        api.MapGet("/components/modules/{version}/{name}", GetModule);

        api.MapGet("/components", GetComponents);
        api.MapGet("/components/{id}", GetComponent);
        api.MapPost("/components", SaveComponent);
        api.MapDelete("/components/{id}", DeleteComponent);
    }

    private static async Task<IResult> GetComponents(IComponentStore store, CancellationToken cancellationToken)
        => Results.Ok(await store.GetAllAsync(cancellationToken));

    private static async Task<IResult> GetComponent(string id, IComponentStore store, CancellationToken cancellationToken)
        => await store.FindAsync(id, cancellationToken) is { } component
            ? Results.Ok(component)
            : Results.NotFound(new GridletErrorResponse($"No component with id '{id}'."));

    private static async Task<IResult> SaveComponent(
        ComponentSaveRequest body, IComponentStore store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return Results.BadRequest(new GridletErrorResponse("A component needs a name."));
        }

        // A document says what it is. Anything that does not is either not a component or is
        // damaged, and storing it would leave a file the designer cannot open.
        if (GridletComponent.VersionOf(body.Html) is not { } version)
        {
            return Results.BadRequest(new GridletErrorResponse(
                "A component document must be HTML with a data-gridlet version on its <form> element."));
        }

        // Refusing a newer document is the whole point of the version: silently loading one would
        // let this build drop what it does not know about and then save the loss back.
        if (version > GridletComponent.CurrentDocumentVersion)
        {
            return Results.BadRequest(new GridletErrorResponse(
                $"This component was designed with a newer version of Gridlet (document version " +
                $"{version}, this build understands {GridletComponent.CurrentDocumentVersion})."));
        }

        var saved = await store.SaveAsync(
            new GridletComponent(
                string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("n") : body.Id,
                body.Name.Trim(),
                body.Html,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return Results.Ok(saved);
    }

    private static async Task<IResult> DeleteComponent(string id, IComponentStore store, CancellationToken cancellationToken)
        => await store.DeleteAsync(id, cancellationToken)
            ? Results.Ok(new { deleted = true })
            : Results.NotFound(new GridletErrorResponse($"No component with id '{id}'."));

    // ---- modules ----

    // The modules Gridlet ships come first and are marked read-only, so the list is everything a
    // component can import rather than only the part somebody wrote here.
    private static async Task<IResult> GetScripts(
        IComponentScriptStore store, CancellationToken cancellationToken)
        => Results.Ok(GridletBuiltInScripts.All()
            .Concat(await store.GetAllAsync(cancellationToken))
            .ToArray());

    private static async Task<IResult> GetScript(
        string name, IComponentScriptStore store, CancellationToken cancellationToken)
    {
        var script = GridletBuiltInScripts.Find(name) ?? await store.FindAsync(name, cancellationToken);
        return script is not null
            ? Results.Ok(script)
            : Results.NotFound(new GridletErrorResponse($"No module named '{name}'."));
    }

    private static async Task<IResult> SaveScript(
        string name,
        ComponentScriptSaveRequest body,
        IComponentScriptStore store,
        CancellationToken cancellationToken)
    {
        if (!GridletComponentScript.IsValidName(name))
        {
            return Results.BadRequest(new GridletErrorResponse(
                "A module name is a single file name ending in .js, using letters, digits, " +
                "dots, dashes and underscores."));
        }

        // Taking a shipped module's name would change what every `import './gridlet.js'` already
        // written means, so the name is refused rather than shadowed.
        if (GridletComponentScript.IsBuiltIn(name))
        {
            return Results.BadRequest(new GridletErrorResponse(
                $"'{name}' is a module Gridlet ships. Choose another name."));
        }

        return Results.Ok(await store.SaveAsync(name, body.Source ?? string.Empty, cancellationToken));
    }

    private static async Task<IResult> DeleteScript(
        string name, IComponentScriptStore store, CancellationToken cancellationToken)
    {
        if (GridletComponentScript.IsBuiltIn(name))
        {
            return Results.BadRequest(new GridletErrorResponse(
                $"'{name}' is part of Gridlet and cannot be deleted."));
        }

        return await store.DeleteAsync(name, cancellationToken)
            ? Results.Ok(new { deleted = true })
            : Results.NotFound(new GridletErrorResponse($"No module named '{name}'."));
    }

    private static async Task<IResult> GetModule(
        string version,
        string name,
        HttpContext context,
        IComponentScriptStore store,
        CancellationToken cancellationToken)
    {
        _ = version;
        var script = GridletBuiltInScripts.Find(name) ?? await store.FindAsync(name, cancellationToken);
        if (script is null)
        {
            // A missing module has to fail as a module: the browser is importing this, and an
            // error page arriving as JavaScript is a syntax error nobody can read.
            return Results.Text(
                $"throw new Error({JsonSerializer.Serialize($"There is no module named '{name}'.")});",
                "text/javascript",
                statusCode: StatusCodes.Status404NotFound);
        }

        // Never cached: the version segment is what a caller uses to get a fresh graph, and a
        // stale copy behind it would defeat that.
        context.Response.Headers.CacheControl = "no-store";
        return Results.Text(script.Source, "text/javascript");
    }
}
