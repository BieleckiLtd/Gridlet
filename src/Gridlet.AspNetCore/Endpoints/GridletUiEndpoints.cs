using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Gridlet.AspNetCore;

/// <summary>Serves the embedded single-page UI.</summary>
internal static class GridletUiEndpoints
{
    private static readonly ManifestEmbeddedFileProvider Files =
        new(typeof(GridletUiEndpoints).Assembly, "UI/wwwroot");

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private static readonly Lazy<string> IndexTemplate = new(() =>
    {
        using var stream = Files.GetFileInfo("index.html").CreateReadStream();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static void Map(RouteGroupBuilder group, string pattern)
    {
        group.MapGet("/", (HttpContext context) =>
        {
            // The UI uses <base href> so assets and API calls work at any mount path,
            // with or without a trailing slash, and behind a PathBase.
            var basePath = context.Request.PathBase.Add(new PathString(pattern)).Value ?? pattern;
            var html = IndexTemplate.Value.Replace("%GRIDLET_BASE%", basePath.TrimEnd('/'));
            return Results.Content(html, "text/html; charset=utf-8");
        }).ExcludeFromDescription();

        group.MapGet("/assets/{**assetPath}", (string assetPath) =>
        {
            var file = Files.GetFileInfo("assets/" + assetPath);
            if (!file.Exists || file.IsDirectory)
            {
                return Results.NotFound();
            }

            if (!ContentTypes.TryGetContentType(assetPath, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return Results.Stream(file.CreateReadStream(), contentType);
        }).ExcludeFromDescription();
    }
}
