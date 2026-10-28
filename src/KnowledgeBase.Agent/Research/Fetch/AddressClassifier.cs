using System.Net;
using System.Net.Sockets;

namespace KnowledgeBase.Agent.Research.Fetch;

/// <summary>
/// Decides whether the page fetcher may connect to an address. Only public internet addresses are allowed, so a search
/// result can never point the fetcher at the VM itself, its network or a cloud metadata service.
/// </summary>
public static class AddressClassifier
{
    // Every IPv4 range that is not ordinary public unicast (RFC 6890 and friends).
    private static readonly IPNetwork[] RefusedV4 =
    [
        IPNetwork.Parse("0.0.0.0/8"),       // "this network", including 0.0.0.0, which reaches the local host
        IPNetwork.Parse("10.0.0.0/8"),      // private
        IPNetwork.Parse("100.64.0.0/10"),   // carrier-grade NAT, including Alibaba's metadata at 100.100.100.200
        IPNetwork.Parse("127.0.0.0/8"),     // loopback
        IPNetwork.Parse("169.254.0.0/16"),  // link-local, including the 169.254.169.254 metadata service
        IPNetwork.Parse("172.16.0.0/12"),   // private
        IPNetwork.Parse("192.0.0.0/24"),    // IETF protocol assignments
        IPNetwork.Parse("192.0.2.0/24"),    // documentation
        IPNetwork.Parse("192.88.99.0/24"),  // deprecated 6to4 relays
        IPNetwork.Parse("192.168.0.0/16"),  // private
        IPNetwork.Parse("198.18.0.0/15"),   // benchmarking
        IPNetwork.Parse("198.51.100.0/24"), // documentation
        IPNetwork.Parse("203.0.113.0/24"),  // documentation
        IPNetwork.Parse("224.0.0.0/4"),     // multicast
        IPNetwork.Parse("240.0.0.0/4")      // reserved, including the 255.255.255.255 broadcast
    ];

    // Only global unicast is allowed for IPv6, which leaves out loopback, unique-local (fc00::/7, including the
    // fd00:ec2::254 metadata service), link-local, site-local and multicast at once.
    private static readonly IPNetwork GlobalUnicastV6 = IPNetwork.Parse("2000::/3");

    private static readonly IPNetwork[] RefusedV6 =
    [
        IPNetwork.Parse("2001::/32"),     // Teredo, which tunnels to an IPv4 address it hides
        IPNetwork.Parse("2001:db8::/32"), // documentation
        IPNetwork.Parse("2001:2::/48"),   // benchmarking
        IPNetwork.Parse("3fff::/20")      // documentation
    ];

    // Ranges that carry an IPv4 address, which is what is really reached and so what is classified.
    private static readonly IPNetwork Nat64 = IPNetwork.Parse("64:ff9b::/96");
    private static readonly IPNetwork SixToFour = IPNetwork.Parse("2002::/16");

    /// <summary>Whether <paramref name="address"/> is a public internet address the fetcher may connect to.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublic(address.MapToIPv4());
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !RefusedV4.Any(network => network.Contains(address)),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),
            _ => false
        };
    }

    private static bool IsPublicV6(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        if (Nat64.Contains(address))
        {
            return IsPublic(new IPAddress(bytes[12..16]));
        }

        if (SixToFour.Contains(address))
        {
            return IsPublic(new IPAddress(bytes[2..6]));
        }

        return GlobalUnicastV6.Contains(address) && !RefusedV6.Any(network => network.Contains(address));
    }
}
