using System.Net;
using System.Net.Sockets;

namespace SignalRadar.Infrastructure.Summaries;

internal sealed class PublicHttpTargetValidator
{
    private readonly bool _allowPrivateNetworkTargets;

    public PublicHttpTargetValidator(bool allowPrivateNetworkTargets)
    {
        _allowPrivateNetworkTargets = allowPrivateNetworkTargets;
    }

    public async ValueTask ValidateAsync(
        Uri target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsAbsoluteUri || target.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Article content targets must use absolute HTTP or HTTPS URLs.");
        }

        if (_allowPrivateNetworkTargets)
        {
            return;
        }

        string host = target.DnsSafeHost;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Private-network article targets are disabled.");
        }

        IPAddress[] addresses;

        if (IPAddress.TryParse(host, out IPAddress? literalAddress))
        {
            addresses = [literalAddress];
        }
        else
        {
            try
            {
                addresses = await Dns
                    .GetHostAddressesAsync(host, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                throw new HttpRequestException(
                    $"The article host '{host}' could not be resolved.",
                    exception);
            }
        }

        if (addresses.Length == 0)
        {
            throw new HttpRequestException(
                $"The article host '{host}' resolved to no addresses.");
        }

        for (int index = 0; index < addresses.Length; index++)
        {
            if (!IsPublicAddress(addresses[index]))
            {
                throw new InvalidOperationException(
                    "Private-network article targets are disabled.");
            }
        }
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] != 0
                && bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && bytes[0] < 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] bytes = address.GetAddressBytes();
            return !address.Equals(IPAddress.IPv6Any)
                && !address.Equals(IPAddress.IPv6Loopback)
                && !address.IsIPv6LinkLocal
                && !address.IsIPv6Multicast
                && (bytes[0] & 0xFE) != 0xFC;
        }

        return false;
    }
}
