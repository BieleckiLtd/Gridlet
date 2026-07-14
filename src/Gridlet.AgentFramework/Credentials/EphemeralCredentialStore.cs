using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Gridlet.Models;

namespace Gridlet.AgentFramework;

internal sealed class EphemeralCredentialStore : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _lifetime;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public EphemeralCredentialStore(GridletAgentFrameworkSettings settings)
    {
        _lifetime = settings.CredentialLifetime;
        var cleanupInterval = TimeSpan.FromMilliseconds(
            Math.Clamp(_lifetime.TotalMilliseconds / 4, 1_000, 60_000));
        _cleanupTimer = new Timer(
            static state => ((EphemeralCredentialStore)state!).RemoveExpired(),
            this,
            cleanupInterval,
            cleanupInterval);
    }

    public GridletAgentCredential Store(
        string profileId,
        string apiKey,
        GridletAgentUserContext user)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RemoveExpired();

        var secret = Encoding.UTF8.GetBytes(apiKey);
        while (true)
        {
            var handle = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var expiresAt = DateTimeOffset.UtcNow.Add(_lifetime);
            var entry = new Entry(
                profileId,
                RequireOwner(user),
                expiresAt,
                secret);
            if (_entries.TryAdd(handle, entry))
            {
                return new GridletAgentCredential(handle, expiresAt);
            }
        }
    }

    public string? Resolve(
        string handle,
        string profileId,
        GridletAgentUserContext user)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_entries.TryGetValue(handle, out var entry))
        {
            return null;
        }

        lock (entry)
        {
            if (entry.IsCleared || entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Remove(handle, entry);
                return null;
            }
            if (!string.Equals(entry.ProfileId, profileId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.Owner, RequireOwner(user), StringComparison.Ordinal))
            {
                return null;
            }

            return Encoding.UTF8.GetString(entry.Secret);
        }
    }

    public bool Remove(string handle, GridletAgentUserContext user)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_entries.TryGetValue(handle, out var entry))
        {
            return false;
        }

        lock (entry)
        {
            if (!string.Equals(entry.Owner, RequireOwner(user), StringComparison.Ordinal))
            {
                return false;
            }
            return Remove(handle, entry);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cleanupTimer.Dispose();
        foreach (var pair in _entries)
        {
            Remove(pair.Key, pair.Value);
        }
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                lock (pair.Value)
                {
                    if (pair.Value.ExpiresAt <= now)
                    {
                        Remove(pair.Key, pair.Value);
                    }
                }
            }
        }
    }

    private bool Remove(string handle, Entry expected)
    {
        if (!_entries.TryRemove(new KeyValuePair<string, Entry>(handle, expected)))
        {
            return false;
        }

        expected.Clear();
        return true;
    }

    private static string RequireOwner(GridletAgentUserContext user)
    {
        if (user.IsAuthenticated && string.IsNullOrWhiteSpace(user.Subject))
        {
            throw new GridletAgentException(
                "The authenticated user has no stable identifier for agent credentials.");
        }

        return user.Subject ?? "\0explicit-anonymous";
    }

    private sealed class Entry(
        string profileId,
        string owner,
        DateTimeOffset expiresAt,
        byte[] secret)
    {
        public string ProfileId { get; } = profileId;
        public string Owner { get; } = owner;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public byte[] Secret { get; } = secret;
        public bool IsCleared { get; private set; }

        public void Clear()
        {
            if (IsCleared)
            {
                return;
            }
            CryptographicOperations.ZeroMemory(Secret);
            IsCleared = true;
        }
    }
}
