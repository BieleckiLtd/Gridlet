using System.Security.Cryptography;
using System.Text;
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

    private static readonly string AssetVersion =
        typeof(GridletUiEndpoints).Assembly.ManifestModule.ModuleVersionId.ToString("N");

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
            // The generated base path can vary by request and deployment. Never let a browser or
            // intermediary reuse one mount's HTML for another mount.
            context.Response.Headers.CacheControl = "no-store";
            return Results.Content(html, "text/html; charset=utf-8");
        }).ExcludeFromDescription();

        group.MapGet("/assets/{**assetPath}", (string assetPath, HttpContext context) =>
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

            var etag = GetAssetETag(assetPath);
            context.Response.Headers.ETag = etag;
            // Assets are invariant within an assembly, but their stable filenames are not content
            // hashed. Require browser revalidation and keep authenticated assets out of shared caches.
            context.Response.Headers.CacheControl = "private, max-age=0, must-revalidate";

            if (MatchesETag(context.Request.Headers.IfNoneMatch, etag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return Results.Stream(file.CreateReadStream(), contentType);
        }).ExcludeFromDescription();
    }

    private static string GetAssetETag(string assetPath)
    {
        var pathHash = SHA256.HashData(Encoding.UTF8.GetBytes(assetPath));
        return $"\"{AssetVersion}-{Convert.ToHexString(pathHash.AsSpan(0, 8))}\"";
    }

    private static bool MatchesETag(Microsoft.Extensions.Primitives.StringValues candidates, string etag)
    {
        foreach (var value in candidates)
        {
            foreach (var candidate in value?.Split(',') ?? [])
            {
                var trimmed = candidate.Trim();
                if (trimmed == "*" ||
                    trimmed.Equals(etag, StringComparison.Ordinal) ||
                    trimmed.Equals("W/" + etag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
