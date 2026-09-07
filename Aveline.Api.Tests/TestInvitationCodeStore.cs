using Aveline.Api.Modules.Organizations.Services;

namespace Aveline.Api.Tests;

/// <summary>In-memory <see cref="IInvitationCodeStore"/> for unit tests.</summary>
public sealed class FakeInvitationCodeStore : IInvitationCodeStore
{
    private readonly Dictionary<string, Guid> _map = new(StringComparer.Ordinal);

    public bool ThrowOnStore { get; set; }
    public bool ThrowOnRemove { get; set; }
    public int RemoveCount { get; private set; }

    public Task StoreAsync(string code, Guid invitationId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (ThrowOnStore)
        {
            throw new InvalidOperationException("simulated code-store outage");
        }

        _map[code] = invitationId;
        return Task.CompletedTask;
    }

    public Task<Guid?> GetAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult(_map.TryGetValue(code, out var id) ? id : (Guid?)null);

    public Task RemoveAsync(string code, CancellationToken cancellationToken = default)
    {
        if (ThrowOnRemove)
        {
            throw new InvalidOperationException("simulated remove outage");
        }

        RemoveCount++;
        _map.Remove(code);
        return Task.CompletedTask;
    }

    /// <summary>Exposes stored codes for assertions.</summary>
    public bool Contains(string code) => _map.ContainsKey(code);
}
