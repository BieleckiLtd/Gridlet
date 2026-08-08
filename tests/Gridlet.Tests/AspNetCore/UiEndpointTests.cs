using System.Net;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class UiEndpointTests
{
    [Fact]
    public async Task Embedded_assets_use_private_conditional_revalidation()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var first = await client.GetAsync("/gridlet/assets/icon_sm.png");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(first.Headers.ETag);
        Assert.True(first.Headers.CacheControl!.Private);
        Assert.True(first.Headers.CacheControl.MustRevalidate);
        Assert.Equal(TimeSpan.Zero, first.Headers.CacheControl.MaxAge);
        Assert.NotEmpty(await first.Content.ReadAsByteArrayAsync());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/gridlet/assets/icon_sm.png");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag);
        var revalidated = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
        Assert.Equal(first.Headers.ETag, revalidated.Headers.ETag);
        Assert.Empty(await revalidated.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Non_matching_asset_etag_returns_the_full_representation()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/gridlet/assets/icon_sm.png");
        request.Headers.IfNoneMatch.ParseAdd("\"different-representation\"");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Generated_index_is_not_cached()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync("/gridlet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Null(response.Headers.ETag);
    }
}
