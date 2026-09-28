using System.Net;
using System.Net.Sockets;

namespace Aveline.Api.Modules.Conversations.Media;

/// <summary>
/// Decides whether a resolved address is one the fetcher may contact. The only address this
/// fetcher may reach is a routable public one; every special-use range is refused explicitly,
/// because a "deny the obvious ones" list is how SSRF guards are defeated
/// (salon §7.5 item 5).
/// </summary>
/// <remarks>
/// <para>
/// The list is deliberately the special-purpose registries rather than a hand-picked few:
/// loopback (<c>127/8</c>, <c>::1</c>), RFC1918 private (<c>10/8</c>, <c>172.16/12</c>,
/// <c>192.168/16</c>), link-local (<c>169.254/16</c>, <c>fe80::/10</c> — including the cloud
/// metadata endpoint <c>169.254.169.254</c>), unique-local (<c>fc00::/7</c>), CGNAT
/// (<c>100.64/10</c>), multicast, unspecified, broadcast, benchmarking, documentation and the
/// transition ranges that can embed a private IPv4 address (6to4, NAT64, Teredo).
/// </para>
/// <para>
/// An IPv4-mapped IPv6 address (<c>::ffff:10.0.0.1</c>) is unwrapped and classified as its IPv4
/// self, so the mapped form is not a bypass.
/// </para>
/// <para>
/// Callers must reject the whole resolution when <em>any</em> address is non-public; a single
/// public answer next to a private one is the shape a rebinding answer takes.
/// </para>
/// </remarks>
internal static class ImageUrlAddressPolicy
{
    private static readonly IPNetwork[] NonPublicIPv4 =
    [
        IPNetwork.Parse("0.0.0.0/8"),        // "this network" / unspecified
        IPNetwork.Parse("10.0.0.0/8"),       // RFC1918 private
        IPNetwork.Parse("100.64.0.0/10"),    // RFC6598 CGNAT
        IPNetwork.Parse("127.0.0.0/8"),      // loopback
        IPNetwork.Parse("169.254.0.0/16"),   // link-local, incl. 169.254.169.254
        IPNetwork.Parse("172.16.0.0/12"),    // RFC1918 private
        IPNetwork.Parse("192.0.0.0/24"),     // IETF protocol assignments
        IPNetwork.Parse("192.0.2.0/24"),     // TEST-NET-1
        IPNetwork.Parse("192.88.99.0/24"),   // 6to4 relay anycast
        IPNetwork.Parse("192.168.0.0/16"),   // RFC1918 private
        IPNetwork.Parse("198.18.0.0/15"),    // benchmarking
        IPNetwork.Parse("198.51.100.0/24"),  // TEST-NET-2
        IPNetwork.Parse("203.0.113.0/24"),   // TEST-NET-3
        IPNetwork.Parse("224.0.0.0/4"),      // multicast
        IPNetwork.Parse("240.0.0.0/4"),      // reserved, incl. 255.255.255.255
    ];

    private static readonly IPNetwork[] NonPublicIPv6 =
    [
        IPNetwork.Parse("::/128"),           // unspecified
        IPNetwork.Parse("::1/128"),          // loopback
        IPNetwork.Parse("::ffff:0:0/96"),    // IPv4-mapped (unwrapped above, kept as a belt)
        IPNetwork.Parse("64:ff9b::/96"),     // NAT64, can embed a private IPv4
        IPNetwork.Parse("100::/64"),         // discard-only
        IPNetwork.Parse("2001::/32"),        // Teredo, can embed a private IPv4
        IPNetwork.Parse("2001:20::/28"),     // ORCHIDv2
        IPNetwork.Parse("2001:db8::/32"),    // documentation
        IPNetwork.Parse("2002::/16"),        // 6to4, can embed a private IPv4
        IPNetwork.Parse("3fff::/20"),        // documentation (RFC9637)
        IPNetwork.Parse("5f00::/16"),        // SRv6 SIDs
        IPNetwork.Parse("fc00::/7"),         // unique-local
        IPNetwork.Parse("fe80::/10"),        // link-local
        IPNetwork.Parse("fec0::/10"),        // deprecated site-local
        IPNetwork.Parse("ff00::/8"),         // multicast
    ];

    /// <summary>Whether the address is a routable public one the fetcher may contact.</summary>
    public static bool IsPublic(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork =>
                !NonPublicIPv4.Any(network => network.Contains(address)),
            AddressFamily.InterNetworkV6 =>
                !NonPublicIPv6.Any(network => network.Contains(address)),
            _ => false,
        };
    }
}
