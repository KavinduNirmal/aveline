using System.Net;

namespace Aveline.Api.Modules.Conversations.Media;

/// <summary>
/// Resolves a host name to the addresses the fetcher will judge before it connects. DNS is a
/// seam, not an implementation detail: the fetcher must resolve the host itself (salon §7.5
/// item 5) rather than let the HTTP stack silently resolve it a second time, and pinning needs
/// the exact address the validation approved (item 6).
/// </summary>
public interface IImageUrlHostResolver
{
    /// <summary>Every address the host currently resolves to. Order is not significant.</summary>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

/// <summary>
/// The production resolver: <see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// Resolution happens exactly once per hop, and the address it returns is the address the socket
/// connects to. There is no second lookup between validation and connect, so a DNS answer that
/// changes after validation cannot redirect the connection.
/// </remarks>
internal sealed class DnsImageUrlHostResolver : IImageUrlHostResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        return addresses;
    }
}
