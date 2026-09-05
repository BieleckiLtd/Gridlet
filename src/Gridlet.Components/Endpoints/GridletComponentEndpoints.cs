using System.Text.Json;
using System.Text;
using Gridlet.AspNetCore.Agents;
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
    string Html,
    bool? Routable = null,
    string? Route = null,
    string? Title = null);

/// <summary>Body for creating or replacing a component's JavaScript module.</summary>
public sealed record ComponentScriptSaveRequest(string? Source);

/// <summary>
/// Component administration endpoints, contributed into Gridlet's authorized API group.
/// </summary>
/// <remarks>
/// These include the designer-side JSON endpoints and the separate consumer-facing component page.
/// </remarks>
internal sealed class GridletComponentEndpoints :
    IGridletEndpointContributor,
    IGridletRuntimeContributor,
    IGridletRootRuntimeContributor,
    IGridletRuntimeRouteMetadata
{
    private readonly IOptions<GridletComponentsOptions> componentOptions;

    public GridletComponentEndpoints(IOptions<GridletComponentsOptions> componentOptions)
    {
        this.componentOptions = componentOptions;
    }

    string? IGridletRootRuntimeContributor.RootPath
        => componentOptions.Value.PublicRoutePrefix.TrimStart().StartsWith('/')
            ? NormalizeConfiguredPath(componentOptions.Value.PublicRoutePrefix, absolute: true)
            : null;

    string IGridletRuntimeRouteMetadata.ComponentPublicPath
        => NormalizeConfiguredPath(
            componentOptions.Value.PublicRoutePrefix,
            componentOptions.Value.PublicRoutePrefix.TrimStart().StartsWith('/'));
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
        var (componentPrefix, isAbsolute) = ValidateAndNormalizePaths(gridlet.ServiceProvider);
        if (isAbsolute)
        {
            // Absolute public paths are mapped by MapAtRoot, where the route group is rooted at
            // the configured path. Mapping it beneath /gridlet as well would create a duplicate
            // endpoint and defeat the purpose of independent public URLs.
            return;
        }

        // The published endpoint catch-all is mapped before this endpoint by MapGridlet. The
        // handler also rejects the published prefix's first segment, so an unknown /pub/api/...
        // path can never be served as a component page when both prefixes share a root.
        var route = componentPrefix.Length == 0
            ? "/{**route}"
            : $"/{componentPrefix}/{{**route}}";
        gridlet.MapGet(route, GetComponentPage);
    }

    void IGridletRootRuntimeContributor.MapAtRoot(IEndpointRouteBuilder root)
    {
        var (_, isAbsolute) = ValidateAndNormalizePaths(root.ServiceProvider);
        if (!isAbsolute)
        {
            return;
        }

        // The root group itself represents the configured component-public prefix. Routes below
        // it are therefore the component's custom slug, including multi-segment slugs.
        root.MapGet("/{**route}", GetComponentPage);
    }

    private static (string Prefix, bool IsAbsolute) ValidateAndNormalizePaths(IServiceProvider services)
    {
        var gridletOptions = services.GetRequiredService<IOptions<GridletOptions>>().Value;
        var componentOptions = services.GetRequiredService<IOptions<GridletComponentsOptions>>().Value;
        if (!GridletRoutePath.TryNormalize(
                componentOptions.PublicRoutePrefix, out var componentPrefix, allowEmpty: true))
        {
            throw new InvalidOperationException(
                "GridletComponents.PublicRoutePrefix is not a safe route path.");
        }

        var publishedValue = gridletOptions.PublishedApiPath ?? gridletOptions.PublishedApiRoutePrefix;
        if (!GridletRoutePath.TryNormalize(publishedValue, out var publishedPrefix))
        {
            throw new InvalidOperationException(
                "PublishedApiRoutePrefix/PublishedApiPath is not a safe route path.");
        }

        // A component catch-all must not own the management API, assets, or the published API
        // subtree. Equal/ancestor prefixes are rejected at mapping time because the Core options
        // validator cannot see the optional Components package.
        if (!componentOptions.PublicRoutePrefix.TrimStart().StartsWith('/') &&
            (componentPrefix.Equals("api", StringComparison.OrdinalIgnoreCase) ||
             componentPrefix.Equals("assets", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Gridlet component pages cannot use reserved 'api' or 'assets' paths.");
        }

        // PublishedApiPath is always application-rooted when supplied, even if the host writes it
        // without a leading slash. PublicRoutePrefix is the component setting that can be either
        // relative to the Gridlet mount or application-rooted.
        var publishedIsAbsolute = gridletOptions.PublishedApiPath is not null;
        var componentIsAbsolute = componentOptions.PublicRoutePrefix.TrimStart().StartsWith('/');
        if (publishedIsAbsolute == componentIsAbsolute &&
            GridletRoutePath.IsEqualOrAncestor(publishedPrefix, componentPrefix))
        {
            throw new InvalidOperationException(
                "Gridlet component and published API route prefixes must not be equal, and the " +
                "component route cannot be beneath the published API path.");
        }

        return (componentPrefix, componentIsAbsolute);
    }

    private static async Task<IResult> GetComponents(IComponentStore store, CancellationToken cancellationToken)
        => Results.Ok(await store.GetAllAsync(cancellationToken));

    private static async Task<IResult> GetComponent(string id, IComponentStore store, CancellationToken cancellationToken)
        => await store.FindAsync(id, cancellationToken) is { } component
            ? Results.Ok(component)
            : Results.NotFound(new GridletErrorResponse($"No component with id '{id}'."));

    private static async Task<IResult> GetComponentPage(
        string? route,
        HttpContext context,
        IComponentStore store,
        IOptions<GridletOptions> options,
        IOptions<GridletComponentsOptions> componentOptions,
        GridletMountPath mountPath,
        CancellationToken cancellationToken)
    {
        // ASP.NET may decode a percent-encoded separator before binding a catch-all route. Reject
        // the encoded request path itself as well, so `%2F`, `%5C` and encoded dot segments can
        // never become an apparently safe route value.
        if ((context.Request.Path.Value ?? string.Empty).Contains('%', StringComparison.Ordinal))
        {
            return Results.NotFound(new GridletErrorResponse("The component route is not valid."));
        }

        if (!GridletRoutePath.TryNormalize(route, out var normalizedRoute))
        {
            return Results.NotFound(new GridletErrorResponse("The component route is not valid."));
        }

        if (GridletRoutePath.TryNormalize(
                componentOptions.Value.PublicRoutePrefix, out var componentRoot, allowEmpty: true) &&
            componentRoot.Length == 0 &&
            (GridletRoutePath.FirstSegment(normalizedRoute).Equals("api", StringComparison.OrdinalIgnoreCase) ||
             GridletRoutePath.FirstSegment(normalizedRoute).Equals("assets", StringComparison.OrdinalIgnoreCase)))
        {
            return Results.NotFound(new GridletErrorResponse("No component exists at that route."));
        }

        var configuredPublishedPath = options.Value.PublishedApiPath ?? options.Value.PublishedApiRoutePrefix;
        var componentPath = componentOptions.Value.PublicRoutePrefix;
        if (GridletRoutePath.TryNormalize(configuredPublishedPath, out var publishedPrefix) &&
            GridletRoutePath.TryNormalize(componentPath, out var componentPrefix, allowEmpty: true))
        {
            var publishedIsAbsolute = options.Value.PublishedApiPath is not null;
            var componentIsAbsolute = componentPath.TrimStart().StartsWith('/');
            // If the API path is beneath the component path (the supported /pub and /pub/api
            // layout), compare the API's suffix as seen inside the component route group.
            var reserved = publishedPrefix;
            var sharesComponentRoot = publishedIsAbsolute == componentIsAbsolute &&
                (componentPrefix.Length == 0 ||
                 publishedPrefix.StartsWith(componentPrefix + "/", StringComparison.OrdinalIgnoreCase));
            if (componentPrefix.Length > 0 && sharesComponentRoot)
            {
                reserved = publishedPrefix[(componentPrefix.Length + 1)..];
            }

            if (sharesComponentRoot &&
                (normalizedRoute.Equals(reserved, StringComparison.OrdinalIgnoreCase) ||
                normalizedRoute.StartsWith(reserved + "/", StringComparison.OrdinalIgnoreCase))
               )
            {
                return Results.NotFound(new GridletErrorResponse("No component exists at that route."));
            }
        }

        var component = (await store.GetAllAsync(cancellationToken))
            .Where(candidate => candidate.Routable)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.EffectiveRoute, normalizedRoute, StringComparison.OrdinalIgnoreCase));
        if (component is null)
        {
            return Results.NotFound(new GridletErrorResponse($"No component at route '{normalizedRoute}'."));
        }

        // The document is placed in a template as base64. This keeps arbitrary author markup inert
        // until runtime.js parses and sanitizes it, and avoids a document containing </template> or
        // a non-ASCII character changing the page that carries it.
        var document = Convert.ToBase64String(Encoding.UTF8.GetBytes(component.Html));
        var title = System.Net.WebUtility.HtmlEncode(component.EffectiveTitle);
        var publishedSegment = System.Net.WebUtility.HtmlEncode(options.Value.PublishedApiSegment);
        var runtimeMountPath = RuntimeMountPath(context, mountPath.Value);
        var scriptPath = RuntimeScriptPath(runtimeMountPath);
        var stylesPath = ComponentStylesheetPath(runtimeMountPath);
        var componentRoute = System.Net.WebUtility.HtmlEncode(component.EffectiveRoute);
        var runtimeMount = System.Net.WebUtility.HtmlEncode(runtimeMountPath);
        var publishedApiPath = System.Net.WebUtility.HtmlEncode(
            options.Value.PublishedApiPath is { } configuredRootPath
                ? NormalizeConfiguredPath(configuredRootPath, absolute: true)
                : $"{runtimeMountPath.TrimEnd('/')}/{options.Value.PublishedApiSegment}");
        var publicPath = System.Net.WebUtility.HtmlEncode(
            NormalizeConfiguredPath(
                componentOptions.Value.PublicRoutePrefix,
                componentOptions.Value.PublicRoutePrefix.TrimStart().StartsWith('/')));

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
  <link rel="stylesheet" href="%COMPONENT_STYLES%">
  <style>
    :root { color-scheme: light dark; font-family: system-ui, sans-serif; color: CanvasText; background: Canvas; }
    *, *::before, *::after { box-sizing: border-box; }
    html, body { width: 100%; height: 100%; min-width: 0; min-height: 100%; margin: 0; }
    body { min-height: 100vh; overflow-x: hidden; }
    #gridlet-component-host { width: 100%; height: 100%; min-width: 0; min-height: 100vh; max-width: 100%; }
    .gridlet-runtime-message { color: #b42318; background: #fef3f2; border: 1px solid #fecdca; border-radius: 6px; padding: 12px 16px; font: 14px system-ui, sans-serif; }
    @media (prefers-color-scheme: dark) {
      .gridlet-runtime-message { color: #fda29b; background: #55160c; border-color: #912018; }
    }
  </style>
</head>
<body data-gridlet-published-segment="%PUBLISHED_SEGMENT%" data-gridlet-published-api-path="%PUBLISHED_PATH%" data-gridlet-component-public-path="%COMPONENT_PUBLIC_PATH%" data-gridlet-component-id="%COMPONENT_ID%" data-gridlet-component-route="%COMPONENT_ROUTE%" data-gridlet-component-routable="true" data-gridlet-runtime-mount="%RUNTIME_MOUNT%">
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
            .Replace("%COMPONENT_STYLES%", System.Net.WebUtility.HtmlEncode(stylesPath), StringComparison.Ordinal)
            .Replace("%PUBLISHED_SEGMENT%", publishedSegment, StringComparison.Ordinal)
            .Replace("%PUBLISHED_PATH%", publishedApiPath, StringComparison.Ordinal)
            .Replace("%COMPONENT_PUBLIC_PATH%", publicPath, StringComparison.Ordinal)
            .Replace("%COMPONENT_ID%", System.Net.WebUtility.HtmlEncode(component.Id), StringComparison.Ordinal)
            .Replace("%COMPONENT_ROUTE%", componentRoute, StringComparison.Ordinal)
            .Replace("%RUNTIME_MOUNT%", runtimeMount, StringComparison.Ordinal)
            .Replace("%TITLE%", title, StringComparison.Ordinal);

        return Results.Content(page, "text/html; charset=utf-8");
    }

    private static string RuntimeMountPath(HttpContext context, string mount)
        => (context.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty) + mount;

    private static string RuntimeScriptPath(string mount)
        => mount.TrimEnd('/') + "/assets/modules/components/runtime.js";

    // Linked rather than injected, and the same file the designer loads: the rules that paint a
    // component are one stylesheet, so the two surfaces cannot disagree about them.
    private static string ComponentStylesheetPath(string mount)
        => mount.TrimEnd('/') + "/assets/modules/components/component.css";

    private static async Task<IResult> SaveComponent(
        ComponentSaveRequest body,
        IComponentStore store,
        IOptions<GridletOptions> options,
        IOptions<GridletComponentsOptions> componentOptions,
        CancellationToken cancellationToken)
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

        var existing = string.IsNullOrWhiteSpace(body.Id)
            ? null
            : await store.FindAsync(body.Id, cancellationToken);
        var id = string.IsNullOrWhiteSpace(body.Id)
            ? Guid.NewGuid().ToString("n")
            : body.Id!;

        var route = body.Route is null
            ? existing?.Route
            : NormalizeComponentRoute(body.Route);
        if (route is null && body.Route is not null && body.Route.Trim().Length > 0)
        {
            return Results.BadRequest(new GridletErrorResponse(
                "A component route must contain safe, non-empty ASCII route segments."));
        }

        if (route is not null && IsReservedComponentRoute(
                route, options.Value.PublishedApiPath ?? options.Value.PublishedApiRoutePrefix,
                componentOptions.Value.PublicRoutePrefix,
                options.Value.PublishedApiPath is not null))
        {
            return Results.BadRequest(new GridletErrorResponse(
                "The component route is reserved for Gridlet's management or published API."));
        }

        var title = body.Title is null ? existing?.Title : NormalizeComponentTitle(body.Title);
        if (body.Title is not null && body.Title.Trim().Length > 256)
        {
            return Results.BadRequest(new GridletErrorResponse("A component browser title may not exceed 256 characters."));
        }

        var routable = body.Routable ?? existing?.Routable ?? false;
        var candidate = new GridletComponent(id, body.Name.Trim(), body.Html, DateTimeOffset.UtcNow)
        {
            Routable = routable,
            Route = route,
            Title = title,
        };

        if (routable)
        {
            var all = await store.GetAllAsync(cancellationToken);
            if (all.Any(other => !string.Equals(other.Id, candidate.Id, StringComparison.OrdinalIgnoreCase) &&
                                 other.Routable &&
                                 string.Equals(other.EffectiveRoute, candidate.EffectiveRoute, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.Conflict(new GridletErrorResponse(
                    $"Another routable component already uses route '{candidate.EffectiveRoute}'."));
            }
        }

        var saved = await store.SaveAsync(candidate, cancellationToken);
        if (store is IComponentPublicationStore publicationStore)
        {
            // The default file store persists this alongside the HTML as part of SaveAsync. An
            // optional second call also lets custom stores keep the publication contract separate
            // from their legacy HTML implementation without changing IComponentStore.
            await publicationStore.SavePublicationAsync(
                candidate.Id, candidate.Routable, candidate.Route, candidate.Title, cancellationToken);
            saved = saved with
            {
                Routable = candidate.Routable,
                Route = candidate.Route,
                Title = candidate.Title,
            };
        }

        return Results.Ok(saved);
    }

    private static async Task<IResult> DeleteComponent(
        string id, IComponentStore store, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(id, cancellationToken);
        if (store is IComponentPublicationStore publicationStore)
        {
            await publicationStore.DeletePublicationAsync(id, cancellationToken);
        }

        return deleted
            ? Results.Ok(new { deleted = true })
            : Results.NotFound(new GridletErrorResponse($"No component with id '{id}'."));
    }

    private static string? NormalizeComponentRoute(string value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : GridletRoutePath.TryNormalize(value, out var normalized) ? normalized : null;

    private static string NormalizeConfiguredPath(string value, bool absolute)
        => GridletRoutePath.TryNormalize(value, out var normalized, allowEmpty: true)
            ? absolute ? "/" + normalized : normalized
            : value;

    private static string? NormalizeComponentTitle(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsReservedComponentRoute(
        string route, string publishedPath, string componentPath, bool publishedIsAbsolute)
    {
        if (!GridletRoutePath.TryNormalize(publishedPath, out var publishedPrefix) ||
            !GridletRoutePath.TryNormalize(componentPath, out var componentPrefix, allowEmpty: true))
        {
            return true;
        }

        if (componentPrefix.Length == 0)
        {
            var first = GridletRoutePath.FirstSegment(route);
            if (first.Equals("api", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("assets", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var componentIsAbsolute = componentPath.TrimStart().StartsWith('/');
        if (componentIsAbsolute != publishedIsAbsolute)
        {
            return false;
        }

        var sharesComponentRoot = componentPrefix.Length == 0 ||
            publishedPrefix.StartsWith(componentPrefix + "/", StringComparison.OrdinalIgnoreCase);
        if (!sharesComponentRoot)
        {
            return false;
        }

        var reserved = componentPrefix.Length == 0
            ? publishedPrefix
            : publishedPrefix[(componentPrefix.Length + 1)..];
        return route.Equals(reserved, StringComparison.OrdinalIgnoreCase) ||
               route.StartsWith(reserved + "/", StringComparison.OrdinalIgnoreCase);
    }

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
