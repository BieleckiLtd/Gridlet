using System.Collections.Concurrent;
using Gridlet.AgentFramework;
using Gridlet.Models;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class EphemeralCredentialStoreTests
{
    private static readonly GridletAgentUserContext Alice = new("alice", "Alice", true);
    private static readonly GridletAgentUserContext Bob = new("bob", "Bob", true);

    [Fact]
    public void Per_owner_limit_does_not_consume_another_owners_capacity()
    {
        using var store = CreateStore(maxCredentials: 3, maxPerOwner: 2);

        store.Store("profile", "alice-one", Alice);
        store.Store("profile", "alice-two", Alice);

        Assert.Throws<GridletAgentException>(() => store.Store("profile", "alice-three", Alice));
        Assert.NotNull(store.Store("profile", "bob-one", Bob));
    }

    [Fact]
    public void Global_limit_rejects_instead_of_evicting_active_credentials()
    {
        using var store = CreateStore(maxCredentials: 2, maxPerOwner: 2);
        var alice = store.Store("profile", "alice-secret", Alice);
        store.Store("profile", "bob-secret", Bob);

        Assert.Throws<GridletAgentException>(() =>
            store.Store("profile", "charlie-secret", new GridletAgentUserContext("charlie", "Charlie", true)));
        Assert.Equal("alice-secret", store.Resolve(alice.Handle, "profile", Alice));
    }

    [Fact]
    public void Successful_removal_frees_global_and_owner_capacity()
    {
        using var store = CreateStore(maxCredentials: 1, maxPerOwner: 1);
        var credential = store.Store("profile", "first", Alice);

        Assert.True(store.Remove(credential.Handle, Alice));
        var replacement = store.Store("profile", "replacement", Alice);

        Assert.Equal("replacement", store.Resolve(replacement.Handle, "profile", Alice));
    }

    [Fact]
    public void Wrong_owner_removal_does_not_free_capacity()
    {
        using var store = CreateStore(maxCredentials: 1, maxPerOwner: 1);
        var credential = store.Store("profile", "first", Alice);

        Assert.False(store.Remove(credential.Handle, Bob));
        Assert.Throws<GridletAgentException>(() => store.Store("profile", "second", Bob));
        Assert.Equal("first", store.Resolve(credential.Handle, "profile", Alice));
    }

    [Fact]
    public async Task Concurrent_stores_cannot_exceed_the_per_owner_limit()
    {
        using var store = CreateStore(maxCredentials: 20, maxPerOwner: 3);
        var credentials = new ConcurrentBag<GridletAgentCredential>();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(() =>
        {
            try
            {
                credentials.Add(store.Store("profile", $"secret-{index}", Alice));
            }
            catch (GridletAgentException)
            {
                // Rejection is the expected result after the exact quota is reached.
            }
        })));

        Assert.Equal(3, credentials.Count);
        Assert.All(credentials, credential =>
            Assert.NotNull(store.Resolve(credential.Handle, "profile", Alice)));
    }

    [Fact]
    public void Disposal_clears_the_store_and_rejects_further_operations()
    {
        var store = CreateStore(maxCredentials: 1, maxPerOwner: 1);
        var credential = store.Store("profile", "secret", Alice);

        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => store.Store("profile", "new", Alice));
        Assert.Throws<ObjectDisposedException>(() => store.Resolve(credential.Handle, "profile", Alice));
        Assert.Throws<ObjectDisposedException>(() => store.Remove(credential.Handle, Alice));
    }

    [Fact]
    public void Credential_limits_are_validated_during_configuration()
    {
        var options = new GridletAgentFrameworkOptions
        {
            MaxEphemeralCredentials = 2,
            MaxEphemeralCredentialsPerOwner = 3,
        };
        options.AddOllama("local", new Uri("http://localhost:11434"), "model");

        var exception = Assert.Throws<GridletValidationException>(options.Build);

        Assert.Contains(nameof(GridletAgentFrameworkOptions.MaxEphemeralCredentialsPerOwner),
            exception.Message, StringComparison.Ordinal);
    }

    private static EphemeralCredentialStore CreateStore(int maxCredentials, int maxPerOwner)
    {
        var options = new GridletAgentFrameworkOptions
        {
            MaxEphemeralCredentials = maxCredentials,
            MaxEphemeralCredentialsPerOwner = maxPerOwner,
        };
        options.AddOllama("local", new Uri("http://localhost:11434"), "model");
        return new EphemeralCredentialStore(options.Build());
    }
}
