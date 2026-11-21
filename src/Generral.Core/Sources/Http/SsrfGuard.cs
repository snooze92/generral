using System.Net;
using System.Net.Sockets;

namespace Generral.Core.Sources.Http;

/// <summary>
/// Blocks source requests to link-local addresses (169.254.0.0/16, fe80::/10), where cloud metadata
/// services live. Everything else, including private ranges, stays reachable: sources may fetch arbitrary URLs.
/// </summary>
public static class SsrfGuard
{
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => address.GetAddressBytes() is [169, 254, ..],
            AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal,
            _ => false,
        };
    }

    /// <summary>Rejects URLs whose host is a blocked IP literal, before any connection is made.</summary>
    /// <exception cref="HttpRequestException">The host is blocked.</exception>
    public static void EnsureAllowed(Uri url)
    {
        if (IPAddress.TryParse(url.IdnHost.Trim('[', ']'), out var address) && IsBlocked(address))
        {
            throw Blocked(url.IdnHost);
        }
    }

    /// <summary>A <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves the host and only connects to allowed addresses.</summary>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> ConnectCallback(
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        resolve ??= Dns.GetHostAddressesAsync;
        return async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? [literal] : await resolve(host, cancellationToken);
            var allowed = addresses.Where(a => !IsBlocked(a)).ToArray();
            if (allowed.Length == 0)
            {
                throw Blocked(host);
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
    }

    private static HttpRequestException Blocked(string host) =>
        new($"Requests to link-local addresses are blocked ({host})");
}
