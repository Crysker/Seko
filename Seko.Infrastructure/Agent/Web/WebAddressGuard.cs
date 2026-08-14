using System.Net;

namespace Seko.Infrastructure.Agent.Web;

public sealed class WebAddressGuard
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolver;

    public WebAddressGuard(
        Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
    {
        _resolver =
            resolver
            ?? ResolveDefaultAsync;
    }

    public async Task<Uri> ValidateAsync(
        string rawUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                rawUrl))
        {
            throw new ArgumentException(
                "Web URL cannot be empty.",
                nameof(rawUrl));
        }

        if (rawUrl.Length > 2_048)
        {
            throw new ArgumentException(
                "Web URL is too long.",
                nameof(rawUrl));
        }

        if (!Uri.TryCreate(
                rawUrl.Trim(),
                UriKind.Absolute,
                out var uri))
        {
            throw new ArgumentException(
                "Web URL must be an absolute HTTP or HTTPS URL.",
                nameof(rawUrl));
        }

        if (!uri.Scheme.Equals(
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only HTTP and HTTPS URLs are allowed.");
        }

        if (!string.IsNullOrEmpty(
                uri.UserInfo))
        {
            throw new InvalidOperationException(
                "URLs containing embedded credentials are not allowed.");
        }

        var isHttps =
            uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);

        var expectedPort =
            isHttps
                ? 443
                : 80;

        var effectivePort =
            uri.IsDefaultPort
                ? expectedPort
                : uri.Port;

        if (effectivePort
            != expectedPort)
        {
            throw new InvalidOperationException(
                isHttps
                    ? "HTTPS web access is limited to port 443."
                    : "HTTP web access is limited to port 80.");
        }

        await ResolvePublicAddressesAsync(
            uri.DnsSafeHost,
            cancellationToken);

        return uri;
    }

    public async Task<IReadOnlyCollection<IPAddress>> ResolvePublicAddressesAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                host))
        {
            throw new InvalidOperationException(
                "Web host cannot be empty.");
        }

        var normalizedHost =
            host
                .Trim()
                .TrimEnd('.');

        if (IsObviouslyLocalHost(
                normalizedHost))
        {
            throw new InvalidOperationException(
                $"Web host '{normalizedHost}' is local/private and is blocked.");
        }

        IPAddress[] addresses;

        if (IPAddress.TryParse(
                normalizedHost,
                out var literalAddress))
        {
            addresses =
                new[]
                {
                    literalAddress
                };
        }
        else
        {
            if (!normalizedHost.Contains(
                    ".",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Single-label host names are blocked because they can resolve to local network resources.");
            }

            addresses =
                await _resolver(
                    normalizedHost,
                    cancellationToken);
        }

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException(
                $"Web host '{normalizedHost}' did not resolve to an address.");
        }

        foreach (var address
                 in addresses)
        {
            if (!IsPublicAddress(
                    address))
            {
                throw new InvalidOperationException(
                    $"Web host '{normalizedHost}' resolved to a private, loopback, link-local or reserved address and was blocked.");
            }
        }

        return
            addresses
                .Distinct()
                .ToList()
                .AsReadOnly();
    }

    public static bool IsPublicAddress(
        IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(
            address);

        if (address.IsIPv4MappedToIPv6)
        {
            address =
                address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(
                address))
        {
            return false;
        }

        var bytes =
            address.GetAddressBytes();

        if (address.AddressFamily
            == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var first =
                bytes[0];

            var second =
                bytes[1];

            if (first == 0
                || first == 10
                || first == 127
                || first >= 224)
            {
                return false;
            }

            if (first == 100
                && second is >= 64 and <= 127)
            {
                return false;
            }

            if (first == 169
                && second == 254)
            {
                return false;
            }

            if (first == 172
                && second is >= 16 and <= 31)
            {
                return false;
            }

            if (first == 192
                && second == 168)
            {
                return false;
            }

            if (first == 192
                && second == 0)
            {
                return false;
            }

            if (first == 198
                && second is 18 or 19)
            {
                return false;
            }

            if (first == 198
                && second == 51
                && bytes[2] == 100)
            {
                return false;
            }

            if (first == 203
                && second == 0
                && bytes[2] == 113)
            {
                return false;
            }

            return true;
        }

        if (address.AddressFamily
            == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (address.Equals(
                    IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6Multicast)
            {
                return false;
            }

            // Unique-local fc00::/7.
            if ((bytes[0] & 0xFE)
                == 0xFC)
            {
                return false;
            }

            // Deprecated site-local fec0::/10.
            if (bytes[0] == 0xFE
                && (bytes[1] & 0xC0) == 0xC0)
            {
                return false;
            }

            // Documentation prefix 2001:db8::/32.
            if (bytes[0] == 0x20
                && bytes[1] == 0x01
                && bytes[2] == 0x0D
                && bytes[3] == 0xB8)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool IsObviouslyLocalHost(
        string host)
    {
        return
            host.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(
                ".localhost",
                StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(
                ".local",
                StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(
                ".internal",
                StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IPAddress[]> ResolveDefaultAsync(
        string host,
        CancellationToken cancellationToken)
    {
        return
            await Dns.GetHostAddressesAsync(
                    host)
                .WaitAsync(
                    cancellationToken);
    }
}
