using System.Net;
using System.Net.Sockets;
using Helios.Application.Abstractions.Webhooks;

namespace Helios.Infrastructure.Webhooks;

/// <summary>
/// SSRF rules for customer-supplied destinations. HTTPS only; no credentials in the URL; no
/// literal or well-known internal hosts. <see cref="AllowPrivateNetworks"/> exists for local
/// development and tests only and is refused at Production startup.
/// </summary>
public sealed class OutboundUrlPolicy(bool allowPrivateNetworks) : IOutboundUrlPolicy
{
    public bool AllowPrivateNetworks { get; } = allowPrivateNetworks;

    public string? ValidateForRegistration(string url)
    {
        if (url.Length > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "The webhook URL must be an absolute URL of at most 2000 characters.";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(AllowPrivateNetworks && uri.Scheme == Uri.UriSchemeHttp))
        {
            return "Webhook URLs must use https.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "Webhook URLs must not contain credentials.";
        }

        if (AllowPrivateNetworks)
        {
            return null;
        }

        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return "Webhook URLs must point to a public host.";
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal) && IsDisallowed(literal))
        {
            return "Webhook URLs must not point to a private, loopback, link-local or reserved address.";
        }

        return null;
    }

    /// <summary>
    /// True for any address an outbound customer webhook must never reach: loopback, private,
    /// carrier-grade NAT, link-local (including cloud metadata at 169.254.169.254), multicast,
    /// reserved, unspecified, IPv6 unique-local and link-local, and IPv4-mapped forms of all of these.
    /// </summary>
    public static bool IsDisallowed(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                100 => b[1] is >= 64 and <= 127,           // 100.64.0.0/10 carrier-grade NAT
                169 => b[1] == 254,                         // 169.254.0.0/16 link-local, metadata
                172 => b[1] is >= 16 and <= 31,             // 172.16.0.0/12
                192 => (b[1] == 168) || (b[1] == 0 && b[2] is 0 or 2), // 192.168/16, 192.0.0/24, 192.0.2/24
                198 => b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100), // benchmarking, TEST-NET-2
                203 => b[1] == 0 && b[2] == 113,            // TEST-NET-3
                >= 224 => true,                             // multicast, reserved, broadcast
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any) ||
                address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }

            var first = address.GetAddressBytes()[0];
            return (first & 0xFE) == 0xFC;                  // fc00::/7 unique local
        }

        return true;
    }
}
