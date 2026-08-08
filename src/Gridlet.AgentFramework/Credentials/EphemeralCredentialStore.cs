using System.Security.Cryptography;
using System.Text;
using Gridlet.Models;

namespace Gridlet.AgentFramework;

internal sealed class EphemeralCredentialStore : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _lifetime;
    private readonly int _maxCredentials;
    private readonly int _maxCredentialsPerOwner;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public EphemeralCredentialStore(GridletAgentFrameworkSettings settings)
    {
        _lifetime = settings.CredentialLifetime;
        _maxCredentials = settings.MaxEphemeralCredentials;
        _maxCredentialsPerOwner = settings.MaxEphemeralCredentialsPerOwner;
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
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RemoveExpiredCore(DateTimeOffset.UtcNow);

            var owner = RequireOwner(user);
            if (_entries.Count >= _maxCredentials)
            {
                throw new GridletAgentException(
                    "This application has reached its active agent credential limit. " +
                    "Remove an existing credential or wait for one to expire.");
            }
            if (_entries.Values.Count(entry =>
                    string.Equals(entry.Owner, owner, StringComparison.Ordinal)) >=
                _maxCredentialsPerOwner)
            {
                throw new GridletAgentException(
                    "This user has reached the active agent credential limit. " +
                    "Remove an existing credential or wait for one to expire.");
            }

            var secret = Encoding.UTF8.GetBytes(apiKey);
            while (true)
            {
                var handle = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
                var expiresAt = DateTimeOffset.UtcNow.Add(_lifetime);
                var entry = new Entry(profileId, owner, expiresAt, secret);
                if (_entries.TryAdd(handle, entry))
                {
                    return new GridletAgentCredential(handle, expiresAt);
                }
            }
        }
    }

    public string? Resolve(
        string handle,
        string profileId,
        GridletAgentUserContext user)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(handle, out var entry))
            {
                return null;
            }

            if (entry.IsCleared || entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                RemoveCore(handle, entry);
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
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(handle, out var entry))
            {
                return false;
            }

            if (!string.Equals(entry.Owner, RequireOwner(user), StringComparison.Ordinal))
            {
                return false;
            }
            return RemoveCore(handle, entry);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cleanupTimer.Dispose();
            foreach (var entry in _entries.Values)
            {
                entry.Clear();
            }
            _entries.Clear();
        }
    }

    private void RemoveExpired()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            RemoveExpiredCore(DateTimeOffset.UtcNow);
        }
    }

    private void RemoveExpiredCore(DateTimeOffset now)
    {
        foreach (var pair in _entries.Where(pair => pair.Value.ExpiresAt <= now).ToArray())
        {
            RemoveCore(pair.Key, pair.Value);
        }
    }

    private bool RemoveCore(string handle, Entry expected)
    {
        if (!_entries.TryGetValue(handle, out var entry) || !ReferenceEquals(entry, expected))
        {
            return false;
        }

        _entries.Remove(handle);
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
