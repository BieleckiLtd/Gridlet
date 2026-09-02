using System.Text.Json;
using System.Text;
using Gridlet.AspNetCore.Contracts;
using Gridlet.AspNetCore.Extensibility;
using Gridlet.Components.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
/// These include the designer-side JSON endpoints and the separate consumer-facing component page.
/// </remarks>
internal sealed class GridletComponentEndpoints : IGridletEndpointContributor, IGridletRuntimeContributor
{
    void IGridletEndpointContributor.Map(IEndpointRouteBuilder api)
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

    /// <summary>
    /// Maps the consumer-facing component page. It is deliberately outside <c>/api</c>: the
    /// designer needs a JSON document, while a person filling or viewing a component needs an
    /// ordinary browser page.
    /// </summary>
    void IGridletRuntimeContributor.Map(IEndpointRouteBuilder gridlet)
    {
        var options = gridlet.ServiceProvider.GetRequiredService<IOptions<GridletOptions>>().Value;
        if (options.PublishedApiSegment.Equals("components", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PublishedApiRoutePrefix cannot be 'components' when Gridlet components are enabled; " +
                "that route is reserved for consumer-facing components.");
        }

        gridlet.MapGet("/components/{id}", GetComponentPage);
    }

    private static async Task<IResult> GetComponents(IComponentStore store, CancellationToken cancellationToken)
        => Results.Ok(await store.GetAllAsync(cancellationToken));

    private static async Task<IResult> GetComponent(string id, IComponentStore store, CancellationToken cancellationToken)
        => await store.FindAsync(id, cancellationToken) is { } component
            ? Results.Ok(component)
            : Results.NotFound(new GridletErrorResponse($"No component with id '{id}'."));

    private static async Task<IResult> GetComponentPage(
        string id,
        HttpContext context,
        IComponentStore store,
        IOptions<GridletOptions> options,
        CancellationToken cancellationToken)
    {
        var component = await store.FindAsync(id, cancellationToken);
        if (component is null)
        {
            return Results.NotFound(new GridletErrorResponse($"No component with id '{id}'."));
        }

        // The document is placed in a template as base64. This keeps arbitrary author markup inert
        // until runtime.js parses and sanitizes it, and avoids a document containing </template> or
        // a non-ASCII character changing the page that carries it.
        var document = Convert.ToBase64String(Encoding.UTF8.GetBytes(component.Html));
        var title = System.Net.WebUtility.HtmlEncode(component.Name);
        var publishedSegment = System.Net.WebUtility.HtmlEncode(options.Value.PublishedApiSegment);
        var scriptPath = RuntimeScriptPath(context, id);

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; " +
            "form-action 'self'; frame-ancestors 'self'";

        var page = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>%TITLE%</title>
  <style>
    :root { color-scheme: light dark; font-family: system-ui, sans-serif; color: CanvasText; background: Canvas; }
    body { margin: 0; min-height: 100vh; display: grid; place-items: start center; box-sizing: border-box; padding: 24px; }
    #gridlet-component-host { max-width: 100%; }
    .gridlet-runtime-message { color: #b42318; background: #fef3f2; border: 1px solid #fecdca; border-radius: 6px; padding: 12px 16px; font: 14px system-ui, sans-serif; }
    @media (prefers-color-scheme: dark) {
      .gridlet-runtime-message { color: #fda29b; background: #55160c; border-color: #912018; }
    }
  </style>
</head>
<body data-gridlet-published-segment="%PUBLISHED_SEGMENT%" data-gridlet-component-id="%COMPONENT_ID%">
  <main id="gridlet-component-host" aria-live="polite">
    <template id="gridlet-component-document">%DOCUMENT%</template>
  </main>
  <script type="module" src="%RUNTIME_SCRIPT%"></script>
</body>
</html>
"""
            // Replace the document first: unlike base64, an arbitrary component name may contain
            // one of the page placeholders, and values inserted later must not be substituted.
            .Replace("%DOCUMENT%", document, StringComparison.Ordinal)
            .Replace("%RUNTIME_SCRIPT%", System.Net.WebUtility.HtmlEncode(scriptPath), StringComparison.Ordinal)
            .Replace("%PUBLISHED_SEGMENT%", publishedSegment, StringComparison.Ordinal)
            .Replace("%COMPONENT_ID%", System.Net.WebUtility.HtmlEncode(id), StringComparison.Ordinal)
            .Replace("%TITLE%", title, StringComparison.Ordinal);

        return Results.Content(page, "text/html; charset=utf-8");
    }

    private static string RuntimeScriptPath(HttpContext context, string id)
    {
        var requestPath = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        var suffix = "/components/" + id;
        var mount = requestPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? requestPath[..^suffix.Length]
            : "/gridlet";
        var pathBase = context.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
        return $"{pathBase}{mount.TrimEnd('/')}/assets/modules/components/runtime.js";
    }

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
                "A component document must be HTML whose root element carries a data-gridlet version."));
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
