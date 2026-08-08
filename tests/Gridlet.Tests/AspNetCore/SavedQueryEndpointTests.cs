using System.Net;
using System.Net.Http.Json;
using Gridlet.Models;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class SavedQueryEndpointTests
{
    [Fact]
    public async Task Saved_queries_roundtrip_through_the_store()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var saved = await (await client.PostAsJsonAsync("/gridlet/api/queries",
            new { name = "Top customers", connectionName = "Main", database = "FakeDb", sql = "SELECT 1" }))
            .Content.ReadFromJsonAsync<SavedQuery>();
        Assert.NotNull(saved);

        var list = await client.GetFromJsonAsync<List<SavedQuery>>("/gridlet/api/queries");
        Assert.Single(list!);
        Assert.Equal("Top customers", list![0].Name);

        // Overwrite by id keeps a single entry.
        await client.PostAsJsonAsync("/gridlet/api/queries",
            new { id = saved!.Id, name = "Top customers", connectionName = "Main", sql = "SELECT 2" });
        list = await client.GetFromJsonAsync<List<SavedQuery>>("/gridlet/api/queries");
        Assert.Single(list!);
        Assert.Equal("SELECT 2", list![0].Sql);

        var delete = await client.DeleteAsync($"/gridlet/api/queries/{saved.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        list = await client.GetFromJsonAsync<List<SavedQuery>>("/gridlet/api/queries");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Saving_requires_name_and_sql()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/gridlet/api/queries",
            new { name = "", connectionName = "Main", sql = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Failed_store_write_does_not_leak_the_mutation_into_cached_state()
    {
        var destinationDirectory = Path.Combine(
            Path.GetTempPath(), $"gridlet-store-destination-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destinationDirectory);
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", GridletProviderNames.SqlServer);
            options.Security.AllowAnonymous = true;
            // A directory cannot be replaced by the store's completed temporary file.
            options.Storage.FilePath = destinationDirectory;
        });

        try
        {
            var response = await client.PostAsJsonAsync("/gridlet/api/queries",
                new { name = "Must not leak", connectionName = "Main", sql = "SELECT 1" });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            var list = await client.GetFromJsonAsync<List<SavedQuery>>("/gridlet/api/queries");
            Assert.Empty(list!);
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(destinationDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Replacing_an_existing_store_preserves_unix_permission_bits()
    {
        if (OperatingSystem.IsWindows()) return;

        var storePath = Path.Combine(Path.GetTempPath(), $"gridlet-store-mode-{Guid.NewGuid():N}.json");
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", GridletProviderNames.SqlServer);
            options.Security.AllowAnonymous = true;
            options.Storage.FilePath = storePath;
        });

        try
        {
            var first = await client.PostAsJsonAsync("/gridlet/api/queries",
                new { name = "First", connectionName = "Main", sql = "SELECT 1" });
            first.EnsureSuccessStatusCode();

            var expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
            File.SetUnixFileMode(storePath, expectedMode);

            var second = await client.PostAsJsonAsync("/gridlet/api/queries",
                new { name = "Second", connectionName = "Main", sql = "SELECT 2" });
            second.EnsureSuccessStatusCode();

            Assert.Equal(expectedMode, File.GetUnixFileMode(storePath));
        }
        finally
        {
            await app.DisposeAsync();
            if (File.Exists(storePath)) File.Delete(storePath);
        }
    }
}
